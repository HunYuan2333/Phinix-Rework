using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    internal sealed class HostNicknameActionContext
    {
        internal readonly IReadOnlyList<ClientActionDefinition> Actions;
        internal HostNicknameActionContext(IEnumerable<ClientActionDefinition> actions) { Actions = actions.ToArray(); }
    }

    // Shares the existing permanent Host principal, renderer and normal typed action service.
    internal sealed class ClientNicknameWindowService : IDisposable
    {
        private readonly ThemePresentationServices host;
        private readonly IClientTheme theme;
        private readonly Func<NicknameFacts> capture;
        private readonly Action<string> persist;
        private readonly Func<string, bool> submit;
        private readonly IClientExtensionPointScope scope;
        internal readonly IClientActionPoint<HostNicknameActionContext> Point;
        private NicknameWindow window;
        internal bool Active { get; private set; } = true;
        internal ClientNicknameWindowService(ThemePresentationServices host, IClientTheme theme,
            Func<NicknameFacts> capture, Action<string> persist, Func<string, bool> submit)
        {
            this.host = host; this.theme = theme; this.capture = capture; this.persist = persist; this.submit = submit;
            scope = host.Points.ForModule(host.Owner);
            try
            {
                Point = scope.DefineActions<HostNicknameActionContext>("builtin.host.nickname.actions");
                scope.ContributeActions<HostNicknameActionContext>("builtin.host.nickname.actions", context => context.Actions);
            }
            catch { scope.Dispose(); throw; }
        }
        internal void Open()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Nickname window requires the game main thread.");
            if (!Active || !host.Active || window?.IsOpen == true) return;
            var next = new NicknameWindow(this, host, theme, capture, new NicknameEditState(capture, persist, submit));
            window = next;
            try { Find.WindowStack.Add(next); }
            catch { window = null; next.Release(); throw; }
        }
        public void Dispose()
        {
            if (!Active) return;
            Active = false;
            try { if (window?.IsOpen == true) window.Close(); else window?.Release(); }
            finally { window = null; scope.Dispose(); }
        }

        private sealed class NicknameWindow : Window
        {
            private readonly ClientNicknameWindowService owner;
            private readonly ThemePresentationServices host;
            private readonly Func<NicknameFacts> capture;
            private readonly NicknameEditState state;
            private readonly string pageId = "nickname." + Guid.NewGuid().ToString("N");
            private IClientPresentationSession view;
            private NicknameFacts published;
            private long revision, pointsVersion = -1;
            private object language;
            private ClientActionExecutionResult result;
            private bool dirty = true;
            internal NicknameWindow(ClientNicknameWindowService owner, ThemePresentationServices host, IClientTheme theme,
                Func<NicknameFacts> capture, NicknameEditState state) : base(new ClientThemeWindowDrawing(() => theme.Capture()))
            {
                this.owner = owner; this.host = host; this.capture = capture; this.state = state;
                doCloseX = true; closeOnAccept = false; closeOnCancel = true; closeOnClickedOutside = false;
                absorbInputAroundWindow = true; draggable = true; resizeable = true;
            }
            public override Vector2 InitialSize => new Vector2(Mathf.Min(620, UiScreenSafeArea.Current.width), Mathf.Min(420, UiScreenSafeArea.Current.height));
            private static string L(string key) => ("Phinix_nickname_" + key).Translate();
            public override void PreOpen()
            {
                base.PreOpen();
                try { view = host.Renderer.Open(host.Owner, pageId); Refresh(); }
                catch { Release(); throw; }
            }
            public override void WindowUpdate()
            {
                base.WindowUpdate(); windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(320, 260));
                if (!owner.Active || !host.Active || view == null || view.IsClosed) { Close(); return; }
                // Read the rendered revision completely before edits invalidate its context.
                var pendingInputs = new List<ClientPresentationInput>();
                while (view.TryDequeueInput(out var queued)) pendingInputs.Add(queued);
                foreach (var input in pendingInputs)
                    if (input.Id == "name") { state.Change(input.Value); dirty = true; }
                Refresh();
            }
            private void Refresh()
            {
                var facts = capture(); var nextResult = view.LastActionResult; var nextLanguage = LanguageDatabase.activeLanguage;
                long pointVersion = host.Points.Revision;
                if (!dirty && published.Equals(facts) && ReferenceEquals(result, nextResult) && ReferenceEquals(language, nextLanguage) && pointVersion == pointsVersion) return;
                long ticket = ++revision;
                Func<bool> current = () => owner.Active && host.Active && IsOpen && !dirty && view != null && !view.IsClosed &&
                    revision == ticket && facts.Equals(capture()) && pointVersion == host.Points.Revision &&
                    ReferenceEquals(nextLanguage, LanguageDatabase.activeLanguage) && ReferenceEquals(nextResult, view.LastActionResult);
                string validation = state.Draft.Length > 4096 ? L("tooLong") : state.Validation == null ? null : L(state.Validation);
                var definitions = new[] {
                    new ClientActionDefinition("save", L("save"), () => { state.Save(); dirty = true; }, state.Committed ? L("alreadySubmitted") : validation),
                    new ClientActionDefinition("cancel", L(state.Committed ? "close" : "cancel"), () => Close())
                };
                var actions = owner.Point.Collect(new HostNicknameActionContext(definitions), current);
                try
                {
                    var bindings = actions.Actions.Select(a => new ClientPresentationAction(a.OwnerId + "." + a.Id, a,
                        a.OwnerId == host.Owner.ExtensionId && a.Id == "save" ? ClientButtonKind.Primary : ClientButtonKind.Normal)).ToArray();
                    var fields = state.Draft.Length > 4096 ? null : new[] { new ClientPresentationTextField("name", L("name"), state.Draft, maxLength: 4096, validationText: validation) };
                    string status = validation ?? L(state.Status(facts));
                    var sections = new[] {
                        new ClientPresentationSection("editor", "", L("help"), fields: fields).WithPlainLayout(),
                        new ClientPresentationSection("preview", L("preview"), state.Draft.Length > 4096 ? L("tooLong") : state.Draft).WithPlainLayout(),
                        new ClientPresentationSection("actions", "", "", bindings,
                            new ClientPresentationStatus(status, state.Error || validation != null ? ClientPresentationTone.Warning : ClientPresentationTone.Neutral)).WithPlainLayout().WithWrappedActions()
                    };
                    dirty = false;
                    view.Publish(new ClientPresentationPage(pageId, L("title"), ticket, sections).WithFixedHeaderSections(1), actions, current);
                }
                catch { dirty = true; actions.Dispose(); throw; }
                published = facts; result = nextResult; language = nextLanguage; pointsVersion = pointVersion;
            }
            public override void DoWindowContents(Rect rect) { view?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height)); }
            internal void Release() { var old = view; view = null; old?.Dispose(); }
            public override void PostClose() { try { Release(); } finally { base.PostClose(); } }
        }
    }
}
