using System;
using System.IO;
using Utils.Framework;
namespace Fixture.Managed
{
    public abstract class ModuleBase : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        public abstract string ExtensionId { get; }
        public static int Activations, Shutdowns;
        public void Activate(ExtensionHostContext context) { Activations++; }
        public void Shutdown(ExtensionHostContext context) { Shutdowns++; }
        public void Register(IExtensionBuilder builder) { if (Helper.Value == null) throw new InvalidOperationException(); }
    }
    [PhinixExtension("test.managed", DependsOn = new[] { "builtin.host" })]
    public sealed class Module : ModuleBase
    {
        static Module()
        {
            string sentinel = Environment.GetEnvironmentVariable("PHINIX_MANAGED_METADATA_SENTINEL");
            if (sentinel != null) File.WriteAllText(sentinel, "executed");
        }
        public override string ExtensionId => "test.managed";
    }
    public class Outer
    {
        [PhinixExtension("test.nested")]
        public sealed class Nested : ModuleBase { public override string ExtensionId => "test.nested"; }
    }
}
