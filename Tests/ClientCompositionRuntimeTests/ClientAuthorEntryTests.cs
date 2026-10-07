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
            Assert(ClientAuthorFixture.Compositions==1 && LegacyAuthorFixture.Registrations==1,
                "One ordinary registry invokes new Compose and legacy Register exactly once.");
            runtime.Stop(); runtime.Stop();
            Assert(ClientAuthorFixture.Stops==1 && LegacyAuthorFixture.Stops==1,"Both entries stop through the same idempotent lifecycle.");
        }
        var unprepared=new ExtensionHostContext(); unprepared.AddService<IExtensionDiscoveryPolicy>(new AuthorDiscovery(true));
        var rejected=new ClientExtensionRuntime(unprepared,()=>true);
        rejected.Start(); rejected.Stop();
        Assert(ClientAuthorFixture.Compositions==1,"The new bridge rejects an unprepared host before author composition runs.");
        var server=new ExtensionHostContext {HostKind="server-test"}; server.AddService<IExtensionDiscoveryPolicy>(new LegacyAuthorDiscovery());
        var legacy=new ClientExtensionRuntime(server,()=>true); legacy.Start(); legacy.Stop();
        Assert(LegacyAuthorFixture.Registrations==2,"A legacy/server entry needs no client factory or client registration changes.");
    }
    private sealed class AuthorDiscovery : IExtensionDiscoveryPolicy
    {
        private readonly bool onlyNew;
        internal AuthorDiscovery(bool onlyNew) {this.onlyNew=onlyNew;}
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==typeof(ClientAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type)=>type==typeof(ClientAuthorFixture) || !onlyNew && type==typeof(LegacyAuthorFixture);
    }
    private sealed class LegacyAuthorDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==typeof(LegacyAuthorFixture).Assembly;
        public bool ShouldDiscoverType(Type type)=>type==typeof(LegacyAuthorFixture);
    }
}
