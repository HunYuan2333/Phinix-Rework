using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    // Business projection and intent adapter. The same Host renderer used by Example draws every control.
    internal sealed class ChatSidebarProvider : IServerSidebarProvider, IResponsiveSidebarProvider, IClientNavigationMetadata, IClientViewLifecycle, IDisposable
    {
        private readonly IChatUiHostContext host;
        private readonly IClientSessionContext connection;
        private readonly IClientSettingsContext settings;
        private readonly UserList users;
        private readonly IClientPresentationService renderer;
        private readonly IClientExtensionPointService points;
        private readonly IClientApiDirectory directory;
        private IPhinixExtensionModule module;
        private IClientExtensionPointScope scope;
        private IClientActionPoint<ChatMemberActionContext> point;
        private IClientPresentationSession view;
        private MemberActionWindow memberWindow;
        private IClientApiProvider<ITradeRequestApi> trade;
        private string filter = "", selected;
        private long revision, memberVersion = -1, pointVersion = -1;
        private object language;
        private bool online, collapsed, formatting, inputChanged;
        private long themeVersion = -1;
        private ClientActionExecutionResult lastAction;
        private readonly Dictionary<string, ImmutableUser> visible = new Dictionary<string, ImmutableUser>();

        public ChatSidebarProvider(IChatUiHostContext hostContext, IClientSessionContext sessionContext, UserList userList,
            IClientPresentationService renderer, IClientExtensionPointService points, IClientApiDirectory directory, IClientSettingsContext settings)
        { host = hostContext; connection = sessionContext; users = userList; this.renderer = renderer; this.points = points; this.directory = directory; this.settings = settings; }
        internal void Start(IPhinixExtensionModule owner)
        {
            module = owner; users.Start(); scope = points.ForModule(owner);
            point = scope.DefineActions<ChatMemberActionContext>(ChatMemberActionContext.PointId);
            scope.ContributeActions<ChatMemberActionContext>(ChatMemberActionContext.PointId, context =>
            {
                if (context.Uuid == null) return new[] { new ClientActionDefinition("collapse-blocked", L("toggleBlocked"), () => settings.CollapseBlockedUsers = !settings.CollapseBlockedUsers) };
                if (context.Uuid == host.Uuid) return new[] { new ClientActionDefinition("collapse-blocked", L("toggleBlocked"), () => settings.CollapseBlockedUsers = !settings.CollapseBlockedUsers) };
                var tradeProvider = trade;
                bool canTrade = context.Online && tradeProvider != null && tradeProvider.TryGet(out var available);
                return new[] {
                    new ClientActionDefinition("collapse-blocked", L("toggleBlocked"), () => settings.CollapseBlockedUsers = !settings.CollapseBlockedUsers),
                    new ClientActionDefinition("block", (context.Blocked ? "Phinix_chat_contextMenu_unblockUser" : "Phinix_chat_contextMenu_blockUser").Translate(),
                        () => { if (context.Blocked) host.UnBlockUser(context.Uuid); else host.BlockUser(context.Uuid); }),
                    new ClientActionDefinition("trade", "Phinix_chat_contextMenu_tradeWith".Translate(TextHelper.StripRichText(context.DisplayName)),
                        () => { if (tradeProvider != null && tradeProvider.TryGet(out var api)) api.CreateTrade(context.Uuid); }, canTrade ? null : !context.Online ? "Phinix_chat_notConnected".Translate().ToString() : L("tradeUnavailable"))
                };
            });
        }
        private static string L(string key) => ("Phinix_members_" + key).Translate();
        public float Order => 0;
        public float PreferredWidth => 270;
        public float MinimumWidth => 180;
        public bool CanCollapse => true;
        public string NavigationId => "members";
        public string TabLabel => "Phinix_sidebar_users".Translate();
        public void ViewOpened()
        {
            if (module == null || view != null && !view.IsClosed) return;
            view = renderer.Open(module, "members"); memberVersion = -1; ViewUpdate();
        }
        public void ViewClosed() { memberWindow?.Close(); memberWindow = null; var previous = view; view = null; previous?.Dispose(); }
        public void ViewUpdate()
        {
            if (view == null || view.IsClosed) return;
            string nextFilter = filter, nextSelected = selected;
            while (view.TryDequeueInput(out var input))
            {
                if (input.Id == "member-filter") nextFilter = input.Value;
                if (input.Id == "members-list") nextSelected = input.Value;
            }
            inputChanged = nextFilter != filter || nextSelected != selected;
            filter = nextFilter; selected = nextSelected;
            long version = users.MemberRevision, contributions = points.Revision;
            bool connected = connection.Authenticated && connection.LoggedIn;
            var nextTrade = FindTrade();
            bool tradeChanged = trade?.RegistrationId != nextTrade?.RegistrationId;
            if (!inputChanged && version == memberVersion && connected == online && contributions == pointVersion &&
                ReferenceEquals(language, LanguageDatabase.activeLanguage) && collapsed == settings.CollapseBlockedUsers && formatting == host.ShowNameFormatting &&
                themeVersion == ChatTheme.Version && !tradeChanged && ReferenceEquals(lastAction, view.LastActionResult)) return;
            trade = nextTrade;
            visible.Clear(); var rows = new List<ClientPresentationRow>();
            if (connected)
            {
                foreach (var user in users.CaptureMembers(false)) AddRow(user, false, rows);
                if (!settings.CollapseBlockedUsers) foreach (var user in users.CaptureMembers(true)) AddRow(user, true, rows);
            }
            if (selected != null && !visible.ContainsKey(selected)) selected = null;
            ImmutableUser? target = selected == null ? (ImmutableUser?)null : visible[selected];
            bool wasBlocked = target != null && host.BlockedUsers.Contains(target.Value.Uuid);
            long expected = version, expectedPoints = contributions;
            bool expectedCollapse = settings.CollapseBlockedUsers, expectedFormatting = host.ShowNameFormatting;
            object expectedLanguage = LanguageDatabase.activeLanguage;
            string expectedSelection = selected, expectedFilter = filter;
            long expectedTheme = ChatTheme.Version;
            var expectedTrade = trade;
            var actionResult = view.LastActionResult;
            Func<bool> current = () => view != null && !view.IsClosed && expected == users.MemberRevision && expectedPoints == points.Revision &&
                connected == (connection.Authenticated && connection.LoggedIn) && expectedCollapse == settings.CollapseBlockedUsers && expectedFormatting == host.ShowNameFormatting &&
                ReferenceEquals(expectedLanguage, LanguageDatabase.activeLanguage) && expectedTheme == ChatTheme.Version && expectedSelection == selected && expectedFilter == filter &&
                (expectedTrade == null || expectedTrade.TryGet(out var _)) && ReferenceEquals(actionResult, view.LastActionResult);
            var actions = point.Collect(new ChatMemberActionContext(target?.Uuid, target?.DisplayName, wasBlocked, connected), current);
            try
            {
                var bindings = new List<ClientPresentationAction>();
                foreach (var action in actions.Actions) bindings.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action));
                var sections = new List<ClientPresentationSection>();
                string status = connected ? "Phinix_chat_onlineCount".Translate(users.OnlineCount).ToString() : "Phinix_chat_notConnected".Translate().ToString();
                // Selected actions precede the full list so they remain easy to find in narrow sidebars.
                sections.Add(new ClientPresentationSection("member-controls", "", target == null ? L("chooseMember") : TextHelper.StripRichText(target.Value.DisplayName), bindings,
                    new ClientPresentationStatus(status), fields: new[] { new ClientPresentationTextField("member-filter", L("search"), filter, L("search")) }));
                sections.Add(new ClientPresentationSection("member-rows", "", "", list: new ClientPresentationList("members-list", rows,
                    !connected ? "Phinix_chat_notConnected".Translate().ToString() : filter.Length > 0 ? L("noMatches") : L("empty"), selected)));
                view.Publish(new ClientPresentationPage("members", TabLabel, ++revision, sections), actions, current);
            }
            catch { actions.Dispose(); throw; }
            memberVersion = version; pointVersion = contributions; online = connected; language = expectedLanguage;
            collapsed = expectedCollapse; formatting = expectedFormatting; themeVersion = expectedTheme; lastAction = actionResult; inputChanged = false;
        }
        private IClientApiProvider<ITradeRequestApi> FindTrade()
        {
            foreach (var handle in directory.Capture<ITradeRequestApi>()) if (handle.TryGet(out var _)) return handle;
            return null;
        }
        private void AddRow(ImmutableUser user, bool blocked, List<ClientPresentationRow> rows)
        {
            // Preserve the existing invariant-culture, tags-stripped substring match and formatted names.
            if (user.DisplayName.StripTags().IndexOf(filter, StringComparison.InvariantCultureIgnoreCase) < 0) return;
            string id = "user." + EncodeId(user.Uuid);
            visible[id] = user;
            rows.Add(new ClientPresentationRow(id, users.FormatMember(user), blocked ? "Phinix_chat_blockedUsers".Translate().ToString() : "", colorKey: blocked ? "chat.blockedName" : null));
        }
        private static string EncodeId(string value)
        { return BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(value)).Replace("-", ""); }
        internal void OpenMemberActions(ImmutableUser user)
        {
            if (module == null || point == null || !users.TryGetMember(user.Uuid, out var _)) return;
            memberWindow?.Close();
            var session = renderer.Open(module, "member-actions");
            try { memberWindow = new MemberActionWindow(this, user.Uuid, session); Find.WindowStack.Add(memberWindow); }
            catch { session.Dispose(); memberWindow = null; throw; }
        }
        // Host publishes/guards actions before the native menu constructor receives its options.
        private sealed class MemberActionWindow : FloatMenu
        {
            private readonly MemberActionProjection projection;
            internal MemberActionWindow(ChatSidebarProvider owner, string uuid, IClientPresentationSession session)
                : this(new MemberActionProjection(owner, uuid, session)) { }
            private MemberActionWindow(MemberActionProjection projection) : base(projection.Options)
            { this.projection = projection; }
            public override void WindowUpdate()
            {
                base.WindowUpdate();
                if (!projection.IsCurrent) { Close(); return; }
                try { projection.Refresh(); options = projection.Options; }
                catch (Exception error) { Log.Warning("[Phinix Chat] Member actions projection failed: " + error); Close(); }
            }
            public override void PostClose()
            { try { projection.Session.Dispose(); } finally { if (ReferenceEquals(projection.Owner.memberWindow, this)) projection.Owner.memberWindow = null; base.PostClose(); } }
        }
        private sealed class MemberActionProjection
        {
            private readonly ChatSidebarProvider owner;
            private readonly string uuid;
            private readonly IClientPresentationSession session;
            private long revision, contributions = -1;
            private string name;
            private bool blocked, connected;
            private object language;
            private long tradeId = -1;
            private ClientActionExecutionResult result;
            internal List<FloatMenuOption> Options { get; private set; }
            internal IClientPresentationSession Session => session;
            internal ChatSidebarProvider Owner => owner;
            internal bool IsCurrent => !session.IsClosed && owner.point != null && owner.users.TryGetMember(uuid, out var _);
            internal MemberActionProjection(ChatSidebarProvider owner, string uuid, IClientPresentationSession session)
            { this.owner = owner; this.uuid = uuid; this.session = session; Refresh(); }
            internal void Refresh()
            {
                if (!owner.users.TryGetMember(uuid, out var user)) throw new InvalidOperationException("ChatMemberUnavailable");
                string nextName = user.DisplayName;
                bool nextBlocked = owner.host.BlockedUsers.Contains(uuid), nextConnected = owner.host.IsOnline;
                long nextContributions = owner.points.Revision;
                var nextTrade = owner.FindTrade();
                long nextTradeId = nextTrade?.RegistrationId ?? -1;
                object nextLanguage = LanguageDatabase.activeLanguage;
                var nextResult = session.LastActionResult;
                if (name == nextName && blocked == nextBlocked && connected == nextConnected && contributions == nextContributions &&
                    tradeId == nextTradeId && ReferenceEquals(language, nextLanguage) && ReferenceEquals(result, nextResult)) return;
                owner.trade = nextTrade;
                Func<bool> current = () => !session.IsClosed && owner.point != null && owner.users.TryGetMember(uuid, out var now) && now.DisplayName == nextName &&
                    owner.host.BlockedUsers.Contains(uuid) == nextBlocked && owner.host.IsOnline == nextConnected && owner.points.Revision == nextContributions &&
                    ReferenceEquals(LanguageDatabase.activeLanguage, nextLanguage) && ReferenceEquals(session.LastActionResult, nextResult) &&
                    (nextTrade == null || nextTrade.TryGet(out var _));
                var actions = owner.point.Collect(new ChatMemberActionContext(uuid, nextName, nextBlocked, nextConnected), current);
                try
                {
                    var bindings = new List<ClientPresentationAction>();
                    foreach (var action in actions.Actions) bindings.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action));
                    session.Publish(new ClientPresentationPage("member-actions", TextHelper.StripRichText(nextName), ++revision,
                        new[] { new ClientPresentationSection("actions", "", "", bindings) }), actions, current);
                }
                catch { actions.Dispose(); throw; }
                Options = session.CreateActionMenuOptions();
                if (Options.Count == 0) throw new InvalidOperationException("ChatMemberActionsMissing");
                name = nextName; blocked = nextBlocked; connected = nextConnected; contributions = nextContributions; tradeId = nextTradeId; language = nextLanguage; result = nextResult;
            }
        }
        public void Draw(Rect rect) { view?.Draw(rect); }
        public void Dispose() { ViewClosed(); scope?.Dispose(); scope = null; point = null; module = null; }
    }
}
