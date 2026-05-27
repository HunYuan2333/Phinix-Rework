using Authentication;
using UserManagement;

namespace PhinixClient.Framework
{
    internal sealed class ClientSessionContextAdapter : IClientSessionContext
    {
        private readonly ClientAuthenticator authenticator;
        private readonly ClientUserManager userManager;

        public ClientSessionContextAdapter(ClientAuthenticator authenticator, ClientUserManager userManager)
        {
            this.authenticator = authenticator;
            this.userManager = userManager;
        }

        public bool Authenticated => authenticator?.Authenticated ?? false;

        public bool LoggedIn => userManager?.LoggedIn ?? false;

        public string SessionId => authenticator?.SessionId;

        public string Uuid => userManager?.Uuid;
    }
}
