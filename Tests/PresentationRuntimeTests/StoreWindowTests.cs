using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phinix.PluginStore;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private sealed class LocalPoint : IClientExtensionPointService,IClientActionPoint<PluginStorePresentationActionContext>
    {
        public long Revision {get;set;}
        public bool ThrowCollect;
        public LocalSet Last;
        public PluginStorePresentationActionContext Context;
        public IClientActionSet Collect(PluginStorePresentationActionContext context,Func<bool> current=null)
        {if(ThrowCollect) throw new InvalidOperationException("Collect failure fixture");Context=context;Last=new LocalSet(context.OwnerActions,current);return Last;}
    }
    private sealed class LocalSet : IClientActionSet
    {
        internal bool Closed;
        public IReadOnlyList<IClientContributedAction> Actions {get;}
        internal LocalSet(IEnumerable<ClientActionDefinition> definitions,Func<bool> current)
        {Actions=definitions.Select(d=>(IClientContributedAction)new LocalCommand(this,d,current)).ToArray();}
        public void Dispose(){Closed=true;}
    }
    private sealed class LocalCommand : IClientContributedAction
    {
        private readonly LocalSet set;private readonly ClientActionDefinition definition;private readonly Func<bool> current;
        private bool invoked;
        internal LocalCommand(LocalSet set,ClientActionDefinition definition,Func<bool> current){this.set=set;this.definition=definition;this.current=current;}
        public string OwnerId=>"test";public string Id=>definition.Id;public string Label=>definition.Label;public string DisabledReason=>definition.DisabledReason;
        public ClientActionExecutionResult TryExecute()
        {if(set.Closed || invoked || !current() || DisabledReason!=null) return new ClientActionExecutionResult{Code="Stale"};invoked=true;definition.Execute();return new ClientActionExecutionResult{Completed=true};}
    }
    private static void CheckLocalFileAdapter()
    {
        string folder=Path.Combine(Path.GetTempPath(),"phinix-local-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string zip=Path.Combine(folder,"example.zip");File.WriteAllText(zip,"selection-only fixture");File.WriteAllText(Path.Combine(folder,"ignore.txt"),"ignored");
        try
        {
            var points=new LocalPoint();var renderer=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
            string selected=null;
            var window=new LocalPackageFileWindow(new Module(),renderer,points,points,()=>true,path=>selected=path);
            window.PreOpen();
            Check(points.Context.Surface==PluginStorePresentationSurface.LocalFile && !points.Context.InventoryKnown,"Local file context identifies its surface without inventing plugin inventory authority");
            Event.current.type=EventType.Repaint;Widgets.FieldValue=folder;window.DoWindowContents(new Rect(0,0,680,440));window.WindowUpdate();
            var browse=points.Last.Actions.Single(a=>a.Id=="browse");Check(browse.TryExecute().Completed,"Explicit browse executes through collected public actions");
            var staleSelect=points.Last.Actions.Single(a=>a.Id=="select");Check(!staleSelect.TryExecute().Completed && selected==null,"Directory change invalidates previous action set before republish");
            window.WindowUpdate();Widgets.Labels.Clear();window.DoWindowContents(new Rect(0,0,680,440));
            Check(Widgets.Labels.Any(l=>l.Text=="example.zip") && !Widgets.Labels.Any(l=>l.Text=="ignore.txt"),"Owner directory projection lists ZIP files and omits unrelated files");
            Widgets.FieldValue=zip;window.DoWindowContents(new Rect(0,0,680,440));window.WindowUpdate();
            Event.current.type=EventType.MouseDown;Widgets.ClickLabel="Phinix_store2_localSelect";
            window.DoWindowContents(new Rect(0,0,680,440));
            Check(selected==zip && !window.IsOpen && points.Last.Closed,"Rendered file selection closes its session during fixed-header Draw without accessing the disposed page");
            selected=null;Widgets.ClickLabel="Phinix_store2_localSelect";window.DoWindowContents(new Rect(0,0,680,440));
            Check(selected==null,"A later Draw after file-picker close cannot repeat the selection intent");Widgets.ClickLabel=null;
            selected=null;window=new LocalPackageFileWindow(new Module(),renderer,points,points,()=>true,path=>selected=path);window.PreOpen();
            var revoked=points.Last.Actions.Single(a=>a.Id=="select");Prefs.DevMode=false;
            Check(!revoked.TryExecute().Completed,"Developer mode revocation rejects existing local DLL actions");window.WindowUpdate();
            Check(!window.IsOpen && selected==null,"Revocation closes selection without committing business intent");
            var failedLife=new Lifetime();var failedRenderer=new ClientPresentationService(failedLife,new Theme(),()=>true,_=>{});
            points.ThrowCollect=true;
            var failed=new LocalPackageFileWindow(new Module(),failedRenderer,points,points,()=>true,path=>selected=path,developerOnly:false);
            Reject(failed.PreOpen,"Projection failure is reported to caller");
            Check(failedLife.IsClosed,"Failed window open releases its Host session and owned lifetime");points.ThrowCollect=false;
            var ordinary=new LocalPackageFileWindow(new Module(),renderer,points,points,()=>true,path=>selected=path,developerOnly:false);ordinary.PreOpen();
            Check(ordinary.IsOpen,"Theme ZIP selection remains available without developer mode");ordinary.Close();Check(selected==null,"Closing a file picker emits no selection intent");
        }
        finally {Prefs.DevMode=true;Widgets.FieldValue=null;Directory.Delete(folder,true);}
    }
}
