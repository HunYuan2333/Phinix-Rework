using System;
using UserManagement;

namespace PhinixClient.Framework
{
    // Dispatch one immutable subscriber snapshot. Optional consumers cannot prevent later subscribers from seeing the same event.
    internal static class ClientEventDispatch
    {
        internal static void Raise(object sender, EventHandler handlers, EventArgs args, string eventName)
        {
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(sender, args); }
                catch (Exception exception) { Report(eventName, handler, exception); }
            }
        }

        internal static void Raise<T>(object sender, EventHandler<T> handlers, T args, string eventName) where T : EventArgs
        {
            if (handlers == null) return;
            foreach (EventHandler<T> handler in handlers.GetInvocationList())
            {
                try { handler(sender, args); }
                catch (Exception exception) { Report(eventName, handler, exception); }
            }
        }

        private static void Report(string eventName, Delegate handler, Exception exception)
        {
            // Include the failing code owner/type, never event payloads or arbitrary exception messages.
            try
            {
                string owner = handler.Method.DeclaringType?.FullName ?? "unknown";
                Verse.Log.Error("[Phinix] Event subscriber failed: event=" + eventName +
                    ", owner=" + owner + ", exception=" + exception.GetType().Name);
            }
            catch { /* Logging failure must not stop remaining subscribers. */ }
        }
    }

    internal sealed class ClientUserEventStream : IClientUserEventStream
    {
        public event EventHandler Disconnected;
        public event EventHandler UsersChanged;
        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;

        public void RaiseDisconnected() => ClientEventDispatch.Raise(this, Disconnected, EventArgs.Empty, nameof(Disconnected));
        public void RaiseUsersChanged() => ClientEventDispatch.Raise(this, UsersChanged, EventArgs.Empty, nameof(UsersChanged));
        public void RaiseUserDisplayNameChanged(UserDisplayNameChangedEventArgs args) => ClientEventDispatch.Raise(this, UserDisplayNameChanged, args, nameof(UserDisplayNameChanged));
        public void RaiseBlockedUsersChanged(UserBlockStateChangedEventArgs args) => ClientEventDispatch.Raise(this, BlockedUsersChanged, args, nameof(BlockedUsersChanged));
    }

    internal sealed class ClientShellEventStream : IClientShellEventStream
    {
        public event EventHandler MainWindowOpened;
        public void RaiseMainWindowOpened() => ClientEventDispatch.Raise(this, MainWindowOpened, EventArgs.Empty, nameof(MainWindowOpened));
    }
}
