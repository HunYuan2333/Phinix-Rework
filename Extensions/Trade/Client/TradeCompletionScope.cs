using System;

namespace Phinix.TradeExtension.Client
{
    // Live game-instance fence, deliberately not a persistent save identity.
    internal sealed class TradeCompletionScope
    {
        private readonly Func<object> game;
        private readonly Func<bool> capturedGameIsCurrent;
        private readonly Func<string> recipient,endpoint;
        private readonly object originGame;
        private readonly string originRecipient,originEndpoint;
        private bool invalid;
        private readonly bool requiresGame;
        public TradeCompletionScope(string owner,string server,Func<object> game,Func<string> recipient,Func<string> endpoint,bool requiresGame=true)
        {
            this.game=game;this.recipient=recipient;this.endpoint=endpoint;
            originGame=game();originRecipient=owner;originEndpoint=server;
            this.requiresGame=requiresGame;
            invalid=(requiresGame && originGame==null) || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(server);
        }
        public TradeCompletionScope(string owner,string server,Func<bool> capturedGameIsCurrent,Func<string> recipient,Func<string> endpoint,bool requiresGame)
        {
            this.capturedGameIsCurrent=capturedGameIsCurrent;
            this.recipient=recipient;this.endpoint=endpoint;
            originRecipient=owner;originEndpoint=server;this.requiresGame=requiresGame;
            invalid=string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(server) || (requiresGame && capturedGameIsCurrent==null);
        }
        public bool IsValid()
        {
            if(!invalid && ((requiresGame && (capturedGameIsCurrent!=null ? !capturedGameIsCurrent() : !ReferenceEquals(originGame,game()))) || recipient()!=originRecipient || endpoint()!=originEndpoint)) invalid=true;
            return !invalid;
        }
    }
}
