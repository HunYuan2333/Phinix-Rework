using System;
using System.Reflection;
using LiteNetLib;

namespace Connections
{
    internal sealed class PinnedClientUdpReceiveGuard
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly NetManager manager;
        private readonly PinnedUdpReceiveBudget budget;
        private readonly Action<byte[], int, int, NetEndPoint> receive;
        private readonly Action<Exception> failed;
        private bool stopped;
        internal object PeerGate { get; }

        private PinnedClientUdpReceiveGuard(NetManager manager, object peerGate,
            Action<byte[], int, int, NetEndPoint> receive, Action<Exception> failed)
        {
            this.manager = manager;
            PeerGate = peerGate;
            this.receive = receive;
            this.failed = failed;
            budget = new PinnedUdpReceiveBudget(manager);
        }
        internal void Reset()
        {
            if (manager.IsRunning) throw new InvalidOperationException("Receive guard reset requires a stopped transport.");
            lock (PeerGate) stopped = false;
        }
        private void Receive(byte[] bytes, int count, int error, NetEndPoint endpoint)
        {
            lock (PeerGate)
            {
                if (stopped) return;
                try
                {
                    if (error == 0 && !budget.CanReceive(bytes, count, endpoint)) return;
                    receive(bytes, count, error, endpoint);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    stopped = true;
                    failed(exception);
                }
            }
        }
        internal static PinnedClientUdpReceiveGuard Install(NetManager manager, Action<Exception> failed)
        {
            if (manager.IsRunning || manager.UnsyncedEvents) throw new InvalidOperationException("Receive guard requires queued callbacks before startup.");
            PinnedUdpReceiveBudget.ValidateBackend();
            object peers = typeof(NetManager).GetField("_peers", Private)?.GetValue(manager)
                ?? throw new NotSupportedException("UDP peer gate unavailable.");
            object socket = typeof(NetManager).GetField("_socket", Private)?.GetValue(manager)
                ?? throw new NotSupportedException("UDP socket callback unavailable.");
            var field = socket.GetType().GetField("_onMessageReceived", Private)
                ?? throw new NotSupportedException("UDP callback layout changed.");
            var prior = field.GetValue(socket) as Delegate;
            if (prior == null || prior.GetInvocationList().Length != 1 || !ReferenceEquals(prior.Target, manager))
                throw new NotSupportedException("UDP callback ownership changed.");
            var receive = (Action<byte[], int, int, NetEndPoint>)prior.Method.CreateDelegate(typeof(Action<byte[], int, int, NetEndPoint>), prior.Target);
            var guard = new PinnedClientUdpReceiveGuard(manager, peers, receive, failed);
            var callback = Delegate.CreateDelegate(field.FieldType, guard, typeof(PinnedClientUdpReceiveGuard).GetMethod(nameof(Receive), Private));
            field.SetValue(socket, callback);
            if (!ReferenceEquals(field.GetValue(socket), callback)) throw new NotSupportedException("UDP receive guard could not be installed.");
            return guard;
        }
    }
}
