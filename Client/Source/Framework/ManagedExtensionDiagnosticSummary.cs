using System.Linq;
using System.Text;
using System.Collections.Generic;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    internal static class ManagedExtensionDiagnosticSummary
    {
        // Export only package/host/module facts, never logs, settings, paths or session/player data.
        internal static string Build(ManagedExtensionManagementPackage model,ClientExtensionControlSnapshot controls,
            IEnumerable<ExtensionDiscoveryResult> discovery,string hostVersion,string gameVersion)
        {
            var p=model.Package; var b=new StringBuilder();
            b.AppendLine("Package: "+p.PackageId); b.AppendLine("Version: "+p.Version);
            b.AppendLine("Source: "+(p.IsLocalDevelopment?"local-development":p.SourceId));
            b.AppendLine("Installed ZIP SHA-256: "+p.ArtifactSha256);
            b.AppendLine("Installed manifest SHA-256: "+p.ManifestSha256);
            b.AppendLine("Current ZIP SHA-256: "+(model.Current?.Package.ArtifactSha256??"not loaded"));
            b.AppendLine("Current version: "+(model.Current?.Package.Version??"not loaded"));
            b.AppendLine("Next state: "+p.DesiredState); b.AppendLine("Restart pending: "+(model.RestartPending || model.ModulesRestartPending));
            b.AppendLine("RimWorld: "+gameVersion+"; Phinix: "+hostVersion+"; abstractions: "+ClientAbstractionsCompatibility.Version);
            b.AppendLine("Content: "+p.ContentState+"; failure: "+(p.DiagnosticCode??model.Current?.DiagnosticCode??"none"));
            b.AppendLine("Enable: "+(model.EnableBlockCode??"allowed")+"; disable: "+(model.DisableBlockCode??"allowed")+"; removal: "+(model.RemovalBlockCode??"allowed"));
            foreach(var module in p.Manifest?.Modules??Enumerable.Empty<ManagedExtensionModule>())
            {
                var result=discovery?.FirstOrDefault(d=>d.ExtensionId==module.Id);
                b.AppendLine("Module: "+module.Id+"; discovery: "+(result?.State.ToString()??"not discovered")+
                    "; active: "+(controls?.ActiveModuleIds.Contains(module.Id)==true)+"; next disabled: "+(controls?.EffectiveDisabledModuleIds.Contains(module.Id)==true));
                b.AppendLine("Module dependencies: "+string.Join(", ",module.DependsOn));
            }
            foreach(var dependency in p.Manifest?.Dependencies??Enumerable.Empty<ManagedExtensionDependency>())
                b.AppendLine("Package dependency: "+dependency.PackageId+" "+dependency.VersionRange.Text+"; optional: "+dependency.Optional);
            return b.ToString();
        }
    }
}
