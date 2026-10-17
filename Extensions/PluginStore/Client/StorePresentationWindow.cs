using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    // Owner-side lifecycle/projection adapter. All layout, controls and confirmations belong to Host.
    internal abstract class StorePresentationWindow : Window
    {
        private readonly IPhinixExtensionModule owner;
        private readonly IClientPresentationService renderer;
        private readonly IClientExtensionPointService points;
        private readonly IClientActionPoint<PluginStorePresentationActionContext> point;
        private readonly Func<bool> alive;
        private readonly PluginStorePresentationSurface surface;
        protected readonly string PageId = "store.local." + Guid.NewGuid().ToString("N");
        private IClientPresentationSession presentation;
        private long revision, pointRevision = -1;
        private object language;
        private ClientActionExecutionResult result;
        private bool dirty = true;
        protected StorePresentationWindow(IPhinixExtensionModule owner, IClientPresentationService renderer,
            IClientExtensionPointService points, IClientActionPoint<PluginStorePresentationActionContext> point,
            Func<bool> alive, PluginStorePresentationSurface surface, IClientTheme theme = null)
            : base(new ClientThemeWindowDrawing(() => theme?.Capture()))
        {
            this.owner=owner;this.renderer=renderer;this.points=points;this.point=point;this.alive=alive;this.surface=surface;
            doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;draggable=true;resizeable=true;
        }
        protected virtual bool Available => alive();
        protected void Invalidate() { dirty=true; }
        protected abstract void RebuildPresentation();
        protected virtual void HandleInput(ClientPresentationInput input) { }
        public override void PreOpen()
        {
            base.PreOpen();if(!Available) return;
            try {presentation=renderer.Open(owner,PageId);RebuildPresentation();}
            catch {var failed=presentation;presentation=null;failed?.Dispose();throw;}
        }
        public override void WindowUpdate()
        {
            base.WindowUpdate();windowRect=UiScreenSafeArea.ClampWindow(windowRect,new Vector2(320,320));
            if(!Available) {Close();return;}
            if(presentation==null || presentation.IsClosed) return;
            // Read the rendered revision completely before edits invalidate its context.
            var pendingInputs = new List<ClientPresentationInput>();
            while (presentation.TryDequeueInput(out var queued)) pendingInputs.Add(queued);
            foreach (var input in pendingInputs) {HandleInput(input);Invalidate();}
            if(dirty || pointRevision!=points.Revision || !ReferenceEquals(language,LanguageDatabase.activeLanguage) || !ReferenceEquals(result,presentation.LastActionResult)) RebuildPresentation();
        }
        public override void PostClose()
        { var old=presentation;presentation=null;try {old?.Dispose();}finally {base.PostClose();} }
        public override void DoWindowContents(Rect rect) {if(Available) presentation?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height));}
        protected static string T(string key) => ("Phinix_store2_"+key).Translate();
        protected static string Safe(string text) => (text??"").Replace("<","‹").Replace(">","›");
        protected void Publish(string title,string state,IEnumerable<ClientActionDefinition> definitions,
            Func<IReadOnlyList<ClientPresentationAction>,IEnumerable<ClientPresentationSection>> sections,
            Func<string,ClientPresentationConfirmation> confirmation=null, string selectedKey=null,string packageId=null,string version=null,int fixedHeaderSections=0)
        {
            var ticket=++revision;var nextLanguage=LanguageDatabase.activeLanguage;var nextPoints=points.Revision;
            var nextResult=presentation.LastActionResult;
            Func<bool> current=()=>IsOpen && Available && presentation!=null && !presentation.IsClosed && !dirty && revision==ticket &&
                points.Revision==nextPoints && ReferenceEquals(LanguageDatabase.activeLanguage,nextLanguage) && ReferenceEquals(presentation.LastActionResult,nextResult);
            var actions=point.Collect(new PluginStorePresentationActionContext(PageId,ticket,selectedKey,state,packageId,version,false,false,definitions,surface),current);
            try
            {
                var bindings=actions.Actions.Select(a=>new ClientPresentationAction(a.OwnerId+"."+a.Id,a,
                    a.OwnerId==owner.ExtensionId && (a.Id=="select" || a.Id=="theme-install") ? ClientButtonKind.Primary :
                    a.OwnerId==owner.ExtensionId && a.Id=="theme-uninstall" ? ClientButtonKind.Danger : ClientButtonKind.Normal,
                    a.OwnerId==owner.ExtensionId ? confirmation?.Invoke(a.Id) : null)).ToList().AsReadOnly();
                dirty=false;
                var page=new ClientPresentationPage(PageId,title,ticket,sections(bindings).Select(s=>s.WithPlainLayout()));
                if(fixedHeaderSections>0) page=page.WithFixedHeaderSections(fixedHeaderSections);
                presentation.Publish(page,actions,current);
            }
            catch {dirty=true;actions.Dispose();throw;}
            language=nextLanguage;pointRevision=nextPoints;result=nextResult;
        }
    }
}
