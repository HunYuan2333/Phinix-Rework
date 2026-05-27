using System;
using UserManagement;

namespace PhinixClient.Framework
{
    internal sealed class ClientUserEventStream : IClientUserEventStream
    {
        public event EventHandler Disconnected;

        public event EventHandler UsersChanged;

        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;

        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;

        public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

        public void RaiseUsersChanged() => UsersChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseUserDisplayNameChanged(UserDisplayNameChangedEventArgs args) => UserDisplayNameChanged?.Invoke(this, args);

        public void RaiseBlockedUsersChanged(UserBlockStateChangedEventArgs args) => BlockedUsersChanged?.Invoke(this, args);
    }
}
