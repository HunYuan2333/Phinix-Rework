using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PhinixClient.Framework
{
    internal static class ClientRuntimeAssemblyFacts
    {
        // CoreCLR resolves framework assemblies lazily. Availability is broader
        // than GetAssemblies(), but is limited to the runtime's trusted list,
        // its own directory, and framework names. Never scan mod/plugin folders.
        internal static IEnumerable<AssemblyName> FrameworkReferences()
        {
            string trusted=AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if(string.IsNullOrEmpty(trusted))yield break; // Mono keeps existing loaded facts.
            string runtime=Path.GetDirectoryName(typeof(object).Assembly.Location);
            foreach(string path in trusted.Split(Path.PathSeparator).Distinct(StringComparer.Ordinal))
            {
                if(!string.Equals(Path.GetDirectoryName(path),runtime,StringComparison.Ordinal))continue;
                string name=Path.GetFileNameWithoutExtension(path);
                if(!name.StartsWith("System.",StringComparison.Ordinal) && name!="System" && name!="netstandard" && name!="mscorlib" && name!="Microsoft.CSharp")continue;
                // Identity-only inspection does not execute or load this DLL.
                var identity=AssemblyName.GetAssemblyName(path);
                if(identity.Name!=name)throw new InvalidDataException("Runtime framework identity mismatch: "+name);
                yield return identity;
            }
        }
        internal static Assembly ResolveFrameworkReference(AssemblyName required)
        {
            var trusted=FrameworkReferences().SingleOrDefault(a=>a.FullName==required.FullName);
            if(trusted==null)return null;
            var assembly=Assembly.Load(trusted);
            if(assembly.GetName().FullName!=trusted.FullName ||
                !string.Equals(Path.GetDirectoryName(assembly.Location),Path.GetDirectoryName(typeof(object).Assembly.Location),StringComparison.Ordinal))
                throw new InvalidDataException("Runtime framework binding mismatch: "+required.FullName);
            return assembly;
        }
        internal static IEnumerable<AssemblyName> AvailableReferences()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>a.GetName())
                .Concat(FrameworkReferences()).GroupBy(a=>a.FullName,StringComparer.Ordinal).Select(g=>g.First());
        }
    }
}
