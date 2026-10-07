namespace PhinixClient.Framework
{
    public enum ClientLinkOpenPreference
    {
        PreferGameBrowser,
        ExternalBrowser
    }

    public enum ClientLinkOpenResult
    {
        GameBrowserRequested,
        ExternalBrowserRequested,
        Unavailable
    }

    /// <summary>Opens HTTP(S) links on the main thread. Results describe a request, not page loading.</summary>
    public interface IClientLinkService
    {
        ClientLinkOpenResult Open(string url, ClientLinkOpenPreference preference = ClientLinkOpenPreference.PreferGameBrowser);
    }
}
