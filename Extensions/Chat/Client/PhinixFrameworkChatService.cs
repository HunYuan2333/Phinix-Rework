using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class PhinixFrameworkChatService : IFrameworkChatClientApi
    {
        /// <summary>
        /// 系统消息通用占位用户，延迟初始化避免静态构造阶段访问翻译系统。
        /// RimWorld 的 LanguageDatabase 在 mod 构造函数之后才激活；
        /// 静态初始化器中调用 Translate() 会导致 TypeInitializer 异常，
        /// 进而使整个 BuiltInChatClientExtension 注册失败。
        /// </summary>
        private static ImmutableUser _systemUser;
        private static bool _systemUserInitialized;

        private static ImmutableUser GetSystemUser()
        {
            if (_systemUserInitialized) return _systemUser;
            string name = "Phinix_framework_systemDisplayName".Translate().Resolve();
            _systemUser = new ImmutableUser(FrameworkProtocol.SystemSenderUuid, name, true, false);
            _systemUserInitialized = true;
            return _systemUser;
        }

        public FrameworkPacket CreateOutgoingMessage(string rawMessage, ClientFrameworkContext context)
        {
            global::Phinix.Framework.BuiltInChatMessagePayload payload = new global::Phinix.Framework.BuiltInChatMessagePayload
            {
                MessageId = Guid.NewGuid().ToString(),
                Message = rawMessage ?? string.Empty
            };

            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Message,
                MessageType = FrameworkChatProtocol.MessageType,
                MessageId = payload.MessageId,
                SenderUuid = context.SenderUuid,
                PayloadBytes = payload.ToByteArray()
            };
        }

        public FrameworkDisplayMessage RenderMessage(FrameworkPacket message)
        {
            global::Phinix.Framework.BuiltInChatMessagePayload payload = global::Phinix.Framework.BuiltInChatMessagePayload.Parser.ParseFrom(message.PayloadBytes ?? Array.Empty<byte>());
            long timestampTicks = payload.Timestamp != null
                ? payload.Timestamp.ToDateTime().ToUniversalTime().Ticks
                : message.TimestampUtcTicks;

            return new FrameworkDisplayMessage
            {
                MessageId = payload.MessageId ?? message.MessageId,
                SenderUuid = payload.SenderUuid ?? message.SenderUuid,
                TimestampUtcTicks = timestampTicks,
                Source = "builtin_chat",
                Text = payload.Message
            };
        }

        public FrameworkPacket CreateHistoryRequestPacket(string sessionId, string senderUuid)
        {
            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkChatProtocol.HistoryRequestType,
                SessionId = sessionId ?? string.Empty,
                SenderUuid = senderUuid ?? string.Empty,
                PayloadBytes = new Empty().ToByteArray()
            };
        }

        public UIChatMessage[] BuildUiMessages(IEnumerable<FrameworkDisplayMessage> messages, IClientUserDirectory userDirectory)
        {
            return (messages ?? Enumerable.Empty<FrameworkDisplayMessage>())
                .Select(message => ToUiMessage(message, userDirectory))
                .OrderBy(message => message.Timestamp)
                .ToArray();
        }

        public bool TryGetUiMessage(IEnumerable<FrameworkDisplayMessage> messages, string messageId, IClientUserDirectory userDirectory, out UIChatMessage message)
        {
            FrameworkDisplayMessage frameworkMessage = (messages ?? Enumerable.Empty<FrameworkDisplayMessage>())
                .SingleOrDefault(candidate => candidate.MessageId == messageId);

            if (frameworkMessage == null)
            {
                message = null;
                return false;
            }

            message = ToUiMessage(frameworkMessage, userDirectory);
            return true;
        }

        public int CountUnreadExcluding(IEnumerable<FrameworkDisplayMessage> messages, IEnumerable<string> excludedUuids)
        {
            ICollection<string> excluded = (excludedUuids as ICollection<string>)
                ?? new HashSet<string>(excludedUuids ?? Enumerable.Empty<string>());
            int count = 0;
            foreach (FrameworkDisplayMessage message in (messages ?? Enumerable.Empty<FrameworkDisplayMessage>()))
            {
                if (!excluded.Contains(message.SenderUuid))
                {
                    count++;
                }
            }

            return count;
        }

        public bool ShouldDisplayChatMessage(UIChatMessage message, IEnumerable<string> blockedUserUuids, bool includeBlockedMessages)
        {
            if (message == null)
            {
                return false;
            }

            if (includeBlockedMessages)
            {
                return true;
            }

            HashSet<string> blockedUsers = new HashSet<string>(blockedUserUuids ?? Enumerable.Empty<string>());
            return !blockedUsers.Contains(message.SenderUuid);
        }

        public bool ShouldPlayNotification(UIChatMessage message, string localUuid, bool playNoiseOnMessageReceived, bool isInGame, IEnumerable<string> blockedUserUuids)
        {
            if (message == null || !playNoiseOnMessageReceived || !isInGame)
            {
                return false;
            }

            if (message.SenderUuid == localUuid || message.SenderUuid == FrameworkProtocol.SystemSenderUuid)
            {
                return false;
            }

            HashSet<string> blockedUsers = new HashSet<string>(blockedUserUuids ?? Enumerable.Empty<string>());
            return !blockedUsers.Contains(message.SenderUuid);
        }

        public UIChatMessage ToUiMessage(FrameworkDisplayMessage message, IClientUserDirectory userDirectory)
        {
            ImmutableUser user;
            if (message.SenderUuid == FrameworkProtocol.SystemSenderUuid)
            {
                user = GetSystemUser();
            }
            else if (userDirectory == null || !userDirectory.TryGetUser(message.SenderUuid, out user))
            {
                user = new ImmutableUser(message.SenderUuid);
            }

            return new UIChatMessage(
                messageId: message.MessageId,
                senderUuid: message.SenderUuid,
                message: ResolveDisplayText(message),
                timestamp: new DateTime(message.TimestampUtcTicks, DateTimeKind.Utc),
                status: UIChatMessageStatus.Confirmed,
                user: user,
                source: message.Source);
        }

        public bool CanFormat(FrameworkDisplayMessage message)
        {
            return message != null && message.Source == "builtin_chat";
        }

        public UIChatMessage Format(FrameworkDisplayMessage message, IClientUserDirectory userDirectory)
        {
            return ToUiMessage(message, userDirectory);
        }

        private string ResolveDisplayText(FrameworkDisplayMessage message)
        {
            if (!string.IsNullOrEmpty(message.TranslationKey))
            {
                List<string> translationArgs = message.TranslationArgs ?? new List<string>();
                if (translationArgs.Any())
                {
#pragma warning disable CS0618
                    return message.TranslationKey.Translate(translationArgs.Cast<object>().ToArray());
#pragma warning restore CS0618
                }

                return message.TranslationKey.Translate();
            }

            return message.Text ?? string.Empty;
        }
    }
}
