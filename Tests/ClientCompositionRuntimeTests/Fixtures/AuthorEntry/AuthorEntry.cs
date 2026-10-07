using PhinixClient.Framework;
using Utils.Framework;

[PhinixExtension("author.new")]
public sealed class ClientAuthorFixture : ClientExtensionModule, IActivatablePhinixExtensionModule
{
    public static int Compositions,Stops;
    public override string ExtensionId=>"author.new";
    public override void Compose(IExtensionBuilder builder) {Compositions++;}
    public void Activate(ExtensionHostContext host) {}
    public void Shutdown(ExtensionHostContext host) {Stops++;}
}
[PhinixExtension("author.legacy")]
public sealed class LegacyAuthorFixture : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public static int Registrations,Stops;
    public string ExtensionId=>"author.legacy";
    public void Register(IExtensionBuilder builder) {Registrations++;}
    public void Activate(ExtensionHostContext host) {}
    public void Shutdown(ExtensionHostContext host) {Stops++;}
}
