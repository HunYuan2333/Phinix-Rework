using Authentication;
using UserManagement;

namespace PhinixClient.Framework
{
    internal sealed class ClientSessionContextAdapter : IClientSessionContext, IClientConnectionIdentity
    {
        private readonly ClientAuthenticator authenticator;
        private readonly ClientUserManager userManager;
        private readonly System.Func<string> endpoint;

        public ClientSessionContextAdapter(ClientAuthenticator authenticator, ClientUserManager userManager, System.Func<string> endpoint = null)
        {
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.endpoint = endpoint;
        }

        public string RemoteEndpoint => endpoint?.Invoke();

        public bool Authenticated => authenticator?.Authenticated ?? false;

        public bool LoggedIn => userManager?.LoggedIn ?? false;

        public string SessionId => authenticator?.SessionId;

        public string Uuid => userManager?.Uuid;
    }
}
