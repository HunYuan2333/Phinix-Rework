using System;
using System.Reflection;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void HostReferenceRegression()
    {
        Func<string,ManagedAssemblyIdentity> identity = value => ManagedAssemblyIdentity.FromAssemblyName(new AssemblyName(value));
        var required=identity("0Harmony, Version=2.3.6.0, Culture=neutral, PublicKeyToken=null");
        var newer=identity("0Harmony, Version=2.4.1.0, Culture=neutral, PublicKeyToken=null");
        Assert(newer.CanProvideHostReference(required),"Harmony 2.3.6 to 2.4.1 host upgrade allowed");
        Assert(required.CanProvideHostReference(required),"Exact host version allowed");
        foreach(string denied in new[]{
            "0Harmony, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null",
            "0Harmony, Version=2.3.5.0, Culture=neutral, PublicKeyToken=null",
            "0Harmony, Version=2.3.6.0, Culture=en-US, PublicKeyToken=null",
            "0Harmony, Version=2.4.1.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "OtherLibrary, Version=2.4.1.0, Culture=neutral, PublicKeyToken=null"})
            Assert(!identity(denied).CanProvideHostReference(required),"Cross-major/downgrade/culture/token/name differences rejected");
        Assert(ReferenceEquals(ManagedAssemblyIdentity.SelectHostReference(required,new[]{newer}),newer),"One compatible host selected");
        Assert(ManagedAssemblyIdentity.SelectHostReference(required,new ManagedAssemblyIdentity[0])==null,"Missing host rejected");
        Assert(ManagedAssemblyIdentity.SelectHostReference(required,new[]{newer,identity("0Harmony, Version=2.5.0.0, Culture=neutral, PublicKeyToken=null")})==null,"Ambiguous compatible hosts rejected");
        Assert(ReferenceEquals(ManagedAssemblyIdentity.SelectHostReference(required,new[]{required,newer}),required),"Exact host takes precedence over upgrade");
        Assert(ManagedAssemblyIdentity.SelectHostReference(required,new[]{required,required})==null,"Duplicate exact host facts rejected");
        var composedClient=identity("ClientExtensionAbstractions, Version=1.9.0.0, Culture=neutral, PublicKeyToken=null");
        var previousClient=identity("ClientExtensionAbstractions, Version=1.8.0.0, Culture=neutral, PublicKeyToken=null");
        Assert(ReferenceEquals(ManagedAssemblyIdentity.SelectHostReference(previousClient,new[]{composedClient}),composedClient),"Existing 1.8 plugins can bind to the additive 1.9 client host.");
        Assert(ManagedAssemblyIdentity.SelectHostReference(composedClient,new[]{previousClient})==null,"New Compose authors cannot bind to a host lacking 1.9 contracts.");
        var oldClient=identity("ClientExtensionAbstractions, Version=1.7.0.0, Culture=neutral, PublicKeyToken=null");
        var newClient=identity("ClientExtensionAbstractions, Version=1.8.0.0, Culture=neutral, PublicKeyToken=null");
        Assert(ReferenceEquals(ManagedAssemblyIdentity.SelectHostReference(oldClient,new[]{newClient}),newClient),
            "Existing localization plugins compiled against 1.7 can use the additive 1.8 host API.");
    }
}
