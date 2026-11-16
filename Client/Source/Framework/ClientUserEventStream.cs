using System;
using UserManagement;

namespace PhinixClient.Framework
{
    // Dispatch one immutable subscriber snapshot. Optional consumers cannot prevent later subscribers from seeing the same event.
    internal static class ClientEventDispatch
    {
        internal static void Raise(object sender, EventHandler handlers, EventArgs args, string eventName, Func<string,string,Exception,string> fault = null)
        {
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(sender, args); }
                catch (Exception exception) { Report(eventName, handler, exception, fault); }
            }
        }

        internal static void Raise<T>(object sender, EventHandler<T> handlers, T args, string eventName, Func<string,string,Exception,string> fault = null) where T : EventArgs
        {
            if (handlers == null) return;
            foreach (EventHandler<T> handler in handlers.GetInvocationList())
            {
                try { handler(sender, args); }
                catch (Exception exception) { Report(eventName, handler, exception, fault); }
            }
        }

        private static void Report(string eventName, Delegate handler, Exception exception, Func<string,string,Exception,string> fault)
        {
            // Include the failing code owner/type, never event payloads or arbitrary exception messages.
            try
            {
                string owner = handler.Method.DeclaringType?.FullName ?? "unknown";
                try { if(fault?.Invoke(handler.Method.DeclaringType?.Assembly.GetName().Name ?? "unknown", "Event/"+eventName+"/"+owner, exception)!=null) return; } catch { }
                Verse.Log.Error("[Phinix] Event subscriber failed: event=" + eventName +
                    ", owner=" + owner + ", exception=" + exception);
            }
            catch { /* Logging failure must not stop remaining subscribers. */ }
        }
    }

    internal sealed class ClientUserEventStream : IClientUserEventStream
    {
        private readonly Func<string,string,Exception,string> fault;
        internal ClientUserEventStream(Func<string,string,Exception,string> fault=null) { this.fault=fault; }
        public event EventHandler Disconnected;
        public event EventHandler UsersChanged;
        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;

        public void RaiseDisconnected() => ClientEventDispatch.Raise(this, Disconnected, EventArgs.Empty, nameof(Disconnected), fault);
        public void RaiseUsersChanged() => ClientEventDispatch.Raise(this, UsersChanged, EventArgs.Empty, nameof(UsersChanged), fault);
        public void RaiseUserDisplayNameChanged(UserDisplayNameChangedEventArgs args) => ClientEventDispatch.Raise(this, UserDisplayNameChanged, args, nameof(UserDisplayNameChanged), fault);
        public void RaiseBlockedUsersChanged(UserBlockStateChangedEventArgs args) => ClientEventDispatch.Raise(this, BlockedUsersChanged, args, nameof(BlockedUsersChanged), fault);
    }

    internal sealed class ClientShellEventStream : IClientShellEventStream
    {
        private readonly Func<string,string,Exception,string> fault;
        internal ClientShellEventStream(Func<string,string,Exception,string> fault=null) { this.fault=fault; }
        public event EventHandler MainWindowOpened;
        public void RaiseMainWindowOpened() => ClientEventDispatch.Raise(this, MainWindowOpened, EventArgs.Empty, nameof(MainWindowOpened), fault);
    }
}
