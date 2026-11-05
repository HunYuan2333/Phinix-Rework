#if PHINIX_RIMWORLD_17
namespace Authentication;
internal static class LegacyClientIdentityAdapter
{
    internal static string PreviousProof(CredentialStore store, string serverName)
    {
        if (store.Credentials.TryGetValue(serverName, out var credential))
        {
            if (credential.AuthType != AuthTypes.ClientKey) throw new System.InvalidOperationException("Previous identity is not eligible for automatic mapping.");
            if (string.IsNullOrEmpty(credential.Username)) throw new System.InvalidOperationException("Previous identity credential is missing.");
            return credential.Username;
        }
        // Historical zero-configuration fallback is restricted to this one-time compatibility proof.
        if (string.IsNullOrEmpty(store.ClientKey)) throw new System.InvalidOperationException("Previous installation identity is missing.");
        return store.ClientKey;
    }
}
#endif
