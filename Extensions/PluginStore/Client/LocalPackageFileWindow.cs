using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class LocalPackageFileWindow : StorePresentationWindow
    {
        private readonly Action<string> selected;
        private readonly bool developerOnly;
        private string path="",message="";
        private string[] entries=Array.Empty<string>();
        private HashSet<string> folders=new HashSet<string>(StringComparer.Ordinal);
        internal LocalPackageFileWindow(IPhinixExtensionModule owner,IClientPresentationService renderer,
            IClientExtensionPointService points,IClientActionPoint<PluginStorePresentationActionContext> point,
            Func<bool> alive,Action<string> selected,bool developerOnly=true,IClientTheme theme=null)
            : base(owner,renderer,points,point,alive,PluginStorePresentationSurface.LocalFile,theme)
        {this.selected=selected;this.developerOnly=developerOnly;forcePause=true;}
        protected override bool Available => base.Available && (!developerOnly || Prefs.DevMode);
        public override Vector2 InitialSize => new Vector2(Mathf.Min(700,UiScreenSafeArea.Current.width),Mathf.Min(460,UiScreenSafeArea.Current.height));
        protected override void HandleInput(ClientPresentationInput input)
        {
            if(input.Id=="path" && input.Kind==ClientPresentationInputKind.TextChanged) path=input.Value;
            else if(input.Id=="files" && input.Kind==ClientPresentationInputKind.RowSelected && int.TryParse(input.Value?.Replace("file.",""),out var index) && index>=0 && index<entries.Length)
            {path=entries[index];if(Directory.Exists(path)) Browse(path);}
        }
        protected override void RebuildPresentation()
        {
            var definitions=new[] {
                new ClientActionDefinition("browse",T("localBrowse"),()=>Browse(path)),
                new ClientActionDefinition("select",T("localSelect"),Select)
            };
            var rows=entries.Select((entry,i)=>new ClientPresentationRow("file."+i,Safe(string.IsNullOrEmpty(Path.GetFileName(entry))?entry:Path.GetFileName(entry)),folders.Contains(entry)?T("localFolder"):"ZIP")).ToArray();
            Publish(T(developerOnly?"localImport":"themeChooseZip"),"SelectingLocalZip",definitions,bindings=>new[] {
                new ClientPresentationSection("path-section","","",bindings,fields:new[] {new ClientPresentationTextField("path",T("localPath"),path,T("localPath"),4096)}).WithWrappedActions(),
                new ClientPresentationSection("directory",T("localBrowse"),"",list:new ClientPresentationList("files",rows,T("localDirectoryEmpty"))),
                new ClientPresentationSection("feedback","","",status:new ClientPresentationStatus(Safe(message),message.Length==0?ClientPresentationTone.Neutral:ClientPresentationTone.Warning))
            },fixedHeaderSections:1);
        }
        private void Select()
        {
            try
            {
                if(!Available) return;
                string full=ClientEnvironmentPaths.NormalizeAbsolute(path);
                if(!File.Exists(full) || !string.Equals(Path.GetExtension(full),".zip",StringComparison.OrdinalIgnoreCase))
                {message=T("localInvalidSelection");Invalidate();return;}
                selected(full);Close();
            }
            catch(Exception) {message=T("localInvalidSelection");Invalidate();}
        }
        private void Browse(string selectedPath)
        {
            try
            {
                if(!Available) return;
                string full=ClientEnvironmentPaths.NormalizeAbsolute(selectedPath);
                if(File.Exists(full)) full=Path.GetDirectoryName(full);
                var items=new List<string>();var nextFolders=new HashSet<string>(StringComparer.Ordinal);
                string parent=Path.GetDirectoryName(full);if(parent!=null) {items.Add(parent);nextFolders.Add(parent);}
                int count=0;
                foreach(string entry in Directory.EnumerateFileSystemEntries(full))
                {
                    if(++count>4096) {message=T("localDirectoryLimit");Invalidate();return;}
                    bool directory=Directory.Exists(entry);
                    if(directory || string.Equals(Path.GetExtension(entry),".zip",StringComparison.OrdinalIgnoreCase)) items.Add(entry);
                    if(directory) nextFolders.Add(entry);
                    if(items.Count>256) {message=T("localDirectoryLimit");Invalidate();return;}
                }
                path=full;entries=items.OrderBy(e=>e,StringComparer.OrdinalIgnoreCase).ToArray();folders=nextFolders;message="";
            }
            catch(Exception) {message=T("localInvalidSelection");}
            Invalidate();
        }
    }
}
