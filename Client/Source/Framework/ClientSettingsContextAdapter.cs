using System;
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

        public event Action<string, object> OnSettingChanged;

        public T Get<T>(string key, T defaultValue = default)
        {
            if (client.Settings == null) return defaultValue;
            return client.Settings.GetExtensionSetting(key, defaultValue);
        }

        public void Set<T>(string key, T value)
        {
            if (client.Settings == null) return;

            client.Settings.SetExtensionSetting(key, value);
            client.Settings.AcceptChanges();
            OnSettingChanged?.Invoke(key, value);
        }

        public IEnumerable<string> BlockedUsers => client.Settings?.BlockedUsers ?? Enumerable.Empty<string>();

        public bool PlayNoiseOnMessageReceived => client.Settings?.PlayNoiseOnMessageReceived ?? false;

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
