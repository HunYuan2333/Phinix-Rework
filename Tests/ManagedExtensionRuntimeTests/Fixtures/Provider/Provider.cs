using Utils.Framework;
namespace Fixture.Managed
{
    [PhinixExtension("test.provider")]
    public sealed class Provider : IPhinixExtensionModule
    {
        public string ExtensionId => "test.provider";
        public void Register(IExtensionBuilder builder) { }
    }
}
