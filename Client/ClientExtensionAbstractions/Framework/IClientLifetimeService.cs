using System;
using System.Threading;
using Utils.Framework;

namespace PhinixClient.Framework
{
    public interface IClientLifetimeService
    {
        IClientLifetimeScope ForModule(IPhinixExtensionModule module);
    }
    public interface IClientLifetimeScope : IDisposable
    {
        string OwnerId { get; }
        bool IsClosed { get; }
        CancellationToken Stopping { get; }
        // Transfers an owned synchronous resource. Never pass borrowed host services/APIs.
        IDisposable Own(IDisposable resource);
        // Subscription and callback run on the main thread; late callbacks are suppressed.
        IDisposable Subscribe(Action<Action> subscribe,Action<Action> unsubscribe,Action callback);
    }
}
