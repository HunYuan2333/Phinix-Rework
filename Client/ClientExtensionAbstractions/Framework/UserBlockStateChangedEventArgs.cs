using System;

namespace PhinixClient.Framework
{
    public sealed class UserBlockStateChangedEventArgs : EventArgs
    {
        public UserBlockStateChangedEventArgs(string uuid, bool isBlocked)
        {
            Uuid = uuid;
            IsBlocked = isBlocked;
        }

        public string Uuid { get; }

        public bool IsBlocked { get; }
    }
}
