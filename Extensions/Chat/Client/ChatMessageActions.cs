using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    // Owner projection/intent adapter. Native menu options use the public Host binding/guard path.
    internal sealed class ChatMessageActions : IDisposable
    {
        private const int UrlsPerPage = 20;
        private readonly IChatUiHostContext host;
        private readonly IClientPresentationService renderer;
        private readonly IClientExtensionPointService points;
        private IPhinixExtensionModule module;
        private IClientExtensionPointScope scope;
        private IClientActionPoint<ChatMessageActionContext> point;
        private ActionWindow window;

        public ChatMessageActions(IChatUiHostContext host, IClientPresentationService renderer, IClientExtensionPointService points)
        { this.host = host; this.renderer = renderer; this.points = points; }

        internal void Start(IPhinixExtensionModule owner)
        {
            module = owner; scope = points.ForModule(owner);
            point = scope.DefineActions<ChatMessageActionContext>(ChatMessageActionContext.PointId);
            scope.ContributeActions<ChatMessageActionContext>(ChatMessageActionContext.PointId, context =>
            {
                var actions = new List<ClientActionDefinition>();
                if (context.SelectedMediaUrl != null)
                {
                    string url = context.SelectedMediaUrl;
                    actions.Add(new ClientActionDefinition("selected-media", context.CanRetryMedia ? "Phinix_chat_actions_retryImage".Translate().ToString() :
                        string.Format("Phinix_chat_contextMenu_openInBrowser".Translate(), url.Length > 100 ? url.Substring(0, 100) + "..." : url),
                        () => { if (context.CanRetryMedia) { window?.RetryMedia?.Invoke(); window?.Close(); } else Application.OpenURL(url); }));
                }
                int start = context.UrlPage * UrlsPerPage;
                for (int i = start; i < Math.Min(context.Urls.Count, start + UrlsPerPage); i++)
                {
                    string url = context.Urls[i];
                    string label = url.Length > 100 ? url.Substring(0, 100) + "..." : url;
                    actions.Add(new ClientActionDefinition("url." + i,
                        string.Format("Phinix_chat_contextMenu_openInBrowser".Translate(), label), () => Application.OpenURL(url)));
                }
                actions.Add(new ClientActionDefinition("copy", "Phinix_chat_contextMenu_copyToClipboard".Translate(),
                    () => GUIUtility.systemCopyBuffer = context.Text));
                actions.Add(new ClientActionDefinition("reply", "Phinix_chat_contextMenu_reply".Translate(), () =>
                {
                    if (window == null) return;
                    host.SetReplyTarget(window.Message); window.Close();
                }));
                if (start > 0) actions.Add(new ClientActionDefinition("previous", L("previousLinks"), () => window?.ChangePage(-1)));
                if (start + UrlsPerPage < context.Urls.Count) actions.Add(new ClientActionDefinition("next", L("nextLinks"), () => window?.ChangePage(1)));
                return actions;
            });
        }
        private static string L(string key) => ("Phinix_chat_actions_" + key).Translate();
        internal void Open(UIChatMessage message, IEnumerable<string> urls, Func<bool> current, string selectedMediaUrl = null, Action retryMedia = null, int urlPage = 0)
        {
            if (module == null || point == null || message == null || !current()) return;
            CloseView();
            var session = renderer.Open(module, "message-actions");
            try { window = new ActionWindow(this, session, message, urls, current, selectedMediaUrl, retryMedia, urlPage); Find.WindowStack.Add(window); }
            catch { session.Dispose(); window = null; throw; }
        }
        internal void CloseView() { window?.Close(); window = null; }
        public void Dispose() { CloseView(); scope?.Dispose(); scope = null; point = null; module = null; }

        // Collect/publish before invoking the native constructor: FloatMenu rejects empty options.
        private sealed class ActionWindow : FloatMenu
        {
            private readonly ActionProjection projection;
            internal UIChatMessage Message => projection.Message;
            internal Action RetryMedia => projection.RetryMedia;
            internal ActionWindow(ChatMessageActions owner, IClientPresentationSession session, UIChatMessage message,
                IEnumerable<string> urls, Func<bool> current, string selectedMediaUrl, Action retryMedia, int urlPage)
                : this(new ActionProjection(owner, session, message, urls, current, selectedMediaUrl, retryMedia, urlPage)) { }
            private ActionWindow(ActionProjection projection) : base(projection.Options)
            { this.projection = projection; }
            internal void ChangePage(int delta) { projection.ChangePage(delta); }
            public override void WindowUpdate()
            {
                base.WindowUpdate();
                if (!projection.IsCurrent) { Close(); return; }
                try { projection.Refresh(); options = projection.Options; }
                catch (Exception error) { Log.Warning("[Phinix Chat] Message actions projection failed: " + error); Close(); }
            }
            public override void PostClose()
            {
                try { projection.Session.Dispose(); }
                finally { if (ReferenceEquals(projection.Owner.window, this)) projection.Owner.window = null; base.PostClose(); }
            }
        }

        private sealed class ActionProjection
        {
            private readonly ChatMessageActions owner;
            private readonly IClientPresentationSession session;
            private readonly Func<bool> messageCurrent;
            private readonly IReadOnlyList<string> urls;
            internal readonly UIChatMessage Message;
            internal readonly Action RetryMedia;
            private readonly string selectedMediaUrl;
            private int urlPage, publishedPage = -1;
            private long revision, contributions = -1;
            private object language;
            private ClientActionExecutionResult result;
            internal List<FloatMenuOption> Options { get; private set; }
            internal IClientPresentationSession Session => session;
            internal ChatMessageActions Owner => owner;
            internal bool IsCurrent => !session.IsClosed && owner.point != null && messageCurrent();
            internal ActionProjection(ChatMessageActions owner, IClientPresentationSession session, UIChatMessage message,
                IEnumerable<string> urls, Func<bool> current, string selectedMediaUrl, Action retryMedia, int urlPage)
            {
                this.urlPage = urlPage;
                this.selectedMediaUrl = selectedMediaUrl; RetryMedia = retryMedia;
                this.owner = owner; this.session = session; messageCurrent = current;
                this.urls = new List<string>(urls).AsReadOnly();
                Message = new UIChatMessage(message.MessageId, message.SenderUuid, message.Message, message.Timestamp, message.Status, message.User, message.Source)
                { ReplyToMessageId = message.ReplyToMessageId, ReplyToSnippet = message.ReplyToSnippet,
                    IsNotice = message.IsNotice, MentionedUuids = message.MentionedUuids == null ? null : new List<string>(message.MentionedUuids) };
                Refresh();
            }
            internal void ChangePage(int delta)
            {
                int next = Math.Max(0, Math.Min((Math.Max(1, urls.Count) - 1) / UrlsPerPage, urlPage + delta));
                owner.Open(Message, urls, messageCurrent, selectedMediaUrl, RetryMedia, next);
            }
            internal void Refresh()
            {
                long nextPoints = owner.points.Revision; var nextLanguage = LanguageDatabase.activeLanguage; var nextResult = session.LastActionResult;
                int nextPage = urlPage;
                if (contributions == nextPoints && ReferenceEquals(language, nextLanguage) && ReferenceEquals(result, nextResult) && publishedPage == nextPage) return;
                Func<bool> current = () => !session.IsClosed && owner.point != null && messageCurrent() &&
                    owner.points.Revision == nextPoints && ReferenceEquals(LanguageDatabase.activeLanguage, nextLanguage) &&
                    ReferenceEquals(session.LastActionResult, nextResult) && nextPage == urlPage;
                var actions = owner.point.Collect(new ChatMessageActionContext(Message.MessageId, Message.SenderUuid, Message.Message, Message.Source, Message.Status, urls, nextPage, selectedMediaUrl, RetryMedia != null), current);
                try
                {
                    var bindings = new List<ClientPresentationAction>();
                    foreach (var action in actions.Actions) bindings.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action));
                    string preview = TextHelper.StripRichText(Message.Message ?? "");
                    if (preview.Length > 4000) preview = preview.Substring(0, 4000) + "…";
                    session.Publish(new ClientPresentationPage("message-actions", L("title"), ++revision,
                        new[] { new ClientPresentationSection("message", "", preview, bindings) }), actions, current);
                }
                catch { actions.Dispose(); throw; }
                Options = ClientPresentationMenus.ToGameOptions(session.CreateActionMenuItems());
                if (Options.Count == 0) throw new InvalidOperationException("ChatMessageActionsMissing");
                contributions = nextPoints; language = nextLanguage; result = nextResult; publishedPage = nextPage;
            }
        }
    }
}
