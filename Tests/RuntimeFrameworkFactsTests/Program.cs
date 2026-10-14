using System;
using System.IO;
using System.Linq;
using PhinixClient.Framework;

class Program
{
    static int Main()
    {
        var before=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToArray();
        var framework=ClientRuntimeAssemblyFacts.FrameworkReferences().ToArray();
        var available=ClientRuntimeAssemblyFacts.AvailableReferences().ToArray();
        foreach(string name in new[]{"System.Runtime.Serialization.Xml","System.Net.WebHeaderCollection"})
            if(!framework.Any(a=>a.Name==name && a.Version.ToString()=="10.0.0.0"))throw new Exception("Missing log dependency: "+name);
        if(!framework.Any(a=>!before.Contains(a.Name)))throw new Exception("No lazy assembly in fixture");
        var after=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToArray();
        if(new[]{"System.Runtime.Serialization.Xml","System.Net.WebHeaderCollection"}.Any(name=>!before.Contains(name) && after.Contains(name)))throw new Exception("Metadata inspection loaded a plugin framework dependency");
        if(available.Select(a=>a.FullName).Distinct().Count()!=available.Length)throw new Exception("Duplicate identity facts");
        if(!framework.All(a=>available.Any(b=>a.FullName==b.FullName)))throw new Exception("Framework omitted from available facts");
        object saved=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        try
        {
            AppContext.SetData("TRUSTED_PLATFORM_ASSEMBLIES",Path.Combine(Path.GetTempPath(),"System.Net.WebHeaderCollection.dll"));
            if(ClientRuntimeAssemblyFacts.FrameworkReferences().Any())throw new Exception("Non-runtime folder trusted");
            AppContext.SetData("TRUSTED_PLATFORM_ASSEMBLIES",null);
            if(ClientRuntimeAssemblyFacts.FrameworkReferences().Any())throw new Exception("Missing TPA must retain loaded-only behavior");
        }
        finally { AppContext.SetData("TRUSTED_PLATFORM_ASSEMBLIES",saved); }
        Console.WriteLine("Runtime framework facts passed: two real log dependencies, lazy availability, no execution, deduplication, non-runtime rejection, absent-TPA fallback.");
        return 0;
    }
}
