using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using UnityEngine;
using Utils.Framework.Themes;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class LocalThemePackagesWindow : StorePresentationWindow
    {
        private readonly IPhinixExtensionModule owner;
        private readonly IClientPresentationService renderer;
        private readonly IClientExtensionPointService points;
        private readonly IClientActionPoint<PluginStorePresentationActionContext> point;
        private readonly IClientThemePackageService packages;
        private readonly IClientTheme theme;
        private readonly IClientThemeWindowService selector;
        private ThemePackage candidate;
        private string expected,message="",selection;
        private ClientPresentationTone messageTone;
        private LocalPackageFileWindow browser;
        private InstalledThemePackage[] rows=Array.Empty<InstalledThemePackage>();
        internal LocalThemePackagesWindow(IPhinixExtensionModule owner,IClientPresentationService renderer,
            IClientExtensionPointService points,IClientActionPoint<PluginStorePresentationActionContext> point,
            IClientThemePackageService packages,IClientThemeWindowService selector,Func<bool> alive,IClientTheme theme=null)
            :base(owner,renderer,points,point,alive,PluginStorePresentationSurface.LocalThemes,theme)
        {
            this.owner=owner;this.renderer=renderer;this.points=points;this.point=point;
            this.theme=theme;this.packages=packages;this.selector=selector;Refresh();
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(760,UiScreenSafeArea.Current.width),Mathf.Min(610,UiScreenSafeArea.Current.height));
        public override void PostClose()
        {try {candidate=null;if(browser?.IsOpen==true) browser.Close();browser=null;}finally {base.PostClose();}}
        private void Refresh()
        {packages.Refresh();rows=packages.Packages.ToArray();if(selection!=null && !rows.Any(p=>p.Manifest.PackageId==selection)) selection=null;message=string.Join("\n",packages.Diagnostics.Take(3));messageTone=message.Length==0?ClientPresentationTone.Neutral:ClientPresentationTone.Warning;Invalidate();}
        private void Run(Action action)
        {try {if(Available) action();}catch(Exception ex) {message=(ex as ManagedExtensionValidationException)?.Code??ex.Message;messageTone=ClientPresentationTone.Error;}finally {Invalidate();}}
        protected override void HandleInput(ClientPresentationInput input)
        {if(input.Id=="themes" && input.Kind==ClientPresentationInputKind.RowSelected) selection=input.Value;}
        private void Choose()
        {
            if(browser?.IsOpen==true) return;
            browser=new LocalPackageFileWindow(owner,renderer,points,point,()=>Available,path=>Run(()=> {
                candidate=null;expected=null;Refresh();candidate=packages.Inspect(path);
                expected=rows.SingleOrDefault(p=>p.Manifest.PackageId==candidate.Manifest.PackageId)?.ArtifactSha256;
                message=T("themeCandidate")+"\n"+Safe(candidate.Manifest.Name)+" · "+Safe(candidate.Manifest.Author)+" · "+candidate.Manifest.Version+"\n"+Safe(candidate.Manifest.PackageId)+"\n"+T("themeLocalSource");messageTone=ClientPresentationTone.Neutral;
            }),developerOnly:false,theme:theme);
            Find.WindowStack.Add(browser);Invalidate();
        }
        protected override void RebuildPresentation()
        {
            var picked=rows.SingleOrDefault(p=>p.Manifest.PackageId==selection);
            var frozenCandidate=candidate;var frozenExpected=expected;
            var definitions=new List<ClientActionDefinition> {
                new ClientActionDefinition("choose",T("themeChooseZip"),()=>Run(Choose)),
                new ClientActionDefinition("refresh",T("refresh"),()=>Run(Refresh)),
                new ClientActionDefinition("selector",T("themeSelector"),()=>Run(selector.OpenThemeWindow)),
                new ClientActionDefinition("theme-install",T(expected==null?"themeInstall":"themeUpdate"),()=>Run(()=> {
                    packages.Install(frozenCandidate,frozenExpected);candidate=null;Refresh();message=T("themeInstalled");messageTone=ClientPresentationTone.Success;
                }),candidate==null?T("themeChooseZip"):null)
            };
            if(picked!=null) definitions.Add(new ClientActionDefinition("theme-uninstall",T("uninstall"),()=>Run(()=> {
                packages.Uninstall(picked.Manifest.PackageId,picked.ArtifactSha256);candidate=null;Refresh();message=T("themeRemoved");messageTone=ClientPresentationTone.Success;
            })));
            var list=rows.Select(p=>new ClientPresentationRow(p.Manifest.PackageId,Safe(p.Manifest.Name)+" · "+p.Manifest.Version,Safe(p.SourceId)+" · "+Safe(p.Manifest.Author))).ToArray();
            Publish(T("localThemes"),messageTone==ClientPresentationTone.Error?"LocalThemeOperationFailed":candidate==null?"LocalThemes":"ThemeCandidateInspected",definitions,bindings=>new[] {
                new ClientPresentationSection("tools","","",bindings.Where(b=>b.Action.Id!="theme-uninstall" && b.Action.Id!="theme-install")).WithWrappedActions(),
                new ClientPresentationSection("packages",T("localThemes"),"",list:new ClientPresentationList("themes",list,T("localDirectoryEmpty"),selection)),
                new ClientPresentationSection("selected",picked==null?"":Safe(picked.Manifest.Name),picked==null?"":Safe(picked.SourceId),bindings.Where(b=>b.Action.Id=="theme-uninstall")),
                new ClientPresentationSection("candidate",candidate==null?"":T("themeCandidate"),"",bindings.Where(b=>b.Action.Id=="theme-install"),status:new ClientPresentationStatus(Safe(message),messageTone))
            },id=>id=="theme-uninstall"?new ClientPresentationConfirmation(T("uninstall"),T("themeRemoveConfirm")+"\n"+Safe(picked.Manifest.Name),T("uninstall"),T("cancel")):null,
                selectedKey:selection,packageId:picked?.Manifest.PackageId,version:picked?.Manifest.Version.ToString(),fixedHeaderSections:1);
        }
    }
}
