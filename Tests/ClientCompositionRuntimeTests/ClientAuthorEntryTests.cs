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
        var metadata=ManagedExtensionMetadataReader.Read(File.ReadAllBytes(typeof(ClientAuthorFixture).Assembly.Location));
        Assert(metadata.Modules.Any(m=>m.Id=="author.new" && m.EntryType==typeof(ClientAuthorFixture).FullName),
            "Static managed inspection recognizes the trusted client composition base without constructing it.");
        Assert(metadata.Modules.Any(m => m.Id == "author.legacy-adapter") && LegacyAdapterAuthorFixture.Constructions == 0,
            "Static inspection recognizes the deprecated trusted adapter without constructing it.");
        Assert(ClientAuthorFixture.Compositions==0,"Metadata inspection never executes Compose.");
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

        LegacyAuthorFixture.Registrations=0; LegacyAuthorFixture.Stops=0;
        var host=new ExtensionHostContext(); host.AddService<IExtensionDiscoveryPolicy>(new AuthorDiscovery(false));
        using(var factory=new ClientCompositionFactory(()=>true,_=>{}))
        {
            host.AddService<IClientCompositionFactory>(factory);
            var runtime=new ClientExtensionRuntime(host,()=>true);
            runtime.Start(); runtime.Start();
            Assert(runtime.Extensions.Warnings.Count(w => w.Contains("ClientRegistrationDeprecated")) == 1 &&
                runtime.Extensions.Warnings.Any(w => w.Contains("author.legacy")),
                "Repeated client Start produces one warning for only the enabled legacy entry.");
            Assert(ClientAuthorFixture.Compositions==1 && LegacyAuthorFixture.Registrations==1,
                "One ordinary registry invokes new Compose and legacy Register exactly once.");
            runtime.Stop(); runtime.Stop();
            Assert(ClientAuthorFixture.Stops==1 && LegacyAuthorFixture.Stops==1,"Both entries stop through the same idempotent lifecycle.");
        }
        var disabledHost = new ExtensionHostContext();
        disabledHost.AddService<IExtensionDiscoveryPolicy>(new LegacyAdapterAuthorDiscovery());
        disabledHost.AddService<IExtensionActivationPolicy>(new DisabledLegacyAuthor());
        var disabledRuntime = new ClientExtensionRuntime(disabledHost, () => true);
        disabledRuntime.Start();
        Assert(LegacyAdapterAuthorFixture.Constructions == 0 && LegacyAdapterAuthorFixture.Registrations == 0 &&
            !disabledRuntime.Extensions.Warnings.Any(w => w.Contains("ClientRegistrationDeprecated")),
            "Disabled legacy adapter is never constructed or diagnosed as an enabled entry.");
        disabledRuntime.Stop();
        var adapterHost = new ExtensionHostContext();
        adapterHost.AddService<IExtensionDiscoveryPolicy>(new LegacyAdapterAuthorDiscovery());
        var adapterRuntime = new ClientExtensionRuntime(adapterHost, () => true);
        adapterRuntime.Start(); adapterRuntime.Start();
        Assert(LegacyAdapterAuthorFixture.Registrations == 1 && adapterRuntime.Extensions.Warnings.Count(w =>
            w.Contains("ClientRegistrationDeprecated")) == 1, "Deprecated source adapter shares the ordinary registry and one warning.");
        adapterRuntime.Stop();
        var unprepared=new ExtensionHostContext(); unprepared.AddService<IExtensionDiscoveryPolicy>(new AuthorDiscovery(true));
        var rejected=new ClientExtensionRuntime(unprepared,()=>true);
        rejected.Start(); rejected.Stop();
        Assert(ClientAuthorFixture.Compositions==1,"The new bridge rejects an unprepared host before author composition runs.");
        var server=new ExtensionHostContext {HostKind="server-test"}; server.AddService<IExtensionDiscoveryPolicy>(new LegacyAuthorDiscovery());
        var legacy = PhinixExtensionRegistry.DiscoverExtensions(server);
        PhinixExtensionRegistry.ActivateExtensions(legacy, server);
        Assert(!legacy.Warnings.Any(w => w.Contains("ClientRegistrationDeprecated")), "Shared server registration has no client deprecation warning.");
        PhinixExtensionRegistry.ShutdownExtensions(legacy, server);
#pragma warning disable 618
        Assert(typeof(LegacyClientExtensionModule).IsDefined(typeof(ObsoleteAttribute), false) &&
            typeof(LegacyClientExtensionModule).GetMethod("Register").IsDefined(typeof(ObsoleteAttribute), false),
            "Distinct client compatibility adapter and author entry carry replacement guidance.");
#pragma warning restore 618
        Assert(!typeof(IPhinixExtensionModule).GetMethod("Register").IsDefined(typeof(ObsoleteAttribute), false),
            "Shared registration is not deprecated for server authors.");
        Assert(LegacyAuthorFixture.Registrations==2,"A legacy/server entry needs no client factory or client registration changes.");
    }
    private sealed class AuthorDiscovery : IExtensionDiscoveryPolicy
    {
        private readonly bool onlyNew;
        internal AuthorDiscovery(bool onlyNew) {this.onlyNew=onlyNew;}
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==typeof(ClientAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type)=>type==typeof(ClientAuthorFixture) || !onlyNew && type==typeof(LegacyAuthorFixture);
    }
    private sealed class LegacyAdapterAuthorDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(LegacyAdapterAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type) => type == typeof(LegacyAdapterAuthorFixture);
    }
    private sealed class DisabledLegacyAuthor : IExtensionActivationPolicy
    {
        public System.Collections.Generic.IReadOnlyCollection<string> DisabledExtensions => new[] { "author.legacy-adapter" };
        public bool ShouldActivate(string id, out string reason) { reason = "test disabled"; return false; }
    }
    private sealed class LegacyAuthorDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==typeof(LegacyAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type)=>type==typeof(LegacyAuthorFixture);
    }
}
