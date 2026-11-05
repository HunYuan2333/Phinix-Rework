using System;
using System.Threading;

namespace PhinixClient.Framework
{
    // Business-neutral lifetime fence. The receiving thread sees only an opaque token.
    internal sealed class ClientGameScopeService : IClientGameScopeService
    {
        private readonly Func<object> game;
        private readonly Func<bool> isMainThread;
        private Scope current;
        private bool stopped;
        internal ClientGameScopeService(Func<object> game, Func<bool> isMainThread)
        { this.game=game??throw new ArgumentNullException(nameof(game)); this.isMainThread=isMainThread??throw new ArgumentNullException(nameof(isMainThread)); }
        public IClientGameScope Capture() => Volatile.Read(ref current);
        internal void Publish()
        {
            MainThread();
            if(stopped) return;
            object value=game();
            var previous=Volatile.Read(ref current);
            if(previous!=null && ReferenceEquals(previous.Game,value) && previous.IsCurrent()) return;
            previous?.Invalidate();
            Volatile.Write(ref current,value==null?null:new Scope(this,value));
        }
        internal void Stop()
        {
            MainThread(); stopped=true;
            Interlocked.Exchange(ref current,null)?.Invalidate();
        }
        private void MainThread()
        { if(!isMainThread()) throw new InvalidOperationException("Game scope validation requires the main thread."); }
        private sealed class Scope : IClientGameScope
        {
            private readonly ClientGameScopeService owner;
            internal readonly object Game;
            private bool invalid;
            internal Scope(ClientGameScopeService owner,object game) { this.owner=owner; Game=game; }
            internal void Invalidate() { invalid=true; }
            public bool IsCurrent()
            {
                owner.MainThread();
                // Also check the live reference: a transition may happen before the next Publish.
                if(!invalid && (!ReferenceEquals(this,Volatile.Read(ref owner.current)) || !ReferenceEquals(Game,owner.game()))) invalid=true;
                return !invalid;
            }
        }
    }
}
