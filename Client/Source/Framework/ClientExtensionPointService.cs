using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PhinixClient;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Host-neutral contribution plumbing. Business context types stay in the declaring plugin's Contracts.
    internal sealed class ClientExtensionPointService : IClientExtensionPointService, IExtensionModuleLifecycleObserver
    {
        private readonly Func<bool> mainThread;
        private readonly Action<string> log;
        private readonly HashSet<Type> types=new HashSet<Type>();
        private readonly Dictionary<IPhinixExtensionModule,List<Scope>> owners=new Dictionary<IPhinixExtensionModule,List<Scope>>(Identity.Instance);
        private readonly Dictionary<string,Point> points=new Dictionary<string,Point>(StringComparer.Ordinal);
        private readonly List<Contribution> contributions=new List<Contribution>();
        private readonly HashSet<ActionSet> sets=new HashSet<ActionSet>();
        private long revision,sequence;
        internal ClientExtensionPointService(Func<bool> mainThread,Action<string> log) { this.mainThread=mainThread; this.log=log; }
        internal void SetModuleTypes(IEnumerable<Type> values) { MainThread(); types.Clear(); foreach(var value in values) types.Add(value); }
        public long Revision { get { MainThread(); return revision; } }
        public void OnActivating(IPhinixExtensionModule module)
        {
            MainThread();
            if(module==null || !types.Contains(module.GetType())) throw new InvalidOperationException("ContributionModuleNotRegistered");
            if(!owners.ContainsKey(module)) owners.Add(module,new List<Scope>());
        }
        public void OnStopped(IPhinixExtensionModule module)
        {
            MainThread(); if(module==null || !owners.TryGetValue(module,out var scopes)) return;
            foreach(var scope in scopes.ToArray()) Close(scope);
            owners.Remove(module);
        }
        public IClientExtensionPointScope ForModule(IPhinixExtensionModule module)
        {
            MainThread();
            if(module==null || !owners.TryGetValue(module,out var scopes)) throw new InvalidOperationException("ContributionModuleNotActive");
            scopes.RemoveAll(s=>s.Closed);
            if(scopes.Count>=16) throw new InvalidOperationException("ContributionScopeLimit");
            var result=new Scope(this,module.ExtensionId); scopes.Add(result); return result;
        }
        public IReadOnlyList<ClientExtensionPointInfo> CapturePoints()
        {
            MainThread(); return points.Values.OrderBy(p=>p.Id,StringComparer.Ordinal).Select(p=>new ClientExtensionPointInfo(p.Id,p.Owner.ModuleId,p.Context,
                contributions.Count(c=>c.Id==p.Id && c.Context==p.Context && !c.Owner.Closed))).ToList().AsReadOnly();
        }
        private void MainThread() { if(!mainThread()) throw new InvalidOperationException("Contributions require the game main thread."); }
        private static void Id(string value)
        { if(value==null || value.Length>160 || !Regex.IsMatch(value,@"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z")) throw new ArgumentException("Invalid contribution identity."); }
        private static void Text(string value,int maximum)
        { if(string.IsNullOrWhiteSpace(value) || value.Length>maximum || value.Any(char.IsControl)) throw new ArgumentException("Invalid contribution text."); }
        private void Require(Scope scope) { MainThread(); if(scope.Closed) throw new ObjectDisposedException("ContributionScope"); }
        private void Close(Scope scope)
        {
            MainThread(); if(scope.Closed) return; scope.Closed=true;
            foreach(var set in sets.Where(s=>ReferenceEquals(s.Point.Owner,scope) || s.Scopes.Contains(scope)).ToArray()) set.Dispose();
            foreach(var point in points.Values.Where(p=>ReferenceEquals(p.Owner,scope)).ToArray()) points.Remove(point.Id);
            contributions.RemoveAll(c=>ReferenceEquals(c.Owner,scope)); revision++;
        }
        private Point<T> Define<T>(Scope owner,string id) where T:class
        {
            Require(owner); Id(id);
            if(!id.StartsWith(owner.ModuleId+".",StringComparison.Ordinal) || points.ContainsKey(id)) throw new InvalidOperationException("ContributionPointOwnershipConflict");
            if(points.Count>=256 || owner.Registrations>=128) throw new InvalidOperationException("ContributionRegistrationLimit");
            var result=new Point<T>(this,owner,id); points.Add(id,result); owner.Registrations++; revision++;
            foreach(var contribution in contributions.Where(c=>c.Id==id && c.Context!=typeof(T))) Report(contribution.Owner.ModuleId,"ContextMismatch",null);
            return result;
        }
        private void Contribute<T>(Scope owner,string id,Func<T,IEnumerable<ClientActionDefinition>> factory,float order) where T:class
        {
            Require(owner); Id(id); if(factory==null) throw new ArgumentNullException(nameof(factory));
            if(float.IsNaN(order) || float.IsInfinity(order) || Math.Abs(order)>1000000) throw new ArgumentOutOfRangeException(nameof(order));
            if(points.TryGetValue(id,out var point) && point.Context!=typeof(T)) throw new InvalidOperationException("ContributionContextMismatch");
            if(contributions.Any(c=>c.Owner.ModuleId==owner.ModuleId && c.Id==id)) throw new InvalidOperationException("ContributionAlreadyRegistered");
            if(contributions.Count>=1024 || owner.Registrations>=128) throw new InvalidOperationException("ContributionRegistrationLimit");
            contributions.Add(new Contribution {Owner=owner,Id=id,Context=typeof(T),Order=order,Sequence=++sequence,Factory=context=>factory((T)context)}); owner.Registrations++; revision++;
        }
        private IClientActionSet Collect(Point point,object context,Func<bool> current)
        {
            Require(point.Owner);
            if(context==null) throw new ArgumentNullException(nameof(context));
            if(!points.TryGetValue(point.Id,out var existing) || !ReferenceEquals(existing,point)) throw new ObjectDisposedException("ContributionPoint");
            if(sets.Count>=64) throw new InvalidOperationException("ContributionSetLimit");
            var result=new ActionSet(this,point,current); sets.Add(result);
            try
            {
                foreach(var contribution in contributions.Where(c=>c.Id==point.Id && c.Context==point.Context && !c.Owner.Closed)
                    .OrderBy(c=>c.Order).ThenBy(c=>c.Owner.ModuleId,StringComparer.Ordinal).ThenBy(c=>c.Sequence).ToArray())
                {
                    if(result.Closed || point.Owner.Closed) break;
                    if(contribution.Owner.Closed) continue;
                    var batch=new List<ClientActionDefinition>();
                    try
                    {
                        // Factory and its lazy enumeration both stay inside the provider's exception/GUI boundary.
                        using(new ClientGuiScope(true))
                        {
                            var seen=new HashSet<string>(StringComparer.Ordinal);
                            foreach(var action in contribution.Factory(context)??new ClientActionDefinition[0])
                            {
                                if(action==null || batch.Count>=32) throw new InvalidOperationException("ContributionActionLimit");
                                Id(action.Id); if(action.Id.Length>64) throw new ArgumentException("Action identity limit."); Text(action.Label,256);
                                if(!seen.Add(action.Id) || action.Execute==null && action.DisabledReason==null) throw new InvalidOperationException("InvalidContributionAction");
                                if(action.DisabledReason!=null) Text(action.DisabledReason,512);
                                batch.Add(action);
                            }
                        }
                    }
                    catch(Exception error) { Report(contribution.Owner.ModuleId,"Collect",error); continue; }
                    if(result.Closed || point.Owner.Closed) break;
                    if(contribution.Owner.Closed) continue;
                    if(result.Items.Count+batch.Count>128) { Report(contribution.Owner.ModuleId,"ActionLimit",null); continue; }
                    result.Scopes.Add(contribution.Owner);
                    foreach(var action in batch) result.Items.Add(new ContributedAction(result,contribution.Owner,action));
                }
                result.Seal(); return result;
            }
            catch { result.Dispose(); throw; }
        }
        private void Report(string owner,string phase,Exception error)
        { try { log?.Invoke("Contribution: owner="+owner+", phase="+phase+", exception="+(error?.GetType().Name??"none")); } catch { } }
        private sealed class Scope:IClientExtensionPointScope
        {
            private readonly ClientExtensionPointService service;
            internal readonly string ModuleId; internal bool Closed; internal int Registrations;
            internal Scope(ClientExtensionPointService service,string moduleId) { this.service=service; ModuleId=moduleId; }
            public IClientActionPoint<T> DefineActions<T>(string pointId) where T:class => service.Define<T>(this,pointId);
            public void ContributeActions<T>(string pointId,Func<T,IEnumerable<ClientActionDefinition>> factory,float order=0) where T:class => service.Contribute(this,pointId,factory,order);
            public void Dispose()=>service.Close(this);
        }
        private abstract class Point
        {
            internal readonly ClientExtensionPointService Service; internal readonly Scope Owner; internal readonly string Id; internal readonly Type Context;
            protected Point(ClientExtensionPointService service,Scope owner,string id,Type context) { Service=service; Owner=owner; Id=id; Context=context; }
        }
        private sealed class Point<T>:Point,IClientActionPoint<T> where T:class
        {
            internal Point(ClientExtensionPointService service,Scope owner,string id):base(service,owner,id,typeof(T)) { }
            public IClientActionSet Collect(T context,Func<bool> isContextCurrent=null)=>Service.Collect(this,context,isContextCurrent);
        }
        private sealed class Contribution
        { internal Scope Owner; internal string Id; internal Type Context; internal float Order; internal long Sequence; internal Func<object,IEnumerable<ClientActionDefinition>> Factory; }
        private sealed class ActionSet:IClientActionSet
        {
            internal readonly ClientExtensionPointService Service; internal readonly Point Point;
            internal readonly HashSet<Scope> Scopes=new HashSet<Scope>(); internal readonly List<ContributedAction> Items=new List<ContributedAction>();
            internal bool Closed; internal Func<bool> Current; private IReadOnlyList<IClientContributedAction> actions;
            internal ActionSet(ClientExtensionPointService service,Point point,Func<bool> current) { Service=service; Point=point; Current=current; }
            internal void Seal() { actions=Items.Cast<IClientContributedAction>().ToList().AsReadOnly(); }
            public IReadOnlyList<IClientContributedAction> Actions { get { Service.MainThread(); return actions??Array.AsReadOnly(new IClientContributedAction[0]); } }
            public void Dispose()
            {
                Service.MainThread(); if(Closed) return; Closed=true; Current=null;
                foreach(var action in Items) action.Clear(); Scopes.Clear(); Service.sets.Remove(this);
            }
        }
        private sealed class ContributedAction:IClientContributedAction
        {
            private readonly ActionSet set; private readonly Scope owner; private Action execute; private bool attempted;
            internal ContributedAction(ActionSet set,Scope owner,ClientActionDefinition definition)
            { this.set=set; this.owner=owner; execute=definition.Execute; Id=definition.Id; Label=definition.Label; DisabledReason=definition.DisabledReason; }
            public string OwnerId=>owner.ModuleId; public string Id {get;} public string Label {get;} public string DisabledReason {get;}
            internal void Clear() { execute=null; }
            public ClientActionExecutionResult TryExecute()
            {
                set.Service.MainThread();
                if(set.Closed || set.Point.Owner.Closed || owner.Closed) return new ClientActionExecutionResult(false,false,"ContributionClosed");
                if(DisabledReason!=null) return new ClientActionExecutionResult(false,false,"Disabled");
                if(attempted) return new ClientActionExecutionResult(false,false,"AlreadyInvoked");
                try { if(set.Current!=null && !set.Current()) return new ClientActionExecutionResult(false,false,"ContextExpired"); }
                catch(Exception error) { set.Service.Report(OwnerId,"Context",error); return new ClientActionExecutionResult(false,false,"ContextUnavailable"); }
                if(set.Closed || set.Point.Owner.Closed || owner.Closed) return new ClientActionExecutionResult(false,false,"ContributionClosed");
                attempted=true; var callback=execute; execute=null;
                try { using(new ClientGuiScope(true)) callback(); return new ClientActionExecutionResult(true,true,"Completed"); }
                catch(Exception error) { set.Service.Report(OwnerId,"Execute",error); return new ClientActionExecutionResult(false,true,"CallbackFailed"); }
            }
        }
        private sealed class Identity:IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly Identity Instance=new Identity();
            public bool Equals(IPhinixExtensionModule a,IPhinixExtensionModule b)=>ReferenceEquals(a,b);
            public int GetHashCode(IPhinixExtensionModule value)=>RuntimeHelpers.GetHashCode(value);
        }
    }
}
