using System.Collections.Generic;
using Utils.Framework.Themes;

namespace PhinixClient.Framework
{
    // Main-thread host service; borrowed by Store. Package facts carry no historical schema branches.
    public interface IClientThemePackageService
    {
        IReadOnlyList<InstalledThemePackage> Packages { get; }
        IReadOnlyList<string> Diagnostics { get; }
        void Refresh();
        ThemePackage Inspect(string absolutePath);
        void Install(ThemePackage package,string expectedInstalledArtifact);
        void Uninstall(string packageId,string expectedInstalledArtifact);
    }
    public sealed class ClientThemeExportInfo
    {
        public ClientThemeExportInfo(string packageId,string name,string author,string version)
        { PackageId=packageId; Name=name; Author=author; Version=version; }
        public string PackageId { get; }
        public string Name { get; }
        public string Author { get; }
        public string Version { get; }
    }
    public interface IClientThemePackageExporter
    {
        void ExportPackage(ClientThemeDraft draft,ClientThemeExportInfo info,string absolutePath);
    }
}
