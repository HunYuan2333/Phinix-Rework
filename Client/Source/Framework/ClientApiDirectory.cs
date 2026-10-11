using System;
using System.Collections.Generic;
using System.Linq;
using Utils.Framework;

namespace PhinixClient.Framework
{
    internal sealed class ClientApiDirectory : IClientApiDirectory,IDisposable
    {
        private readonly ExtensionApiRegistry registry;
        private readonly Func<IEnumerable<ExtensionDiscoveryResult>> results;
        private readonly HashSet<string> activationRequired;
        private readonly Func<bool> mainThread;
        private bool disposed;
        internal ClientApiDirectory(ExtensionApiRegistry registry,Func<IEnumerable<ExtensionDiscoveryResult>> results,IEnumerable<Type> moduleTypes,Func<bool> mainThread)
        {
            this.registry=registry;this.results=results;this.mainThread=mainThread;
            activationRequired=new HashSet<string>(moduleTypes.Where(t=>typeof(IActivatablePhinixExtensionModule).IsAssignableFrom(t))
                .Select(t=>t.GetCustomAttributes(typeof(PhinixExtensionAttribute),true).Cast<PhinixExtensionAttribute>().SingleOrDefault()?.ExtensionId??t.Name),StringComparer.OrdinalIgnoreCase);
        }
        private void RequireMainThread() {if(!mainThread()) throw new InvalidOperationException("Provider queries require main thread.");}
        private ClientApiProviderState State(string owner)
        {
            if(owner=="builtin.host") return ClientApiProviderState.Available;
            var row=(results()??new ExtensionDiscoveryResult[0]).SingleOrDefault(r=>string.Equals(r.ExtensionId,owner,StringComparison.OrdinalIgnoreCase));
            if(row?.State==ExtensionModuleState.Active || row?.State==ExtensionModuleState.Registered && !activationRequired.Contains(owner)) return ClientApiProviderState.Available;
            return row?.State==ExtensionModuleState.Registered || row?.State==ExtensionModuleState.Discovered ? ClientApiProviderState.Registered:ClientApiProviderState.Unavailable;
        }
        public IReadOnlyList<IClientApiProvider<T>> Capture<T>() where T:class
        {
            RequireMainThread();if(disposed) throw new ObjectDisposedException(nameof(ClientApiDirectory));return registry.CaptureProviders<T>().Select(p=>(IClientApiProvider<T>)new Provider<T>(this,p.OwnerId,p.RegistrationId,State(p.OwnerId))).ToList().AsReadOnly();
        }
        public void Dispose() {disposed=true;}
        private sealed class Provider<T>:IClientApiProvider<T> where T:class
        {
            private readonly ClientApiDirectory directory;
            internal Provider(ClientApiDirectory directory,string owner,long id,ClientApiProviderState state) {this.directory=directory;OwnerId=owner;RegistrationId=id;State=state;}
            public string OwnerId { get; }
            public long RegistrationId { get; }
            public ClientApiProviderState State { get; }
            public bool TryGet(out T api)
            {
                directory.RequireMainThread();api=null;
                return !directory.disposed && directory.State(OwnerId)==ClientApiProviderState.Available && directory.registry.TryGetProvider(RegistrationId,out api);
            }
        }
    }
}
