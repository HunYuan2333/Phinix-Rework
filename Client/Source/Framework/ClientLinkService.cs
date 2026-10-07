using System;

namespace PhinixClient.Framework
{
    internal sealed class ClientLinkService : IClientLinkService
    {
        private readonly Func<bool> isMainThread;
        private readonly Func<bool> gameBrowserAvailable;
        private readonly Action<string> openGameBrowser;
        private readonly Action<string> openExternalBrowser;
        private readonly Action<string> diagnostic;

        internal ClientLinkService(Func<bool> isMainThread, Func<bool> gameBrowserAvailable,
            Action<string> openGameBrowser, Action<string> openExternalBrowser, Action<string> diagnostic)
        {
            this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
            this.gameBrowserAvailable = gameBrowserAvailable ?? throw new ArgumentNullException(nameof(gameBrowserAvailable));
            this.openGameBrowser = openGameBrowser ?? throw new ArgumentNullException(nameof(openGameBrowser));
            this.openExternalBrowser = openExternalBrowser ?? throw new ArgumentNullException(nameof(openExternalBrowser));
            this.diagnostic = diagnostic;
        }

        public ClientLinkOpenResult Open(string url, ClientLinkOpenPreference preference = ClientLinkOpenPreference.PreferGameBrowser)
        {
            if (!isMainThread()) throw new InvalidOperationException("Open links on the main thread through the client dispatcher.");
            Uri uri;
            if (string.IsNullOrEmpty(url) || url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                uri.Host.Length == 0 || uri.UserInfo.Length != 0 || url.IndexOf('\r') >= 0 || url.IndexOf('\n') >= 0)
                throw new ArgumentException("A credential-free absolute HTTP(S) URL is required.", nameof(url));
            if (preference != ClientLinkOpenPreference.PreferGameBrowser && preference != ClientLinkOpenPreference.ExternalBrowser)
                throw new ArgumentOutOfRangeException(nameof(preference));
            if (preference == ClientLinkOpenPreference.PreferGameBrowser)
            {
                try
                {
                    if (gameBrowserAvailable())
                    {
                        openGameBrowser(url);
                        Report("GameBrowserRequested", uri.Host);
                        return ClientLinkOpenResult.GameBrowserRequested;
                    }
                }
                catch (Exception ex) { Report("GameBrowserUnavailable:" + ex.GetType().Name, uri.Host); }
            }
            try
            {
                openExternalBrowser(url);
                Report("ExternalBrowserRequested", uri.Host);
                return ClientLinkOpenResult.ExternalBrowserRequested;
            }
            catch (Exception ex)
            {
                Report("LinkOpenUnavailable:" + ex.GetType().Name, uri.Host);
                return ClientLinkOpenResult.Unavailable;
            }
        }

        private void Report(string code, string host)
        {
            // Queries, paths and exception messages can contain credentials; never log them.
            try { diagnostic?.Invoke(code + " host=" + host); }
            catch { /* Logging cannot turn a successful browser request into a duplicate fallback request. */ }
        }
    }
}
