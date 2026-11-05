using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Phinix.InventoryExtension;
using Verse;

internal static partial class Program
{
    private static void CheckInventorySaveScope()
    {
        var type=Type.GetType("Phinix.InventoryExtension.Client.BuiltInInventoryClientExtension, InventoryExtension.Client",true);
        var componentType=type.Assembly.GetType("Phinix.InventoryExtension.Client.InventoryGameComponent",true);
        var patch=type.Assembly.GetType("Phinix.InventoryExtension.Client.InventorySaveIdentityPatch",true);
        const BindingFlags instance=BindingFlags.NonPublic|BindingFlags.Instance;
        var active=type.GetField("active",BindingFlags.NonPublic|BindingFlags.Static);
        var pathProperty=patch.GetProperty("CurrentPath",BindingFlags.Static|BindingFlags.Public);
        var oldActive=active.GetValue(null);var oldPath=pathProperty.GetValue(null);var oldGame=Current.Game;
        string root=Path.Combine(Path.GetTempPath(),"phinix-save-scope-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var service=Activator.CreateInstance(type);
            var read=(IInventoryReadApi)service;
            var scopes=(IInventoryReceiveScopeApi)service;
            var publish=type.GetMethod("PublishReceiveScope",BindingFlags.Instance|BindingFlags.NonPublic);
            var generation=patch.GetProperty("Generation",BindingFlags.Static|BindingFlags.NonPublic);
            Assert(scopes.CaptureReceiveScope()==null,"Inactive inventory cannot capture a receive scope.");
            Assert(read.GetStatus().SaveIdentity==null,"Inactive inventory exposes no save identity.");
            var game=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
            game.components=new List<GameComponent>();
            var component=(GameComponent)Activator.CreateInstance(componentType,new object[]{game});game.components.Add(component);
            componentType.GetField("saveId",instance).SetValue(component,"synthetic-lineage");
            string file=Path.Combine(root,"synthetic-save.rws");File.WriteAllText(file,"synthetic identity fixture, not a game save");
            active.SetValue(null,service);Current.Game=game;pathProperty.SetValue(null,file);
            type.GetField("mainThreadId",instance).SetValue(service,System.Threading.Thread.CurrentThread.ManagedThreadId);
            type.GetField("attachedSave",instance).SetValue(service,component);type.GetField("attachedIdentity",instance).SetValue(service,file);
            string expected;
            using(var sha=SHA256.Create())expected=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("synthetic-lineage|"+file))).Replace("-","").ToLowerInvariant();
            var status=read.GetStatus();
            Assert(status.SaveIdentity==expected&&status.Availability==InventoryAvailability.ReadOnlyFault,"Actual inventory reports its original journal scope even when journal inspection is read-only.");
            Assert(!status.SaveIdentity.Contains(root),"Public save identity exposes no raw save path.");
            publish.Invoke(service,null);var origin=scopes.CaptureReceiveScope();
            Assert(origin!=null&&origin.SaveIdentity==expected&&origin.IsCurrent(),"Actual receive scope captures the attached inventory save, without claiming journal commit.");
            int pathEpoch=(int)generation.GetValue(null);
            pathProperty.SetValue(null,file);publish.Invoke(service,null);
            Assert((int)generation.GetValue(null)==pathEpoch&&ReferenceEquals(origin,scopes.CaptureReceiveScope()),"Same-path frames and saves preserve the active receive scope.");
            Exception receiverError=null;bool threadCapture=false,threadRejected=false;
            var receiver=new Thread(()=>
            {
                try {threadCapture=ReferenceEquals(origin,scopes.CaptureReceiveScope());try {origin.IsCurrent();}catch(InvalidOperationException){threadRejected=true;}}
                catch(Exception error){receiverError=error;}
            });
            receiver.Start();Assert(receiver.Join(5000),"Background capture terminates without game dispatch.");
            if(receiverError!=null)throw receiverError;
            Assert(threadCapture&&threadRejected,"Capture is thread-safe while game validation stays on the actual bound main thread.");
            string alternate=Path.Combine(root,"save-as.rws");File.WriteAllText(alternate,"synthetic second path");
            pathProperty.SetValue(null,alternate);type.GetField("attachedIdentity",instance).SetValue(service,alternate);
            Assert(!origin.IsCurrent()&&origin.SaveIdentity==expected,"Same Game Save As expires the original token and never mutates its captured identity.");
            publish.Invoke(service,null);var otherScope=scopes.CaptureReceiveScope();
            Assert(otherScope!=null&&otherScope.IsCurrent()&&otherScope.SaveIdentity!=expected,"A later receipt can capture the newly attached save scope.");
            pathProperty.SetValue(null,file);type.GetField("attachedIdentity",instance).SetValue(service,file);publish.Invoke(service,null);
            Assert(!origin.IsCurrent()&&!otherScope.IsCurrent(),"Switching back does not revive either historical receive token.");
            var restored=scopes.CaptureReceiveScope();
            var loadByFile=patch.GetNestedType("LoadByFile",BindingFlags.NonPublic);
            loadByFile.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{new FileInfo(file)});
            Assert(!restored.IsCurrent(),"A real load prefix invalidates same-path scopes even if this fixture retains the Game object.");
            publish.Invoke(service,null);Assert(scopes.CaptureReceiveScope()==null,"Frame publication cannot reauthorize the previous game during a pending load.");
            var foreignGame=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));foreignGame.components=new List<GameComponent>();
            var foreignComponent=(GameComponent)Activator.CreateInstance(componentType,new object[]{foreignGame});foreignGame.components.Add(foreignComponent);
            componentType.GetMethod("FinalizeInit").Invoke(foreignComponent,null);
            publish.Invoke(service,null);Assert(scopes.CaptureReceiveScope()==null,"An unrelated game's component cannot release the pending load barrier.");
            componentType.GetMethod("FinalizeInit").Invoke(component,null);
            publish.Invoke(service,null);Assert(scopes.CaptureReceiveScope().IsCurrent(),"Only completed component initialization and a fresh publication permit subsequent receipts.");

            componentType.GetField("saveId",instance).SetValue(component,"another-lineage");
            Assert(read.GetStatus().SaveIdentity!=expected,"Different save lineage cannot reuse original binding.");
            componentType.GetField("saveId",instance).SetValue(component,"synthetic-lineage");
            Assert(read.GetStatus().SaveIdentity==expected,"Original lineage and path yield stable identity.");
            pathProperty.SetValue(null,Path.Combine(root,"different.rws"));
            Assert(read.GetStatus().SaveIdentity==null,"Save path drift has no identity until inventory reattaches.");
            pathProperty.SetValue(null,file);File.Delete(file);
            Assert(read.GetStatus().SaveIdentity==null,"Missing save file cannot authorize a save binding.");
            Assert(!scopes.CaptureReceiveScope().IsCurrent(),"Deleting the save invalidates a captured receive scope.");
            publish.Invoke(service,null);Assert(scopes.CaptureReceiveScope()==null,"Missing save path publishes no adoptable scope.");
            File.WriteAllText(file,"synthetic restored identity");publish.Invoke(service,null);var beforeShutdown=scopes.CaptureReceiveScope();
            type.GetMethod("Shutdown").Invoke(service,new object[]{new Utils.Framework.ExtensionHostContext()});
            Assert(scopes.CaptureReceiveScope()==null&&!beforeShutdown.IsCurrent(),"Shutdown clears publication and expires prior receive scope.");
        }
        finally {active.SetValue(null,oldActive);Current.Game=oldGame;pathProperty.SetValue(null,oldPath);Directory.Delete(root,true);}
    }
}
