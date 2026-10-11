using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Utils.Framework;

namespace PhinixClient.Framework
{
    internal sealed class ClientLifetimeService : IClientLifetimeService,IExtensionModuleLifecycleObserver
    {
        private readonly Func<bool> mainThread;
        private readonly Action<string> log;
        private readonly HashSet<Type> types=new HashSet<Type>();
        private readonly Dictionary<IPhinixExtensionModule,Owner> owners=new Dictionary<IPhinixExtensionModule,Owner>(Identity.Instance);
        private int count;
        private readonly HashSet<string> reported=new HashSet<string>(StringComparer.Ordinal);
        internal ClientLifetimeService(Func<bool> mainThread,Action<string> log) {this.mainThread=mainThread;this.log=log;}
        internal void SetModuleTypes(IEnumerable<Type> values) {MainThread();foreach(var type in values) types.Add(type);}
        private void MainThread() {if(!mainThread()) throw new InvalidOperationException("Lifetime operations require main thread.");}
        public void OnActivating(IPhinixExtensionModule module)
        {
            MainThread();if(module==null || !types.Contains(module.GetType()) ||
                !string.Equals(module.GetType().GetCustomAttributes(typeof(PhinixExtensionAttribute),true).Cast<PhinixExtensionAttribute>().SingleOrDefault()?.ExtensionId??module.GetType().Name,module.ExtensionId,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("LifetimeModuleNotRegistered");
            if(!owners.ContainsKey(module)) owners.Add(module,new Owner(module.ExtensionId));
        }
        public void OnStopped(IPhinixExtensionModule module)
        {
            MainThread();if(module==null || !owners.TryGetValue(module,out var scopes)) return;
            foreach(var scope in scopes.Scopes.ToArray()) scope.Dispose();owners.Remove(module);
        }
        public IClientLifetimeScope ForModule(IPhinixExtensionModule module)
        {
            MainThread();if(module==null || !owners.TryGetValue(module,out var scopes)) throw new InvalidOperationException("LifetimeModuleNotActive");
            scopes.Scopes.RemoveAll(s=>s.IsClosed);if(scopes.Scopes.Count>=16) throw new InvalidOperationException("LifetimeScopeLimit");
            var scope=new Scope(this,scopes.Id);scopes.Scopes.Add(scope);return scope;
        }
        private void Report(string owner,string stage,Exception error)
        {
            string key=owner+"/"+stage+"/"+error.GetType().FullName;
            lock(reported) {if(reported.Count>=256 || !reported.Add(key)) return;}
            try {log?.Invoke("Lifetime: owner="+owner+", stage="+stage+", exception="+error.GetType().Name);}catch {}
        }
        private sealed class Owner
        {internal readonly string Id;internal readonly List<Scope> Scopes=new List<Scope>();internal Owner(string id) {Id=id;}}
        private sealed class Scope:IClientLifetimeScope
        {
            private readonly ClientLifetimeService service;
            private readonly CancellationTokenSource stopping=new CancellationTokenSource();
            private readonly CancellationToken token;
            private readonly List<Resource> resources=new List<Resource>();
            internal Scope(ClientLifetimeService service,string owner) {this.service=service;OwnerId=owner;token=stopping.Token;}
            public string OwnerId {get;}
            public bool IsClosed {get;private set;}
            public CancellationToken Stopping => token;
            private void Require() {service.MainThread();if(IsClosed) throw new ObjectDisposedException("ModuleLifetime");}
            private void Capacity() {if(resources.Count>=256 || service.count>=1024) throw new InvalidOperationException("LifetimeResourceLimit");}
            public IDisposable Own(IDisposable resource)
            {
                Require();if(resource==null) throw new ArgumentNullException(nameof(resource));Capacity();
                if(resources.Any(r=>ReferenceEquals(r.Value,resource))) throw new InvalidOperationException("LifetimeResourceAlreadyOwned");
                var owned=new Resource(this,resource);resources.Add(owned);service.count++;return owned;
            }
            public IDisposable Subscribe(Action<Action> subscribe,Action<Action> unsubscribe,Action callback)
            {
                Require();Capacity();if(subscribe==null || unsubscribe==null || callback==null) throw new ArgumentNullException("subscription");
                var subscription=new Subscription(this,unsubscribe,callback);var owned=(Resource)Own(subscription);
                try
                {
                    subscribe(subscription.Invoke);
                    if(IsClosed || owned.Value==null)
                        try {unsubscribe(subscription.Invoke);}catch(Exception ex) {service.Report(OwnerId,"SubscribeClosed",ex);}
                    return owned;
                }
                catch {owned.Dispose();throw;}
            }
            public void Dispose()
            {
                service.MainThread();if(IsClosed) return;IsClosed=true;
                try {stopping.Cancel();}catch(Exception ex) {service.Report(OwnerId,"Cancel",ex);}
                foreach(var resource in resources.ToArray().Reverse()) resource.Dispose();stopping.Dispose();
            }
            private sealed class Subscription:IDisposable
            {
                private readonly Scope scope;
                private Action<Action> unsubscribe;
                private Action callback;
                internal Subscription(Scope scope,Action<Action> unsubscribe,Action callback) {this.scope=scope;this.unsubscribe=unsubscribe;this.callback=callback;}
                internal void Invoke()
                {
                    if(scope.IsClosed || callback==null) return;
                    if(!scope.service.mainThread()) {scope.service.Report(scope.OwnerId,"CallbackThread",new InvalidOperationException());return;}
                    try {using(new ClientGuiScope(true)) callback?.Invoke();}catch(Exception ex) {scope.service.Report(scope.OwnerId,"Callback",ex);}
                }
                public void Dispose() {var release=unsubscribe;unsubscribe=null;callback=null;release?.Invoke(Invoke);}
            }
            private sealed class Resource:IDisposable
            {
                private readonly Scope scope;
                internal IDisposable Value;
                internal Resource(Scope scope,IDisposable value) {this.scope=scope;Value=value;}
                public void Dispose()
                {
                    scope.service.MainThread();var value=Value;if(value==null) return;Value=null;scope.resources.Remove(this);scope.service.count--;
                    try {value.Dispose();}catch(Exception ex) {scope.service.Report(scope.OwnerId,"Dispose",ex);}
                }
            }
        }
        private sealed class Identity:IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly Identity Instance=new Identity();
            public bool Equals(IPhinixExtensionModule x,IPhinixExtensionModule y)=>ReferenceEquals(x,y);
            public int GetHashCode(IPhinixExtensionModule value)=>RuntimeHelpers.GetHashCode(value);
        }
    }
}
