using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using Utils.Framework;

internal static partial class Program
{
    private static void CheckTradeRequestBoundary()
    {
        var abstractions = typeof(IClientApiDirectory).Assembly;
        Assert(typeof(ITradeRequestApi).Assembly == typeof(FrameworkTradeProtocol).Assembly,
            "The request capability is defined by Trade Contracts.");
        Assert(abstractions.GetType("PhinixClient.Framework.ITradeRequestApi") == null,
            "Host abstractions no longer define a business request contract.");
        Assert(!abstractions.GetReferencedAssemblies().Any(a => a.Name == "TradeExtension"),
            "Moving the contract must not introduce a Host-to-Trade dependency.");

        var host = Assembly.Load("PhinixClient");
        Assert(!host.GetReferencedAssemblies().Any(a => a.Name == "TradeExtension" || a.Name == "TradeExtension.Client"),
            "The concrete Host remains independent of Trade.");
        var chat = Assembly.Load("ChatExtension.Client");
        Assert(chat.GetReferencedAssemblies().Any(a => a.Name == "TradeExtension") &&
            !chat.GetReferencedAssemblies().Any(a => a.Name == "TradeExtension.Client"),
            "Chat references the sibling contract, not the Trade implementation.");

        // Exercise the actual Host directory and Chat lookup without GUI/game assets.
        var registry = new ExtensionApiRegistry();
        var result = new ExtensionDiscoveryResult { ExtensionId = "builtin.trade", State = ExtensionModuleState.Registered };
        Func<IEnumerable<ExtensionDiscoveryResult>> results = () => new[] { result };
        var directoryType = host.GetType("PhinixClient.Framework.ClientApiDirectory", true);
        var directory = (IClientApiDirectory)Activator.CreateInstance(directoryType,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { registry, results, new[] { typeof(BuiltInTradeClientExtension) }, (Func<bool>)(() => true) }, null);
        var sidebarType = chat.GetType("Phinix.ChatExtension.Client.ChatSidebarProvider", true);
        var sidebar = sidebarType.GetConstructors().Single().Invoke(new object[] { null, null, null, null, null, directory, null });
        var lookup = sidebarType.GetMethod("FindTrade", BindingFlags.Instance | BindingFlags.NonPublic);
        Func<IClientApiProvider<ITradeRequestApi>> find = () => (IClientApiProvider<ITradeRequestApi>)lookup.Invoke(sidebar, null);
        try
        {
            Assert(find() == null, "Chat remains usable when no Trade provider is registered.");
            var first = new RequestProbe();
            registry.RegisterApi<ITradeRequestApi>(result.ExtensionId, first);
            Assert(find() == null, "A composed but inactive Trade provider is not available to Chat.");
            result.State = ExtensionModuleState.Active;
            var borrowed = find();
            ITradeRequestApi api;
            Assert(borrowed != null && borrowed.TryGet(out api) && ReferenceEquals(api, first),
                "Activation makes the ordinary Trade contract available.");
            result.State = ExtensionModuleState.Failed;
            Assert(!borrowed.TryGet(out api) && find() == null, "Failed Trade invalidates borrowed handles and Chat lookup.");
            result.State = ExtensionModuleState.Active;
            registry.UnregisterOwner(result.ExtensionId);
            Assert(!borrowed.TryGet(out api) && find() == null, "Unregistering Trade revokes the exact borrowed registration.");
            var replacement = new RequestProbe();
            registry.RegisterApi<ITradeRequestApi>(result.ExtensionId, replacement);
            var current = find();
            Assert(current != null && current.RegistrationId != borrowed.RegistrationId && !borrowed.TryGet(out api),
                "A replacement provider cannot revive a stale handle.");
            Assert(current.TryGet(out api) && ReferenceEquals(api, replacement), "Chat resolves the replacement through the same directory.");
            api.CreateTrade("remote");
            Assert(first.Calls == 0 && replacement.Calls == 1, "The resolved capability dispatches only to its current owner.");
            ((IDisposable)directory).Dispose();
            Assert(!current.TryGet(out api), "Host directory shutdown invalidates borrowed capabilities.");
        }
        finally
        {
            ((IDisposable)directory).Dispose();
            registry.UnregisterOwner(result.ExtensionId);
        }
    }

    private sealed class RequestProbe : ITradeRequestApi
    {
        internal int Calls;
        public void CreateTrade(string uuid) { Calls++; }
    }
}
