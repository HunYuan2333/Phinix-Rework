using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatUiHostContext : IChatUiHostContext
    {
        private readonly IClientChatService chatService;
        private readonly IClientSessionContext session;
        private readonly IClientSettingsContext settings;
        private readonly IClientUserEventStream userEvents;
        private readonly Action<string> createTrade;
        private readonly Action<LogEventArgs> log;

        public ChatUiHostContext(
            IClientChatService chatService,
            IClientSessionContext session,
            IClientSettingsContext settings,
            IClientUserEventStream userEvents,
            Action<string> createTrade,
            Action<LogEventArgs> log)
        {
            this.chatService = chatService;
            this.session = session;
            this.settings = settings;
            this.userEvents = userEvents;
            this.createTrade = createTrade;
            this.log = log;
        }

        public IClientChatService ChatService => chatService;

        public string Uuid => session.Uuid;

        public int ChatMessageLimit => settings.Get<int>("chat.messageLimit", 100);

        public bool ShowNameFormatting => settings.Get<bool>("chat.showNameFormatting", true);

        public bool ShowChatFormatting => settings.Get<bool>("chat.showChatFormatting", true);

        public ISet<string> BlockedUsers => new HashSet<string>(settings.BlockedUsers);

        public event EventHandler OnDisconnect
        {
            add => userEvents.Disconnected += value;
            remove => userEvents.Disconnected -= value;
        }

        public event EventHandler OnUsersChanged
        {
            add => userEvents.UsersChanged += value;
            remove => userEvents.UsersChanged -= value;
        }

        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged
        {
            add => userEvents.UserDisplayNameChanged += value;
            remove => userEvents.UserDisplayNameChanged -= value;
        }

        public event EventHandler<UserBlockStateChangedEventArgs> OnBlockedUsersChanged
        {
            add => userEvents.BlockedUsersChanged += value;
            remove => userEvents.BlockedUsersChanged -= value;
        }

        public void CreateTrade(string uuid) => createTrade?.Invoke(uuid);

        public void BlockUser(string uuid) => settings.BlockUser(uuid);

        public void UnBlockUser(string uuid) => settings.UnBlockUser(uuid);

        public void Log(LogEventArgs args) => log?.Invoke(args);
    }
}
