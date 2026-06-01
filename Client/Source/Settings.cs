using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Verse;

namespace PhinixClient
{
    public class Settings : ModSettings, IChangeTracking
    {
        #region Properties

        private string originalServerAddress;
        private string serverAddress;
        public string ServerAddress
        {
            get => serverAddress;
            set => serverAddress = value;
        }

        private int originalServerPort;
        private int serverPort;
        public int ServerPort
        {
            get => serverPort;
            set => serverPort = value;
        }

        private string originalDisplayName;
        private string displayName;
        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        [Obsolete("Use chat.playNoiseOnMessageReceived extension setting instead.")]
        public bool PlayNoiseOnMessageReceived
        {
            get => GetExtensionSetting("chat.playNoiseOnMessageReceived", true);
            set => SetExtensionSetting("chat.playNoiseOnMessageReceived", value);
        }

        private bool originalMigrated;
        private bool migrated;
        public bool Migrated
        {
            get => migrated;
            set => migrated = value;
        }

        private HashSet<string> originalBlockedUsers;
        private HashSet<string> blockedUsers;
        public HashSet<string> BlockedUsers => blockedUsers;

        private Dictionary<string, object> originalExtensionSettings;
        private Dictionary<string, object> extensionSettings;
        public Dictionary<string, object> ExtensionSettings => extensionSettings;

        private bool originalCollapseBlockedUsers;
        private bool collapseBlockedUsers;
        public bool CollapseBlockedUsers
        {
            get => collapseBlockedUsers;
            set => collapseBlockedUsers = value;
        }

        private bool legacyAcceptingTrades = true;
        private bool legacyShowNameFormatting = true;
        private bool legacyShowChatFormatting = true;
        private bool legacyShowUnreadMessageCount = true;
        private bool legacyShowBlockedUnreadMessageCount = false;
        private int legacyChatMessageLimit = 40;
        private bool legacyForceMessageFieldFocus = true;
        private bool legacyPlayNoiseOnMessageReceived = true;
        private bool legacyAllItemsTradable = false;
        private bool legacyShowBlockedTrades = false;
        private bool legacyDropCurrentMap = false;

        /// <inheritdoc/>
        public bool IsChanged
        {
            get
            {
                return serverAddress != originalServerAddress ||
                       serverPort != originalServerPort ||
                       displayName != originalDisplayName ||
                       migrated != originalMigrated ||
                       !blockedUsers.SequenceEqual(originalBlockedUsers) ||
                       !extensionSettingsEqual() ||
                       collapseBlockedUsers != originalCollapseBlockedUsers;
            }
        }

        #endregion

        #region Constructors

        public Settings()
        {
            // Always set defaults
            serverAddress = "phinix.chat";
            serverPort = 16200;
            displayName = SteamUtility.SteamPersonaName;
            migrated = false;
            collapseBlockedUsers = true;
            extensionSettings = new Dictionary<string, object>();

            originalBlockedUsers = new HashSet<string>();
            blockedUsers = new HashSet<string>();
            originalExtensionSettings = new Dictionary<string, object>();

            SetOriginalValues();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets an extension setting value by key.
        /// </summary>
        public T GetExtensionSetting<T>(string key, T defaultValue = default)
        {
            if (extensionSettings != null && extensionSettings.TryGetValue(key, out object value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Sets an extension setting value by key.
        /// </summary>
        public void SetExtensionSetting<T>(string key, T value)
        {
            if (extensionSettings == null)
            {
                extensionSettings = new Dictionary<string, object>();
            }

            extensionSettings[key] = value;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref serverAddress, "serverAddress", "phinix.chat");
            Scribe_Values.Look(ref serverPort, "serverPort", 16200);
            Scribe_Values.Look(ref displayName, "displayName", SteamUtility.SteamPersonaName);
            Scribe_Values.Look(ref migrated, "migrated", false);
            Scribe_Collections.Look(ref blockedUsers, "blockedUsers", LookMode.Value);
            Scribe_Values.Look(ref collapseBlockedUsers, "collapseBlockedUsers", true);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref legacyAcceptingTrades, "acceptingTrades", true);
                Scribe_Values.Look(ref legacyShowNameFormatting, "showNameFormatting", true);
                Scribe_Values.Look(ref legacyShowChatFormatting, "showChatFormatting", true);
                Scribe_Values.Look(ref legacyShowUnreadMessageCount, "showUnreadMessageCount", true);
                Scribe_Values.Look(ref legacyShowBlockedUnreadMessageCount, "showBlockedUnreadMessageCount", false);
                Scribe_Values.Look(ref legacyChatMessageLimit, "chatMessageLimit", 40);
                Scribe_Values.Look(ref legacyForceMessageFieldFocus, "forceMessageFieldFocus", true);
                Scribe_Values.Look(ref legacyPlayNoiseOnMessageReceived, "playNoiseOnMessageReceived", true);
                Scribe_Values.Look(ref legacyAllItemsTradable, "allItemsTradable", false);
                Scribe_Values.Look(ref legacyShowBlockedTrades, "showBlockedTrades", false);
                Scribe_Values.Look(ref legacyDropCurrentMap, "dropCurrentMap", false);
            }

            // Serialize extension settings
            List<string> extensionKeys = new List<string>();
            List<string> extensionValues = new List<string>();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (extensionSettings != null)
                {
                    foreach (var kvp in extensionSettings)
                    {
                        extensionKeys.Add(kvp.Key);
                        extensionValues.Add(kvp.Value?.ToString() ?? "");
                    }
                }
            }
            Scribe_Collections.Look(ref extensionKeys, "extensionSettingsKeys", LookMode.Value);
            Scribe_Collections.Look(ref extensionValues, "extensionSettingsValues", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                extensionSettings = new Dictionary<string, object>();
                if (extensionKeys != null && extensionValues != null)
                {
                    int count = Math.Min(extensionKeys.Count, extensionValues.Count);
                    for (int i = 0; i < count; i++)
                    {
                        extensionSettings[extensionKeys[i]] = extensionValues[i];
                    }
                }

            }

            // Prevent scribe from interpreting a missing value as null
            if (blockedUsers is null) blockedUsers = new HashSet<string>();
            if (extensionSettings is null) extensionSettings = new Dictionary<string, object>();
        }

        /// <inheritdoc/>
        public void AcceptChanges()
        {
            Write();
            SetOriginalValues();
        }

        /// <summary>
        /// Attempts to load legacy settings and lets extensions migrate their own keys.
        /// </summary>
        public void MigrateLegacySettings(PhinixClient.Framework.IClientSettingsContext settingsContext, IEnumerable<PhinixClient.Framework.IClientLegacySettingsMigrator> migrators)
        {
            if (migrated)
            {
                return;
            }

            Dictionary<string, string> legacyValues = BuildLegacySettingValues();
            LegacySettings legacySettings = LegacySettings.FromHugsLibSettings(System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml"));
            if (legacySettings != null)
            {
                ServerAddress = legacySettings.ServerAddress ?? ServerAddress;
                ServerPort = legacySettings.ServerPort ?? ServerPort;
                DisplayName = legacySettings.DisplayName ?? DisplayName;
                mergeLegacyValues(legacyValues, legacySettings);

                BlockedUsers.Clear();
                BlockedUsers.AddRange(legacySettings.BlockedUsers);
            }

            foreach (var migrator in migrators ?? Array.Empty<PhinixClient.Framework.IClientLegacySettingsMigrator>())
            {
                migrator.TryMigrateLegacySettings(settingsContext, legacyValues);
            }

            Migrated = true;
            AcceptChanges();

            Log.Message("Migrated legacy client settings.");
        }

        private Dictionary<string, string> BuildLegacySettingValues()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["acceptingTrades"] = legacyAcceptingTrades.ToString(),
                ["showNameFormatting"] = legacyShowNameFormatting.ToString(),
                ["showChatFormatting"] = legacyShowChatFormatting.ToString(),
                ["showUnreadMessageCount"] = legacyShowUnreadMessageCount.ToString(),
                ["showBlockedUnreadMessageCount"] = legacyShowBlockedUnreadMessageCount.ToString(),
                ["chatMessageLimit"] = legacyChatMessageLimit.ToString(),
                ["forceMessageFieldFocus"] = legacyForceMessageFieldFocus.ToString(),
                ["playNoiseOnMessageReceived"] = legacyPlayNoiseOnMessageReceived.ToString(),
                ["allItemsTradable"] = legacyAllItemsTradable.ToString(),
                ["showBlockedTrades"] = legacyShowBlockedTrades.ToString(),
                ["dropCurrentMap"] = legacyDropCurrentMap.ToString()
            };
        }

        private static void mergeLegacyValues(Dictionary<string, string> legacyValues, LegacySettings legacySettings)
        {
            if (legacySettings.PlayNoiseOnMessageReceived.HasValue)
                legacyValues["playNoiseOnMessageReceived"] = legacySettings.PlayNoiseOnMessageReceived.Value.ToString();
            if (legacySettings.AcceptingTrades.HasValue)
                legacyValues["acceptingTrades"] = legacySettings.AcceptingTrades.Value.ToString();
            if (legacySettings.ShowNameFormatting.HasValue)
                legacyValues["showNameFormatting"] = legacySettings.ShowNameFormatting.Value.ToString();
            if (legacySettings.ShowChatFormatting.HasValue)
                legacyValues["showChatFormatting"] = legacySettings.ShowChatFormatting.Value.ToString();
            if (legacySettings.ShowUnreadMessageCount.HasValue)
                legacyValues["showUnreadMessageCount"] = legacySettings.ShowUnreadMessageCount.Value.ToString();
            if (legacySettings.ShowBlockedUnreadMessageCount.HasValue)
                legacyValues["showBlockedUnreadMessageCount"] = legacySettings.ShowBlockedUnreadMessageCount.Value.ToString();
            if (legacySettings.ChatMessageLimit.HasValue)
                legacyValues["chatMessageLimit"] = legacySettings.ChatMessageLimit.Value.ToString();
            if (legacySettings.ForceMessageFieldFocus.HasValue)
                legacyValues["forceMessageFieldFocus"] = legacySettings.ForceMessageFieldFocus.Value.ToString();
            if (legacySettings.AllItemsTradable.HasValue)
                legacyValues["allItemsTradable"] = legacySettings.AllItemsTradable.Value.ToString();
            if (legacySettings.ShowBlockedTrades.HasValue)
                legacyValues["showBlockedTrades"] = legacySettings.ShowBlockedTrades.Value.ToString();
            if (legacySettings.DropCurrentMap.HasValue)
                legacyValues["dropCurrentMap"] = legacySettings.DropCurrentMap.Value.ToString();
        }

        /// <summary>
        /// Resets the object's state by copying to the original state variables.
        /// </summary>
        private void SetOriginalValues()
        {
            originalServerAddress = serverAddress;
            originalServerPort = serverPort;
            originalDisplayName = displayName;
            originalMigrated = migrated;
            originalCollapseBlockedUsers = collapseBlockedUsers;

            originalBlockedUsers.Clear();
            originalBlockedUsers.AddRange(blockedUsers);

            originalExtensionSettings.Clear();
            if (extensionSettings != null)
            {
                foreach (var kvp in extensionSettings)
                {
                    originalExtensionSettings[kvp.Key] = kvp.Value;
                }
            }
        }

        private bool extensionSettingsEqual()
        {
            if (extensionSettings is null && originalExtensionSettings is null) return true;
            if (extensionSettings is null || originalExtensionSettings is null) return false;
            if (extensionSettings.Count != originalExtensionSettings.Count) return false;

            foreach (var kvp in extensionSettings)
            {
                if (!originalExtensionSettings.TryGetValue(kvp.Key, out object otherValue)) return false;
                if (!object.Equals(kvp.Value, otherValue)) return false;
            }

            return true;
        }

        #endregion
    }
}
