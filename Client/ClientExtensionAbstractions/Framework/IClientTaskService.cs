using System;
using System.Threading;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Bind during Activate using the actual module instance. The returned scope is owned; the service is borrowed.
    public interface IClientTaskService
    {
        IClientTaskScope ForModule(IPhinixExtensionModule module);
    }

    public interface IClientTaskScope : IDisposable
    {
        // Callback runs synchronously on the game main thread. Completion is callback completion, never remote confirmation.
        // Cancellation is checked before execution. A running callback is not aborted or rolled back.
        IClientTaskHandle Submit(Action action, CancellationToken cancellation = default);
    }

    public enum ClientTaskState { Queued, Running, Succeeded, Rejected, Canceled, Failed }

    public interface IClientTaskHandle
    {
        // Thread-safe immutable observation; no waiting on the game thread and no raw exception exposure.
        ClientTaskSnapshot Capture();
    }

    public sealed class ClientTaskSnapshot
    {
        public ClientTaskSnapshot(long id, ClientTaskState state, string code)
        { Id=id; State=state; Code=code; }
        public long Id { get; }
        public ClientTaskState State { get; }
        public string Code { get; }
        public bool Terminal => State!=ClientTaskState.Queued && State!=ClientTaskState.Running;
    }
}
