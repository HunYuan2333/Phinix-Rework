using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

internal static class Program
{
    // A fresh process is essential: preloading every dependency would hide the
    // initial Verse LoadFrom -> immediate GetTypes failure seen in both games.
    private static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]),game=Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>
        {
            string name=new AssemblyName(e.Name).Name;
            string path=Path.Combine(game,name+".dll");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        try
        {
            bool sdkUsable=false;
            foreach(string file in Directory.GetFiles(Path.Combine(root,"Assemblies"),"*.dll").OrderBy(Path.GetFileName,StringComparer.Ordinal))
            {
                var assembly=Assembly.LoadFrom(file);
                if(assembly.GetName().Name!="ClientExtensionAbstractions")continue;
                var types=assembly.GetTypes();
                sdkUsable=types.Any(t=>t.FullName=="PhinixClient.UnknownItem");
                Console.WriteLine("SDK immediate GetTypes: "+types.Length+" types; UnknownItem="+sdkUsable);
            }
            if(!sdkUsable)throw new Exception("SDK/UnknownItem did not enter the usable type set.");
            var contracts=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="ClientPresentation.Contracts");
            if(contracts.GetType("PhinixClient.Framework.IClientPresentationImage",true)==null ||
               contracts.GetType("PhinixClient.Framework.IClientPresentationImageLease",true)==null)throw new Exception("Image contracts unavailable.");
            var store=Assembly.LoadFrom(Path.Combine(root,"Extensions","17-PluginStore.Client.dll"));
            using(var resource=store.GetManifestResourceStream("Phinix.PluginStore.Assets.maintainers.json"))
            using(var source=File.OpenRead(args[2]))
            using(var hash=SHA256.Create())
            {
                if(resource==null || !hash.ComputeHash(resource).SequenceEqual(hash.ComputeHash(source)))
                    throw new Exception("Embedded maintainer resource missing or changed.");
            }
            Console.WriteLine("PASS ordered SDK type loading, image contracts and embedded maintainer resource; no plugin activation or game execution.");
            return 0;
        }
        catch(ReflectionTypeLoadException e)
        { foreach(Exception inner in e.LoaderExceptions)Console.WriteLine(inner.GetType().Name+": "+inner.Message);return 1; }
        catch(Exception e) { Console.WriteLine(e);return 1; }
    }
}
