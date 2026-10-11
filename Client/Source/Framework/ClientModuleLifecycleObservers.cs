using System;
using System.Collections.Generic;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Shared registry has one observer slot; each host service retains its own ownership rules.
    internal sealed class ClientModuleLifecycleObservers : IExtensionModuleLifecycleObserver
    {
        private readonly IExtensionModuleLifecycleObserver[] observers;
        public ClientModuleLifecycleObservers(params IExtensionModuleLifecycleObserver[] observers)
        { this.observers = observers; }
        public void OnActivating(IPhinixExtensionModule module)
        { foreach (var observer in observers) observer.OnActivating(module); }
        public void OnStopped(IPhinixExtensionModule module)
        {
            var failures = new List<Exception>();
            for (int i = observers.Length - 1; i >= 0; i--)
                try { observers[i].OnStopped(module); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Module host resource cleanup failed.", failures);
        }
    }
}
