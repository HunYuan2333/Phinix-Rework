using System;
using System.Linq;
using PhinixClient.Framework;
using UserManagement;
using Utils;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// 插件运行时宿主：把原 mod 对 `Client.Instance` 的耦合集中到一处，
    /// 由插件入口在 Register/Activate 时注入框架服务（解耦清单见实施方案 §3.7）。
    /// </summary>
    internal static class LegacyTalentTradeRuntime
    {
        public static IClientSessionContext Session;
        public static IClientUserDirectory Users;
        public static IClientMainThreadDispatcher Dispatcher;
        public static IClientSettingsContext SettingsContext;
        public static LegacyTalentTradeSettings Settings = new LegacyTalentTradeSettings();
        public static Action<string, LogLevel> Log;

        /// <summary>插件是否处于激活状态（Activate 置 true，Shutdown 置 false）。</summary>
        public static bool IsActive;

        public static string LocalUuid => Session?.Uuid ?? string.Empty;

        public static bool IsOnline => Session != null && Session.Authenticated && Session.LoggedIn;

        public static bool TryGetDisplayName(string uuid, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(uuid) || Users == null) return false;
            if (Users.TryGetUser(uuid, out ImmutableUser user))
            {
                name = user.DisplayName;
                return true;
            }
            return false;
        }

        public static string[] GetOnlineUserUuids()
        {
            if (Users == null) return new string[0];
            return Users.GetUsers(true).Select(user => user.Uuid).ToArray();
        }

        public static void LogMessage(string message) => Log?.Invoke(message, LogLevel.INFO);

        public static void LogWarning(string message) => Log?.Invoke(message, LogLevel.WARNING);

        public static void LogError(string message) => Log?.Invoke(message, LogLevel.ERROR);
    }
}
