using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void ProbeClientAuthorEntry()
    {
        var metadata = ManagedExtensionMetadataReader.Read(File.ReadAllBytes(typeof(ClientAuthorFixture).Assembly.Location));
        Assert(metadata.Modules.Any(m => m.Id == "author.new"), "Static inspection recognizes the current composition entry.");
        Assert(ClientAuthorFixture.Compositions == 0, "Inspection never executes author code.");
        byte[] foreign=File.ReadAllBytes(typeof(ClientAuthorFixture).Assembly.Location);
        byte[] name=System.Text.Encoding.UTF8.GetBytes("ClientExtensionAbstractions\0");
        int found=-1;
        for(int i=0;i<=foreign.Length-name.Length;i++)
            if(name.Select((value,j)=>foreign[i+j]==value).All(equal=>equal)) {found=i;break;}
        Assert(found>=0,"The fixture refers to the real client contract identity.");
        foreign[found]=(byte)'X';
        bool foreignRejected=false;
        try { ManagedExtensionMetadataReader.Read(foreign); }
        catch(ManagedExtensionValidationException error) {foreignRejected=error.Code=="ModuleEntryInvalid";}
        Assert(foreignRejected,"A similarly named base from a foreign assembly is rejected as an invalid module entry.");

        LegacyAuthorFixture.Registrations = 0; LegacyAuthorFixture.Stops = 0;
        var host = new ExtensionHostContext(); var policy = new AuthorDiscovery(); host.AddService<IExtensionDiscoveryPolicy>(policy);
        using (var factory = new ClientCompositionFactory(() => true, _ => { }))
        {
            host.AddService<IClientCompositionFactory>(factory);
            var runtime = new ClientExtensionRuntime(host, () => true);
            runtime.Start(); runtime.Start();
            Assert(ClientAuthorFixture.Compositions == 1 && LegacyAuthorFixture.Registrations == 0,
                "Only current client entries compose; unsupported Register is rejected before construction.");
            Assert(runtime.Extensions.Warnings.Count(w => w.Contains("ClientEntryUnsupported")) == 1,
                "Unsupported client entry has one diagnostic.");
            Assert(host.GetRequiredService<IExtensionDiscoveryPolicy>() == policy, "Client discovery restores the borrowed host policy.");
            runtime.Stop(); runtime.Stop();
            Assert(ClientAuthorFixture.Stops == 1 && LegacyAuthorFixture.Stops == 0, "Only accepted entries participate in client shutdown.");
        }
        var unprepared = new ExtensionHostContext(); unprepared.AddService<IExtensionDiscoveryPolicy>(new AuthorDiscovery());
        var rejected = new ClientExtensionRuntime(unprepared, () => true); rejected.Start(); rejected.Stop();
        Assert(ClientAuthorFixture.Compositions == 1, "Unprepared host fails before composition runs.");
        var server = new ExtensionHostContext { HostKind = "server-test" }; server.AddService<IExtensionDiscoveryPolicy>(new ServerDiscovery());
        var modules = PhinixExtensionRegistry.DiscoverExtensions(server);
        PhinixExtensionRegistry.ActivateExtensions(modules, server); PhinixExtensionRegistry.ShutdownExtensions(modules, server);
        Assert(LegacyAuthorFixture.Registrations == 1 && LegacyAuthorFixture.Stops == 1, "Shared server Register remains available.");
        Assert(typeof(ClientExtensionModule).Assembly.GetType("PhinixClient.Framework.LegacyClientExtensionModule") == null,
            "Obsolete client author facade is deleted.");
    }
    private sealed class AuthorDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(ClientAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type) => type == typeof(ClientAuthorFixture) || type == typeof(LegacyAuthorFixture);
    }
    private sealed class ServerDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(LegacyAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type) => type == typeof(LegacyAuthorFixture);
    }
}
