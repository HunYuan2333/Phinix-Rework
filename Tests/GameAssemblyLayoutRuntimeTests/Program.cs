using System;using System.IO;using System.Linq;using PhinixClient.Framework;
internal static class Program
{
    private static int checks;
    private static void Check(bool value){checks++;if(!value)throw new Exception("Layout assertion failed: "+checks);}
    private static void Main(string[] args)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-layout-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            Check(!ClientGameAssemblyLayout.GetProbeDirectories(null,1,7).Any());
            var common=ClientGameAssemblyLayout.GetProbeDirectories(root,1,6).ToArray();
            Check(common.SequenceEqual(new[]{Path.Combine(root,"Common","Assemblies"),Path.Combine(root,"Common","Extensions")}));
            bool refused=false;try{ClientGameAssemblyLayout.GetProbeDirectories(root,1,7).ToArray();}catch(InvalidOperationException){refused=true;}Check(refused);
            foreach(string version in new[]{"1.6","1.7"})
            {
                string sdk=Path.Combine(root,version,"Assemblies");Directory.CreateDirectory(sdk);File.WriteAllText(Path.Combine(sdk,"10-ClientExtensionAbstractions.dll"),"fixture marker, never loaded");
                string ext=Path.Combine(root,version,"Extensions");Directory.CreateDirectory(Path.Combine(ext,"plugin"));File.WriteAllText(Path.Combine(ext,"plugin","example.dll"),"fixture");
                Directory.CreateDirectory(Path.Combine(ext,"Resources"));File.WriteAllText(Path.Combine(ext,"Resources","ignore.dll"),"fixture");
            }
            foreach(int minor in new[]{6,7})
            {
                var paths=ClientGameAssemblyLayout.GetProbeDirectories(root,1,minor).ToArray();string version=Path.Combine(root,"1."+minor);
                Check(paths.Length==3 && paths.All(p=>p.StartsWith(version+Path.DirectorySeparatorChar,StringComparison.Ordinal)));
                Check(!paths.Any(p=>p.Contains("Resources")||p.Contains("Common")));
            }
            bool invalid=false;try{ClientGameAssemblyLayout.GetProbeDirectories("relative",1,7).ToArray();}catch(ArgumentException){invalid=true;}Check(invalid);
            bool unsupported=false;try{ClientGameAssemblyLayout.GetProbeDirectories(root,2,0).ToArray();}catch(NotSupportedException){unsupported=true;}Check(unsupported);
            if(args.Length==1)
            {
                string package=Path.GetFullPath(args[0]);
                foreach(int minor in new[]{6,7})
                {
                    var paths=ClientGameAssemblyLayout.GetProbeDirectories(package,1,minor).ToArray();
                    Check(paths.Length==2 && paths.All(Directory.Exists));
                    var files=paths.SelectMany(p=>Directory.GetFiles(p,"*.dll")).ToArray();
                    Check(files.All(p=>p.StartsWith(Path.Combine(package,"1."+minor)+Path.DirectorySeparatorChar,StringComparison.Ordinal)));
                    var names=files.Select(p=>System.Reflection.AssemblyName.GetAssemblyName(p).Name).ToArray();
                    Check(names.Length==names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                    Check(names.Contains("PhinixClient") && names.Contains("ClientExtensionAbstractions") && names.Contains("ClientPresentation.Contracts") && names.Contains("InventoryExtension.GameAdapter"));
                }
            }
            Console.WriteLine("Game assembly layout: "+checks+" checks passed; no DLL or game execution.");
        }
        finally{Directory.Delete(root,true);}
    }
}
