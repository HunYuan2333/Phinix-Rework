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

        private bool originalPlayNoiseOnMessageReceived;
        private bool playNoiseOnMessageReceived;
        public bool PlayNoiseOnMessageReceived
        {
            get => playNoiseOnMessageReceived;
            set => playNoiseOnMessageReceived = value;
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

        /// <inheritdoc/>
        public bool IsChanged
        {
            get
            {
                return serverAddress != originalServerAddress ||
                       serverPort != originalServerPort ||
                       displayName != originalDisplayName ||
                       playNoiseOnMessageReceived != originalPlayNoiseOnMessageReceived ||
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
            playNoiseOnMessageReceived = true;
            migrated = false;
            collapseBlockedUsers = true;

            // Default extension settings
            extensionSettings = new Dictionary<string, object>
            {
                { "chat.showNameFormatting", true },
                { "chat.showChatFormatting", true },
                { "chat.showUnreadMessageCount", true },
                { "chat.showBlockedUnreadMessageCount", false },
                { "chat.messageLimit", 40 },
                { "chat.forceMessageFieldFocus", true },
                { "trade.acceptingTrades", true },
                { "trade.allItemsTradable", false },
                { "trade.showBlockedTrades", false },
                { "trade.dropCurrentMap", false }
            };

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
            Scribe_Values.Look(ref playNoiseOnMessageReceived, "playNoiseOnMessageReceived", true);
            Scribe_Values.Look(ref migrated, "migrated", false);
            Scribe_Collections.Look(ref blockedUsers, "blockedUsers", LookMode.Value);
            Scribe_Values.Look(ref collapseBlockedUsers, "collapseBlockedUsers", true);

            // Migrate old business fields to ExtensionSettings on first load
            if (!migrated)
            {
                MigrateBusinessFieldsToExtensionSettings();
                migrated = true;
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

                // Restore defaults for any missing keys
                foreach (var defaultKvp in new Dictionary<string, object>
                {
                    { "chat.showNameFormatting", "True" },
                    { "chat.showChatFormatting", "True" },
                    { "chat.showUnreadMessageCount", "True" },
                    { "chat.showBlockedUnreadMessageCount", "False" },
                    { "chat.messageLimit", "40" },
                    { "chat.forceMessageFieldFocus", "True" },
                    { "trade.acceptingTrades", "True" },
                    { "trade.allItemsTradable", "False" },
                    { "trade.showBlockedTrades", "False" },
                    { "trade.dropCurrentMap", "False" }
                })
                {
                    if (!extensionSettings.ContainsKey(defaultKvp.Key))
                    {
                        extensionSettings[defaultKvp.Key] = defaultKvp.Value;
                    }
                }
            }

            // Prevent scribe from interpreting a missing value as null
            if (blockedUsers is null) blockedUsers = new HashSet<string>();
            if (extensionSettings is null) extensionSettings = new Dictionary<string, object>();
        }

        private void MigrateBusinessFieldsToExtensionSettings()
        {
            // Migrate old fields that were previously serialized by Scribe
            // Scribe_Values.Look tries to read from the save file; if values exist from old versions, they're in the file.
            // Since we're in ExposeData(), we try to read old key names.
            // Note: old keys may not be present in newer saves, so we use the default to detect what was actually saved.
            // Unfortunately Scribe doesn't support fallback detection cleanly — we read unconditionally with defaults.

            // Read old fields with their original defaults
            bool acceptingTrades = true;
            bool showNameFormatting = true;
            bool showChatFormatting = true;
            bool showUnreadMessageCount = true;
            bool showBlockedUnreadMessageCount = false;
            int chatMessageLimit = 40;
            bool forceMessageFieldFocus = true;
            bool allItemsTradable = false;
            bool showBlockedTrades = false;
            bool dropCurrentMap = false;

            // Try to read old Scribe values — Scribe_Values.Look sets the variable to the saved value or keeps the default
            Scribe_Values.Look(ref acceptingTrades, "acceptingTrades", true);
            Scribe_Values.Look(ref showNameFormatting, "showNameFormatting", true);
            Scribe_Values.Look(ref showChatFormatting, "showChatFormatting", true);
            Scribe_Values.Look(ref showUnreadMessageCount, "showUnreadMessageCount", true);
            Scribe_Values.Look(ref showBlockedUnreadMessageCount, "showBlockedUnreadMessageCount", false);
            Scribe_Values.Look(ref chatMessageLimit, "chatMessageLimit", 40);
            Scribe_Values.Look(ref forceMessageFieldFocus, "forceMessageFieldFocus", true);
            Scribe_Values.Look(ref allItemsTradable, "allItemsTradable", false);
            Scribe_Values.Look(ref showBlockedTrades, "showBlockedTrades", false);
            Scribe_Values.Look(ref dropCurrentMap, "dropCurrentMap", false);

            // Write migrated values into ExtensionSettings
            extensionSettings["trade.acceptingTrades"] = acceptingTrades.ToString();
            extensionSettings["chat.showNameFormatting"] = showNameFormatting.ToString();
            extensionSettings["chat.showChatFormatting"] = showChatFormatting.ToString();
            extensionSettings["chat.showUnreadMessageCount"] = showUnreadMessageCount.ToString();
            extensionSettings["chat.showBlockedUnreadMessageCount"] = showBlockedUnreadMessageCount.ToString();
            extensionSettings["chat.messageLimit"] = chatMessageLimit.ToString();
            extensionSettings["chat.forceMessageFieldFocus"] = forceMessageFieldFocus.ToString();
            extensionSettings["trade.allItemsTradable"] = allItemsTradable.ToString();
            extensionSettings["trade.showBlockedTrades"] = showBlockedTrades.ToString();
            extensionSettings["trade.dropCurrentMap"] = dropCurrentMap.ToString();
        }

        /// <inheritdoc/>
        public void AcceptChanges()
        {
            Write();
            SetOriginalValues();
        }

        /// <summary>
        /// Attempts to load settings saved in the HugsLib format and applies them to the current instance. Changes are saved immediately.
        /// </summary>
        public void MigrateFromHugsLib()
        {
            LegacySettings legacySettings = LegacySettings.FromHugsLibSettings(System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml"));
            if (legacySettings != null)
            {
                ServerAddress = legacySettings.ServerAddress ?? ServerAddress;
                ServerPort = legacySettings.ServerPort ?? ServerPort;
                DisplayName = legacySettings.DisplayName ?? DisplayName;
                PlayNoiseOnMessageReceived = legacySettings.PlayNoiseOnMessageReceived ?? PlayNoiseOnMessageReceived;

                // Migrate business fields to ExtensionSettings
                if (legacySettings.AcceptingTrades.HasValue)
                    extensionSettings["trade.acceptingTrades"] = legacySettings.AcceptingTrades.Value.ToString();
                if (legacySettings.ShowNameFormatting.HasValue)
                    extensionSettings["chat.showNameFormatting"] = legacySettings.ShowNameFormatting.Value.ToString();
                if (legacySettings.ShowChatFormatting.HasValue)
                    extensionSettings["chat.showChatFormatting"] = legacySettings.ShowChatFormatting.Value.ToString();
                if (legacySettings.ShowUnreadMessageCount.HasValue)
                    extensionSettings["chat.showUnreadMessageCount"] = legacySettings.ShowUnreadMessageCount.Value.ToString();
                if (legacySettings.ShowBlockedUnreadMessageCount.HasValue)
                    extensionSettings["chat.showBlockedUnreadMessageCount"] = legacySettings.ShowBlockedUnreadMessageCount.Value.ToString();
                if (legacySettings.ChatMessageLimit.HasValue)
                    extensionSettings["chat.messageLimit"] = legacySettings.ChatMessageLimit.Value.ToString();
                if (legacySettings.ForceMessageFieldFocus.HasValue)
                    extensionSettings["chat.forceMessageFieldFocus"] = legacySettings.ForceMessageFieldFocus.Value.ToString();
                if (legacySettings.AllItemsTradable.HasValue)
                    extensionSettings["trade.allItemsTradable"] = legacySettings.AllItemsTradable.Value.ToString();
                if (legacySettings.ShowBlockedTrades.HasValue)
                    extensionSettings["trade.showBlockedTrades"] = legacySettings.ShowBlockedTrades.Value.ToString();

                BlockedUsers.Clear();
                BlockedUsers.AddRange(legacySettings.BlockedUsers);
            }

            Migrated = true;

            Write();
            AcceptChanges();

            Log.Message("Migrated settings from HugsLib.");
        }

        /// <summary>
        /// Resets the object's state by copying to the original state variables.
        /// </summary>
        private void SetOriginalValues()
        {
            originalServerAddress = serverAddress;
            originalServerPort = serverPort;
            originalDisplayName = displayName;
            originalPlayNoiseOnMessageReceived = playNoiseOnMessageReceived;
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
