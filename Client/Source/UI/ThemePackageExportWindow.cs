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
    internal sealed class ThemePackageExportWindow : ThemePresentationWindow
    {
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        private readonly ClientThemeDraft draft;
        private readonly Action<string> exported;
        private readonly Func<bool> parent;
        private readonly string key;
        private string id,name,author,version,path,error;
        internal ThemePackageExportWindow(ThemePresentationServices services,IClientTheme theme,IClientThemeCatalog catalog,IClientThemePackageExporter exporter,
            IClientSettingsContext settings,ClientThemeDraft draft,string path,Func<bool> parent,Action<string> exported):base(services,theme)
        {
            this.exporter=exporter;this.settings=settings;this.parent=parent;this.exported=exported;
            this.draft=new ClientThemeDraft(draft.ThemeId,draft.Colors,draft.Floats,draft.InheritedKeys);
            key="theme.export."+ManagedExtensionPaths.Hash(Encoding.UTF8.GetBytes(draft.ThemeId))+".";
            id=settings.Get(key+"id","local.theme."+Guid.NewGuid().ToString("N"));
            string sourceName=catalog.Themes.First(t=>t.Id==draft.ThemeId).Name;
            name=settings.Get(key+"name",Translator.CanTranslate(sourceName)?sourceName.Translate().ToString():sourceName);
            author=settings.Get(key+"author","");version=settings.Get(key+"version","1.0.0");
            this.path=string.IsNullOrWhiteSpace(path)?Path.Combine(GenFilePaths.SaveDataFolderPath,"Phinix","ThemeExports",id+"-"+version+".zip"):Path.GetFullPath(path);
        }
        protected override bool Available=>base.Available&&parent();
        public override Vector2 InitialSize=>new Vector2(Mathf.Min(700,UiScreenSafeArea.Current.width),Mathf.Min(580,UiScreenSafeArea.Current.height));
        protected override void Input(ClientPresentationInput input)
        {switch(input.Id){case "name":name=input.Value;break;case "author":author=input.Value;break;case "id":id=input.Value;break;case "version":version=input.Value;break;case "path":path=input.Value;break;}}
        protected override void Project()
        {
            Publish(L("Phinix_theme_exportPackage"),ClientThemePresentationSurface.Export,draft.ThemeId,null,true,new[]{
                new ClientActionDefinition("export",L("Phinix_theme_export"),Export),new ClientActionDefinition("close",L("Phinix_theme_cancel"),()=>Close())
            },bindings=>new[]{
                new ClientPresentationSection("metadata","",L("Phinix_theme_exportPackageTip"),fields:new[]{
                    new ClientPresentationTextField("name",L("Phinix_theme_packageName"),name,maxLength:160),
                    new ClientPresentationTextField("author",L("Phinix_theme_packageAuthor"),author,maxLength:160),
                    new ClientPresentationTextField("id",L("Phinix_theme_packageId"),id,maxLength:128),
                    new ClientPresentationTextField("version",L("Phinix_theme_packageVersion"),version,maxLength:32),
                    new ClientPresentationTextField("path",L("Phinix_theme_packagePath"),path,maxLength:4096)
                }),
                new ClientPresentationSection("actions","","",bindings,new ClientPresentationStatus(Safe(error??""),error==null?ClientPresentationTone.Neutral:ClientPresentationTone.Error)).WithWrappedActions()
            });
        }
        private void Export()
        {
            try
            {
                string full=Path.GetFullPath(path);
                exporter.ExportPackage(draft,new ClientThemeExportInfo(id,name,author,version),full);
                // Metadata persistence is secondary: a written ZIP must remain a successful export.
                try {settings.Set(key+"id",id);settings.Set(key+"name",name);settings.Set(key+"author",author);settings.Set(key+"version",version);}
                catch(Exception){Verse.Log.Warning("[Phinix] ThemeExportMetadataSaveFailed");}
                exported(full);Close();
            }
            catch(Exception ex){error=(ex as ManagedExtensionValidationException)?.Code??ex.Message;Invalidate();}
        }
    }
}
