using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Utils;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // One queue for current tasks and the older void adapter. An accepted action is never evicted for a newer one.
    internal sealed class ClientMainThreadDispatcher : IClientMainThreadDispatcher, IClientDispatcherDiagnostics,
        IClientTaskService, IExtensionModuleLifecycleObserver
    {
        private const int MaxPendingActions = 500;
        private const int MaxActionsPerFrame = 100;
        private readonly Queue<Record> pendingActions = new Queue<Record>();
        private readonly object syncRoot = new object();
        private readonly Action<string, LogLevel> log;
        private readonly Func<bool> isMainThread;
        private readonly HashSet<Type> moduleTypes = new HashSet<Type>();
        private readonly HashSet<IPhinixExtensionModule> modules = new HashSet<IPhinixExtensionModule>(ModuleIdentity.Instance);
        private readonly Dictionary<IPhinixExtensionModule, List<Scope>> scopes = new Dictionary<IPhinixExtensionModule, List<Scope>>(ModuleIdentity.Instance);
        private long droppedCount, sequence;

        public ClientMainThreadDispatcher(Action<string, LogLevel> log = null, Func<bool> isMainThread = null)
        { this.log=log; this.isMainThread=isMainThread??(()=>true); }
        public int PendingCount { get { lock(syncRoot) return pendingActions.Count; } }
        public long DroppedCount { get { lock(syncRoot) return droppedCount; } }

        internal void SetModuleTypes(IEnumerable<Type> types)
        {
            MainThread();
            lock(syncRoot) { moduleTypes.Clear(); foreach(var type in types) moduleTypes.Add(type); }
        }
        public void OnActivating(IPhinixExtensionModule module)
        {
            MainThread();
            lock(syncRoot)
            {
                if(module==null || !moduleTypes.Contains(module.GetType())) throw new InvalidOperationException("TaskModuleNotRegistered");
                modules.Add(module);
            }
        }
        public void OnStopped(IPhinixExtensionModule module)
        {
            MainThread();
            lock(syncRoot)
            {
                if(module==null) return;
                modules.Remove(module);
                if(scopes.TryGetValue(module,out var owned)) { foreach(var scope in owned) Close(scope,"ModuleStopped"); scopes.Remove(module); }
            }
        }
        public IClientTaskScope ForModule(IPhinixExtensionModule module)
        {
            MainThread();
            lock(syncRoot)
            {
                if(module==null || !modules.Contains(module)) throw new InvalidOperationException("TaskModuleNotActive");
                if(!scopes.TryGetValue(module,out var owned)) scopes.Add(module,owned=new List<Scope>());
                owned.RemoveAll(scope=>scope.Closed);
                if(owned.Count>=16) throw new InvalidOperationException("TaskScopeLimit");
                var result=new Scope(this,module.ExtensionId); owned.Add(result); return result;
            }
        }
        public void Enqueue(Action action)
        {
            if(action==null) return;
            Record record=Submit(null,action,CancellationToken.None);
            if(record.Capture().State==ClientTaskState.Rejected) Report("Legacy main-thread submission rejected: QueueFull",LogLevel.WARNING);
        }
        private Record Submit(Scope scope,Action action,CancellationToken token)
        {
            if(action==null) throw new ArgumentNullException(nameof(action));
            lock(syncRoot)
            {
                var record=new Record(++sequence,scope,action,token);
                if(scope!=null && scope.Closed) record.Complete(ClientTaskState.Rejected,"ScopeClosed");
                else if(token.IsCancellationRequested) record.Complete(ClientTaskState.Canceled,"RequestCanceled");
                else if(pendingActions.Count>=MaxPendingActions)
                { droppedCount++; record.Complete(ClientTaskState.Rejected,"QueueFull"); }
                else pendingActions.Enqueue(record);
                return record;
            }
        }
        // Removes owned callbacks immediately on stop/dispose, rather than retaining them until a later frame.
        private void Close(Scope scope,string code)
        {
            lock(syncRoot)
            {
                if(scope.Closed) return;
                scope.Closed=true;
                int count=pendingActions.Count;
                for(int i=0;i<count;i++)
                {
                    var record=pendingActions.Dequeue();
                    if(ReferenceEquals(record.Owner,scope)) record.Complete(ClientTaskState.Canceled,code);
                    else pendingActions.Enqueue(record);
                }
            }
        }
        public void DrainPendingActions()
        {
            MainThread();
            for(int processed=0;processed<MaxActionsPerFrame;processed++)
            {
                Record record; Action action;
                lock(syncRoot)
                {
                    if(pendingActions.Count==0) return;
                    record=pendingActions.Dequeue();
                    if(record.Owner?.Closed==true) { record.Complete(ClientTaskState.Canceled,"ScopeClosed"); continue; }
                    if(record.Token.IsCancellationRequested) { record.Complete(ClientTaskState.Canceled,"RequestCanceled"); continue; }
                    action=record.TakeAction();
                }
                try { action(); record.Complete(ClientTaskState.Succeeded,"Completed"); }
                catch(Exception error)
                {
                    record.Complete(ClientTaskState.Failed,"CallbackFailed");
                    Report("Main-thread callback failed: owner="+record.ModuleId+", task="+record.Id+", exception="+error.GetType().Name,LogLevel.ERROR);
                }
            }
        }
        private void Report(string message,LogLevel level) { try { log?.Invoke(message,level); } catch { } }
        private void MainThread() { if(!isMainThread()) throw new InvalidOperationException("Task bindings and execution require the game main thread."); }
        private sealed class Scope : IClientTaskScope
        {
            private readonly ClientMainThreadDispatcher owner;
            internal readonly string ModuleId;
            internal bool Closed;
            internal Scope(ClientMainThreadDispatcher owner,string moduleId) { this.owner=owner; ModuleId=moduleId; }
            public IClientTaskHandle Submit(Action action,CancellationToken cancellation=default) => owner.Submit(this,action,cancellation);
            public void Dispose() => owner.Close(this,"ScopeClosed");
        }
        private sealed class Record : IClientTaskHandle
        {
            private readonly object gate=new object();
            internal readonly long Id;
            internal Scope Owner;
            internal CancellationToken Token;
            internal readonly string ModuleId;
            private Action action;
            private ClientTaskState state=ClientTaskState.Queued;
            private string code="Queued";
            internal Record(long id,Scope owner,Action action,CancellationToken token) { Id=id; Owner=owner; ModuleId=owner?.ModuleId??"host"; this.action=action; Token=token; }
            internal Action TakeAction() { lock(gate) { var result=action; action=null; state=ClientTaskState.Running; code="Running"; return result; } }
            internal void Complete(ClientTaskState value,string reason) { lock(gate) { action=null; Owner=null; Token=CancellationToken.None; state=value; code=reason; } }
            public ClientTaskSnapshot Capture() { lock(gate) return new ClientTaskSnapshot(Id,state,code); }
        }
        private sealed class ModuleIdentity : IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly ModuleIdentity Instance=new ModuleIdentity();
            public bool Equals(IPhinixExtensionModule a,IPhinixExtensionModule b) => ReferenceEquals(a,b);
            public int GetHashCode(IPhinixExtensionModule module) => RuntimeHelpers.GetHashCode(module);
        }
    }
}
