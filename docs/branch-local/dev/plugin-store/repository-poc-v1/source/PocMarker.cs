using System.Reflection;
using System.Runtime.Versioning;
[assembly: AssemblyTitle("Phinix Store PoC Marker")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.7.2")]
// No initializer, game hook, networking, storage or install behavior.
// This inert assembly exists only to verify release-byte distribution.
namespace Phinix.Store.Poc
{
    public static class PocMarker
    {
        public const string Version = "1.0.0";
    }
}
