using System;
using System.IO;
using System.Linq;
using System.Text;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace PhinixClient
{
    internal sealed class ThemePackageExportWindow : Window
    {
        private readonly IClientTheme theme;
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        private readonly ClientThemeDraft draft;
        private readonly Action<string> exported;
        private readonly string key;
        private string id,name,author,version,path,error;
        internal ThemePackageExportWindow(IClientTheme theme,IClientThemeCatalog catalog,IClientThemePackageExporter exporter,
            IClientSettingsContext settings,ClientThemeDraft draft,string path,Action<string> exported)
        {
            this.theme=theme;this.exporter=exporter;this.settings=settings;this.exported=exported;
            this.draft=new ClientThemeDraft(draft.ThemeId,draft.Colors,draft.Floats,draft.InheritedKeys);
            key="theme.export."+ManagedExtensionPaths.Hash(Encoding.UTF8.GetBytes(draft.ThemeId))+".";
            id=settings.Get(key+"id","local.theme."+Guid.NewGuid().ToString("N"));
            string sourceName=catalog.Themes.First(t=>t.Id==draft.ThemeId).Name;
            name=settings.Get(key+"name",Translator.CanTranslate(sourceName)?sourceName.Translate().ToString():sourceName);
            author=settings.Get(key+"author","");version=settings.Get(key+"version","1.0.0");
            this.path=string.IsNullOrWhiteSpace(path)?Path.Combine(GenFilePaths.SaveDataFolderPath,"Phinix","ThemeExports",id+"-"+version+".zip"):Path.GetFullPath(path);
            doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(700,UiScreenSafeArea.Current.width),Mathf.Min(430,UiScreenSafeArea.Current.height));
        public override void WindowUpdate() {base.WindowUpdate();windowRect=UiScreenSafeArea.ClampWindow(windowRect,new Vector2(320,380));}
        private static string L(string key) => ("Phinix_theme_"+key).Translate();
        public override void DoWindowContents(Rect rect)
        {
            using(new ClientGuiScope(true))
            {
                var palette=theme.Capture();Text.Font=GameFont.Small;
                ClientWidgets.Label(new Rect(0,0,rect.width,30),L("exportPackage"),palette);
                name=Field(40,"packageName",name,160,rect.width,palette);
                author=Field(82,"packageAuthor",author,160,rect.width,palette);
                id=Field(124,"packageId",id,128,rect.width,palette);
                version=Field(166,"packageVersion",version,32,rect.width,palette);
                path=Field(208,"packagePath",path,4096,rect.width,palette);
                ClientWidgets.Label(new Rect(0,252,rect.width,72),error??L("exportPackageTip"),palette,true);
                TooltipHandler.TipRegion(new Rect(0,252,rect.width,72),error??L("exportPackageTip"));
                if(ClientWidgets.Button(new Rect(0,rect.height-38,rect.width,32),L("export"),palette))
                {
                    try
                    {
                        string full=Path.GetFullPath(path);
                        exporter.ExportPackage(draft,new ClientThemeExportInfo(id,name,author,version),full);
                        // Metadata persistence is secondary: a written ZIP must remain a successful export.
                        try {settings.Set(key+"id",id);settings.Set(key+"name",name);settings.Set(key+"author",author);settings.Set(key+"version",version);}
                        catch(Exception) {Verse.Log.Warning("[Phinix] ThemeExportMetadataSaveFailed");}
                        exported(full);Close();
                    }
                    catch(Exception ex) {error=(ex as ManagedExtensionValidationException)?.Code??ex.Message;}
                }
            }
        }
        private string Field(float y,string label,string value,int maximum,float width,IClientThemeReader palette)
        {
            ClientWidgets.Label(new Rect(0,y,100,30),L(label),palette);
            return ClientWidgets.TextField(new Rect(106,y,Mathf.Max(0,width-106),30),value,palette,controlName:"phinix.theme.export."+label,maxLength:maximum);
        }
    }
}
