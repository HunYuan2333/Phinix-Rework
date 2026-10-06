using Utils.Framework;
namespace Fixture.Plugin
{
    [PhinixExtension("test.managed.module")]
    public sealed class Module : IPhinixExtensionModule
    {
        public string ExtensionId => "test.managed.module";
        public void Register(IExtensionBuilder builder) { }
    }
}
