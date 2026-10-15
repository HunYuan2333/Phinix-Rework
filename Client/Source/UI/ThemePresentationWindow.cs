using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using Utils.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    // Permanent host infrastructure owner; no public constructor, so not an optional discovered plugin.
    [PhinixExtension("builtin.host")]
    internal sealed class HostThemePresentationOwner : IPhinixExtensionModule
    {
        internal HostThemePresentationOwner() { }
        public string ExtensionId => "builtin.host";
        public void Register(IExtensionBuilder builder) { }
    }
    internal sealed class HostThemeActionContext : ClientThemePresentationActionContext
    {
        internal HostThemeActionContext(string page,long revision,ClientThemePresentationSurface surface,string themeId,string key,bool canApply,IEnumerable<ClientActionDefinition> actions)
            :base(page,revision,surface,themeId,key,canApply) { Actions=actions.ToArray(); }
        internal IEnumerable<ClientActionDefinition> Actions { get; }
    }
    internal sealed class ThemePresentationServices : IDisposable
    {
        internal readonly HostThemePresentationOwner Owner;
        internal readonly IClientPresentationService Renderer;
        internal readonly IClientExtensionPointService Points;
        internal readonly IClientActionPoint<ClientThemePresentationActionContext> Point;
        private readonly IClientExtensionPointScope scope;
        private readonly ClientExtensionPointService pointOwner;
        private readonly ClientLifetimeService lifetimeOwner;
        internal bool Active {get;private set;}=true;
        internal ThemePresentationServices(IClientPresentationService renderer,ClientExtensionPointService points,ClientLifetimeService lifetime)
        {
            Owner=new HostThemePresentationOwner();Renderer=renderer;Points=points;pointOwner=points;lifetimeOwner=lifetime;
            lifetime.OnActivating(Owner);points.OnActivating(Owner);
            scope=points.ForModule(Owner);
            try
            {
                Point=scope.DefineActions<ClientThemePresentationActionContext>(ClientThemePresentationActionContext.PointId);
                scope.ContributeActions<ClientThemePresentationActionContext>(ClientThemePresentationActionContext.PointId,c=>c is HostThemeActionContext owned?owned.Actions:Array.Empty<ClientActionDefinition>());
            }
            catch {Dispose();throw;}
        }
        public void Dispose()
        {if(!Active)return;Active=false;try {scope?.Dispose();}finally {try {pointOwner.OnStopped(Owner);}finally {lifetimeOwner.OnStopped(Owner);}}}
    }
    internal abstract class ThemePresentationWindow : Window
    {
        protected readonly ThemePresentationServices Services;
        protected readonly IClientTheme Theme;
        protected readonly string PageId="theme.view."+Guid.NewGuid().ToString("N");
        private IClientPresentationSession presentation;
        private long revision,themeVersion=-1,pointsVersion=-1;
        private object language;
        private ClientActionExecutionResult actionResult;
        private bool dirty=true;
        protected ThemePresentationWindow(ThemePresentationServices services,IClientTheme theme)
            : base(new ClientThemeWindowDrawing(() => theme.Capture()))
        {Services=services;Theme=theme;doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;draggable=true;resizeable=true;}
        protected virtual bool Available => Services.Active;
        protected void Invalidate(){dirty=true;}
        protected abstract void Project();
        protected virtual void Input(ClientPresentationInput input) { }
        protected virtual bool DataChanged => false;
        protected virtual Func<bool> DataGuard => ()=>true;
        public override void PreOpen()
        {
            base.PreOpen();if(!Available)return;
            try {presentation=Services.Renderer.Open(Services.Owner,PageId);Project();}
            catch {var failed=presentation;presentation=null;failed?.Dispose();throw;}
        }
        public override void WindowUpdate()
        {
            base.WindowUpdate();windowRect=UiScreenSafeArea.ClampWindow(windowRect,new Vector2(320,380));
            if(!Available){Close();return;}if(presentation==null||presentation.IsClosed)return;
            while(presentation.TryDequeueInput(out var input))
            {
                Input(input);
                // A selection may close its window and dispose the session immediately.
                if(presentation==null||presentation.IsClosed||!IsOpen)return;
                Invalidate();
            }
            if(dirty||DataChanged||themeVersion!=Theme.Version||pointsVersion!=Services.Points.Revision||!ReferenceEquals(language,LanguageDatabase.activeLanguage)||!ReferenceEquals(actionResult,presentation.LastActionResult))Project();
        }
        public override void DoWindowContents(Rect rect){if(Available)presentation?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height));}
        public override void PostClose(){var old=presentation;presentation=null;try {old?.Dispose();}finally {base.PostClose();}}
        protected static string L(string key)=>key.Translate();
        protected static string Safe(string text)=>(text??"").Replace("<","‹").Replace(">","›");
        protected static string Name(string key)=>string.IsNullOrEmpty(key)?"":Translator.CanTranslate(key)?L(key):Safe(key);
        protected void Publish(string title,ClientThemePresentationSurface surface,string themeId,string key,bool canApply,
            IEnumerable<ClientActionDefinition> definitions,Func<IReadOnlyList<ClientPresentationAction>,IEnumerable<ClientPresentationSection>> sections,
            ClientPresentationSplitView split=null,int fixedHeaders=0)
        {
            long ticket=++revision,nextTheme=Theme.Version,nextPoints=Services.Points.Revision;
            var nextLanguage=LanguageDatabase.activeLanguage;var nextResult=presentation.LastActionResult;var dataCurrent=DataGuard;
            Func<bool> current=()=>IsOpen&&Available&&!dirty&&revision==ticket&&presentation!=null&&!presentation.IsClosed&&
                nextTheme==Theme.Version&&nextPoints==Services.Points.Revision&&ReferenceEquals(nextLanguage,LanguageDatabase.activeLanguage)&&ReferenceEquals(nextResult,presentation.LastActionResult)&&dataCurrent();
            var actions=Services.Point.Collect(new HostThemeActionContext(PageId,ticket,surface,themeId,key,canApply,definitions),current);
            try
            {
                var bindings=actions.Actions.Select(a=>new ClientPresentationAction(a.OwnerId+"."+a.Id,a,
                    a.OwnerId==Services.Owner.ExtensionId&&(a.Id=="apply"||a.Id=="export")?ClientButtonKind.Primary:ClientButtonKind.Normal)).ToList().AsReadOnly();
                var page=new ClientPresentationPage(PageId,title,ticket,sections(bindings).Select(s=>s.WithPlainLayout()));
                if(split!=null)page=page.WithSplitView(split);else if(fixedHeaders>0)page=page.WithFixedHeaderSections(fixedHeaders);
                dirty=false;presentation.Publish(page,actions,current);
            }
            catch {dirty=true;actions.Dispose();throw;}
            themeVersion=nextTheme;pointsVersion=nextPoints;language=nextLanguage;actionResult=nextResult;
        }
    }
}
