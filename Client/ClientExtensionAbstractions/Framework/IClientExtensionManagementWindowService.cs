namespace PhinixClient.Framework
{
    /// <summary>Opens host-owned extension controls on the main thread, independently of business plugins.</summary>
    public interface IClientExtensionManagementWindowService
    {
        void OpenExtensionManagerWindow();
    }
}
