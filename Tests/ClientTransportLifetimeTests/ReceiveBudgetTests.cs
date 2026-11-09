using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Connections;
using LiteNetLib;
using LiteNetLib.Utils;

internal static class ReceiveBudgetTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void Run()
    {
        var listener = new EventBasedNetListener();
        var host = new NetManager(listener, "Phinix");
        NetPeer remote = null;
        listener.PeerConnectedEvent += peer => remote = peer;
        using (var client = new NetClient())
        using (var paused = new ManualResetEventSlim())
        using (var resume = new ManualResetEventSlim())
        using (var received = new ManualResetEventSlim())
        {
            var manager = (NetManager)typeof(NetClient).GetField("clientNetManager", Private).GetValue(client);
            object socket = typeof(NetManager).GetField("_socket", Private).GetValue(manager);
            var callback = (Delegate)socket.GetType().GetField("_onMessageReceived", Private).GetValue(socket);
            Check(callback.Target.GetType().Name == "PinnedClientUdpReceiveGuard", "Client socket guard missing");
            var receive = (Action<byte[], int, int, NetEndPoint>)callback.Method.CreateDelegate(typeof(Action<byte[], int, int, NetEndPoint>), callback.Target);
            object gate = callback.Target.GetType().GetProperty("PeerGate", Private).GetValue(callback.Target);
            byte[] expected = Enumerable.Repeat((byte)0x5a, 1024 * 1024).ToArray();
            int small = 0;
            client.RegisterPacketHandler("large", (_, __, bytes) =>
            {
                Check(expected.SequenceEqual(bytes), "Valid fragmented client payload changed");
                received.Set(); paused.Set(); Check(resume.Wait(15000), "Client poll barrier timeout");
            });
            client.RegisterPacketHandler("small", (_, __, bytes) => Interlocked.Increment(ref small));
            Check(host.Start(), "Raw server start failed");
            client.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
            try
            {
                Check(SpinWait.SpinUntil(() => { host.PollEvents(); return remote != null && client.Connected; }, 5000), "Client handshake timeout");
                var writer = new NetDataWriter(); writer.Put("large"); writer.Put(expected);
                remote.Send(writer, SendOptions.ReliableOrdered);
                Check(paused.Wait(10000) && received.IsSet, "Client did not receive valid 1 MiB message");
                NetPeer peer;
                lock (gate) peer = manager.GetPeers().Single();
                var fragment = new byte[10]; fragment[0] = 0x81; fragment[7] = fragment[8] = 0xff;
                receive(fragment, fragment.Length, 0, peer.EndPoint);
                var merged = new byte[] { 0x13, 10, 0 }.Concat(fragment).ToArray();
                receive(merged, merged.Length, 0, peer.EndPoint);
                var held = typeof(NetPeer).GetField("_holdedFragments", Private);
                lock (gate) Check(((IDictionary)held.GetValue(peer)).Count == 0, "Oversized fragment allocated client reassembly state");
                fragment[7] = 0; fragment[8] = 16;
                for (ushort id = 0; id < 12; id++)
                { fragment[1] = fragment[3] = (byte)id; receive(fragment, fragment.Length, 0, peer.EndPoint); }
                lock (gate) Check(((IDictionary)held.GetValue(peer)).Count == 1, "Client incomplete group budget exceeded");
                var queue = (ICollection)typeof(NetManager).GetField("_netEventsQueue", Private).GetValue(manager);
                writer.Reset(); writer.Put("small"); writer.Put((byte)1);
                var datagram = new byte[] { 0 }.Concat(writer.CopyData()).ToArray();
                lock (queue)
                {
                    var eventType = queue.GetType().GetGenericArguments()[0];
                    var enqueue = queue.GetType().GetMethod("Enqueue");
                    for (int i = 0; i < 32; i++)
                    {
                        object evt = Activator.CreateInstance(eventType, nonPublic: true);
                        ((NetDataReader)eventType.GetField("DataReader").GetValue(evt)).SetSource(expected);
                        enqueue.Invoke(queue, new[] { evt });
                    }
                }
                receive(datagram, datagram.Length, 0, peer.EndPoint);
                lock (queue)
                {
                    Check(queue.Count == 32, "Client byte watermark did not stop ingress below event limit");
                    queue.GetType().GetMethod("Clear").Invoke(queue, null);
                }
                for (int i = 0; i < 600; i++) receive(datagram, datagram.Length, 0, peer.EndPoint);
                lock (queue) Check(queue.Count == 512, "Client queue event watermark exceeded");
                resume.Set();
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref small) == 512, 5000), "Client queue did not drain");
                receive(datagram, datagram.Length, 0, peer.EndPoint);
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref small) == 513, 5000), "Client ingress did not recover");
                client.Disconnect();
                Check(!client.Connected, "Client disconnect failed");
                client.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
                Check(SpinWait.SpinUntil(() => { host.PollEvents(); return client.Connected; }, 5000), "Guard did not permit reconnect");
                lock (queue) queue.GetType().GetMethod("Enqueue").Invoke(queue, new object[] { null });
                Check(SpinWait.SpinUntil(() => !client.Connected, 5000), "Client poll fault did not close its transport");
                client.Disconnect();
                client.Connect(new IPEndPoint(IPAddress.Loopback, host.LocalPort));
                Check(SpinWait.SpinUntil(() => { host.PollEvents(); return client.Connected; }, 5000), "Client could not recover after poll fault");
            }
            finally { resume.Set(); client.Disconnect(); host.Stop(); }
        }
        Console.WriteLine("CLIENT RECEIVE BUDGET passed; actual backend/1 MiB/oversized+merged/group+event+byte budgets/drain/poll fault/reconnect");
    }
}
