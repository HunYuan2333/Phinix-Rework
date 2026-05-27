using System;
using System.Collections.Generic;
using UserManagement;
using Utils.Framework;
using Verse;

namespace PhinixClient.Framework
{
    public interface IFrameworkClientTransport
    {
        bool HasRemoteCapability(string capability);

        void SendFrameworkPacket(FrameworkPacket packet);
    }

    public interface IClientDisplayMessageStore
    {
        int UnreadMessages { get; }

        void MarkAsRead();

        FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true);

        FrameworkDisplayMessage[] GetDisplayMessages();
    }

    public interface IClientDisplayMessageFeed
    {
        event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived;
    }

    public sealed class FrameworkCompatibilityModeChangedEventArgs : EventArgs
    {
        public FrameworkCompatibilityModeChangedEventArgs(FrameworkCompatibilityMode compatibilityMode)
        {
            CompatibilityMode = compatibilityMode;
        }

        public FrameworkCompatibilityMode CompatibilityMode { get; }
    }

    public interface IFrameworkClientLifecycle
    {
        FrameworkCompatibilityMode CompatibilityMode { get; }

        event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
    }

    public interface IClientSessionContext
    {
        bool Authenticated { get; }

        bool LoggedIn { get; }

        string SessionId { get; }

        string Uuid { get; }
    }

    public interface IClientSettingsContext
    {
        IEnumerable<string> BlockedUsers { get; }

        bool PlayNoiseOnMessageReceived { get; }

        int ChatMessageLimit { get; }

        bool ShowNameFormatting { get; }

        bool ShowChatFormatting { get; }

        bool AllItemsTradable { get; }

        bool ShowBlockedTrades { get; }

        bool CollapseBlockedUsers { get; set; }

        void BlockUser(string uuid);

        void UnBlockUser(string uuid);
    }

    public interface IClientUserDirectory
    {
        string Uuid { get; }

        ImmutableUser[] GetUsers(bool loggedIn = false);

        bool TryGetUser(string uuid, out ImmutableUser user);
    }

    public interface IClientUserEventStream
    {
        event EventHandler Disconnected;

        event EventHandler UsersChanged;

        event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;

        event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
    }

    public interface IClientMainThreadDispatcher
    {
        void Enqueue(Action action);
    }

    public interface IClientWindowService
    {
        void Open(Window window);

        void OpenSettingsWindow();
    }

    public interface IClientSoundService
    {
        void Enqueue(SoundDef soundDef);
    }
}
