using System.Collections.Generic;
using System.Linq;

namespace PhinixClient.Framework
{
    internal sealed class ClientSettingsContextAdapter : IClientSettingsContext
    {
        private readonly Client client;

        public ClientSettingsContextAdapter(Client client)
        {
            this.client = client;
        }

        public IEnumerable<string> BlockedUsers => client.Settings?.BlockedUsers ?? Enumerable.Empty<string>();

        public bool PlayNoiseOnMessageReceived => client.Settings?.PlayNoiseOnMessageReceived ?? false;

        public int ChatMessageLimit => client.Settings?.ChatMessageLimit ?? 100;

        public bool ShowNameFormatting => client.Settings?.ShowNameFormatting ?? true;

        public bool ShowChatFormatting => client.Settings?.ShowChatFormatting ?? true;

        public bool AllItemsTradable => client.Settings?.AllItemsTradable ?? false;

        public bool ShowBlockedTrades => client.Settings?.ShowBlockedTrades ?? false;

        public bool CollapseBlockedUsers
        {
            get => client.Settings?.CollapseBlockedUsers ?? true;
            set
            {
                if (client.Settings == null || client.Settings.CollapseBlockedUsers == value)
                {
                    return;
                }

                client.Settings.CollapseBlockedUsers = value;
                client.Settings.AcceptChanges();
            }
        }

        public void BlockUser(string uuid) => client.BlockUser(uuid);

        public void UnBlockUser(string uuid) => client.UnBlockUser(uuid);
    }
}
