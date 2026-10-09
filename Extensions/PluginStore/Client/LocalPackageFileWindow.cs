using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    // Explicit path selection works on Windows and Linux without external dialog dependencies.
    internal sealed class LocalPackageFileWindow : Window
    {
        private readonly Action<string> selected;
        private string path="", message;
        private string[] entries=new string[0];
        private Vector2 scroll;
        internal LocalPackageFileWindow(Action<string> selected)
        { this.selected=selected; forcePause=true; absorbInputAroundWindow=true; doCloseX=true; closeOnAccept=false; }
        public override Vector2 InitialSize => new Vector2(700,460);
        private static string T(string key) => ("Phinix_store2_"+key).Translate();
        public override void DoWindowContents(Rect rect)
        {
            var font=Text.Font;
            try { DrawContents(rect); }
            finally { Text.Font=font; }
        }
        private void DrawContents(Rect rect)
        {
            if(!Prefs.DevMode) { Close(); return; }
            Text.Font=GameFont.Small;
            Widgets.Label(new Rect(0,0,rect.width,28),T("localImport"));
            path=Widgets.TextField(new Rect(0,36,Mathf.Max(0,rect.width-110),32),path,4096);
            if(Widgets.ButtonText(new Rect(rect.width-104,36,104,32),T("localBrowse"))) Browse(path);
            var viewport=new Rect(0,78,rect.width,Mathf.Max(0,rect.height-150));
            Widgets.BeginScrollView(viewport,ref scroll,new Rect(0,0,Mathf.Max(0,viewport.width-18),entries.Length*32));
            try
            {
                for(int i=0;i<entries.Length;i++)
                {
                    string entry=entries[i]; string name=Path.GetFileName(entry);
                    if(Widgets.ButtonText(new Rect(0,i*32,Mathf.Max(0,viewport.width-18),28),name))
                    { path=entry; if(Directory.Exists(entry)) Browse(entry); }
                }
            }
            finally { Widgets.EndScrollView(); }
            if(message!=null) Widgets.Label(new Rect(0,rect.height-64,rect.width,24),message);
            if(Widgets.ButtonText(new Rect(0,rect.height-34,rect.width,32),T("localSelect")))
            {
                try
                {
                    if(!Prefs.DevMode) return;
                    string full=PhinixClient.Framework.ClientEnvironmentPaths.NormalizeAbsolute(path);
                    if(!File.Exists(full) || !string.Equals(Path.GetExtension(full),".zip",StringComparison.OrdinalIgnoreCase))
                    { message=T("localInvalidSelection"); return; }
                    selected(full); Close();
                }
                catch(Exception) { message=T("localInvalidSelection"); }
            }
        }
        private void Browse(string selectedPath)
        {
            try
            {
                string full=PhinixClient.Framework.ClientEnvironmentPaths.NormalizeAbsolute(selectedPath);
                if(File.Exists(full)) full=Path.GetDirectoryName(full);
                var items=new List<string>();
                string parent=Path.GetDirectoryName(full); if(parent!=null) items.Add(parent);
                int count=0;
                foreach(string entry in Directory.EnumerateFileSystemEntries(full))
                {
                    if(++count>4096) { message=T("localDirectoryLimit"); return; }
                    if(Directory.Exists(entry) || string.Equals(Path.GetExtension(entry),".zip",StringComparison.OrdinalIgnoreCase)) items.Add(entry);
                    if(items.Count>256) { message=T("localDirectoryLimit"); return; }
                }
                path=full; entries=items.OrderBy(e=>e,StringComparer.OrdinalIgnoreCase).ToArray(); scroll=Vector2.zero; message=null;
            }
            catch(Exception) { message=T("localInvalidSelection"); }
        }
    }
}
