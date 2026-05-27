using UserManagement;

namespace PhinixClient.Framework
{
    public sealed class ClientFrameworkUserDirectoryAdapter : IClientUserDirectory
    {
        private readonly ClientUserManager userManager;

        public ClientFrameworkUserDirectoryAdapter(ClientUserManager userManager)
        {
            this.userManager = userManager;
        }

        public string Uuid => userManager?.Uuid ?? string.Empty;

        public ImmutableUser[] GetUsers(bool loggedIn = false)
        {
            return userManager?.GetUsers(loggedIn) ?? new ImmutableUser[0];
        }

        public bool TryGetUser(string uuid, out ImmutableUser user)
        {
            if (userManager != null)
            {
                return userManager.TryGetUser(uuid, out user);
            }

            user = default(ImmutableUser);
            return false;
        }
    }
}
