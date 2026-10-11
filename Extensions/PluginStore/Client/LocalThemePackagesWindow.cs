using System;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework.Themes;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class LocalThemePackagesWindow : Window
    {
        private readonly IClientTheme theme;
        private readonly IClientThemePackageService packages;
        private readonly IClientThemeWindowService selector;
        private readonly Func<bool> alive;
        private ThemePackage candidate;
        private string expected,message="";
        private Vector2 scroll;
        private LocalPackageFileWindow browser;
        private InstalledThemePackage[] rows=new InstalledThemePackage[0];
        internal LocalThemePackagesWindow(IClientTheme theme,IClientThemePackageService packages,IClientThemeWindowService selector,Func<bool> alive)
        {
            this.theme=theme;this.packages=packages;this.selector=selector;this.alive=alive;
            doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;Refresh();
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(760,UiScreenSafeArea.Current.width),Mathf.Min(610,UiScreenSafeArea.Current.height));
        public override void WindowUpdate() {base.WindowUpdate();windowRect=UiScreenSafeArea.ClampWindow(windowRect,new Vector2(320,420));if(!alive()) Close();}
        public override void PostClose() {base.PostClose();candidate=null;if(browser?.IsOpen==true) browser.Close();browser=null;}
        private static string T(string key) => ("Phinix_store2_"+key).Translate();
        private void Refresh() {packages.Refresh();rows=packages.Packages.ToArray();message=string.Join("\n",packages.Diagnostics.Take(3));}
        private void Run(Action action) {try {if(alive()) action();}catch(Exception ex) {message=(ex as ManagedExtensionValidationException)?.Code??ex.Message;}}
        public override void DoWindowContents(Rect rect)
        {
            if(!alive()) return;
            using(new ClientGuiScope(true))
            {
                var palette=theme.Capture();Text.Font=GameFont.Small;
                ClientWidgets.Label(new Rect(0,0,rect.width,28),T("localThemes"),palette);
                float width=(rect.width-12)/3;
                if(ClientWidgets.Button(new Rect(0,34,width,30),T("themeChooseZip"),palette))
                {
                    if(browser?.IsOpen!=true)
                    {
                        browser=new LocalPackageFileWindow(theme,path=>Run(()=>
                        {
                            candidate=null;expected=null;Refresh();candidate=packages.Inspect(path);expected=rows.SingleOrDefault(p=>p.Manifest.PackageId==candidate.Manifest.PackageId)?.ArtifactSha256;
                            message=T("themeCandidate")+"\n"+candidate.Manifest.Name+" · "+candidate.Manifest.Author+" · "+candidate.Manifest.Version+"\n"+candidate.Manifest.PackageId+"\n"+T("themeLocalSource");
                        }),developerOnly:false);
                        Find.WindowStack.Add(browser);
                    }
                }
                if(ClientWidgets.Button(new Rect(width+6,34,width,30),T("refresh"),palette)) Run(Refresh);
                if(ClientWidgets.Button(new Rect((width+6)*2,34,width,30),T("themeSelector"),palette)) Run(selector.OpenThemeWindow);
                var viewport=new Rect(0,74,rect.width,Mathf.Max(0,rect.height-254));
                Widgets.BeginScrollView(viewport,ref scroll,new Rect(0,0,Mathf.Max(0,viewport.width-18),rows.Length*72));
                try
                {
                    var range=VirtualListLayout.GetFixedRange(rows.Length,72,scroll.y,viewport.height,1);
                    for(int i=range.FirstIndex;i<range.EndIndexExclusive;i++)
                    {
                        var package=rows[i];float y=i*72;
                        ClientWidgets.LabelEllipses(new Rect(0,y,Mathf.Max(0,viewport.width-116),30),package.Manifest.Name+" · "+package.Manifest.Version,palette);
                        ClientWidgets.LabelEllipses(new Rect(0,y+30,Mathf.Max(0,viewport.width-116),30),package.SourceId+" · "+package.Manifest.Author,palette,true);
                        if(ClientWidgets.Button(new Rect(Mathf.Max(0,viewport.width-110),y+10,90,30),T("uninstall"),palette))
                        {
                            Find.WindowStack.Add(new Dialog_MessageBox(T("themeRemoveConfirm")+"\n"+package.Manifest.Name,T("uninstall"),()=>Run(()=>
                            {packages.Uninstall(package.Manifest.PackageId,package.ArtifactSha256);candidate=null;Refresh();message=T("themeRemoved");}),T("cancel")));
                        }
                    }
                }
                finally {Widgets.EndScrollView();}
                ClientWidgets.Label(new Rect(0,rect.height-172,rect.width,126),message,palette,true);
                TooltipHandler.TipRegion(new Rect(0,rect.height-172,rect.width,126),message);
                if(ClientWidgets.Button(new Rect(0,rect.height-38,rect.width,32),T(expected==null?"themeInstall":"themeUpdate"),palette,enabled:candidate!=null))
                    Run(()=> {packages.Install(candidate,expected);candidate=null;Refresh();message=T("themeInstalled");});
            }
        }
    }
}
