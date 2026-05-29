using System;
using System.Collections.Generic;

namespace PhinixClient.Framework
{
    internal sealed class ClientMainThreadDispatcher : IClientMainThreadDispatcher
    {
        private const int MaxPendingActions = 500;
        private readonly Queue<Action> pendingActions = new Queue<Action>();
        private readonly object syncRoot = new object();

        public void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (syncRoot)
            {
                if (pendingActions.Count >= MaxPendingActions)
                {
                    pendingActions.Dequeue(); // drop oldest
                }
                pendingActions.Enqueue(action);
            }
        }

        public void DrainPendingActions()
        {
            while (true)
            {
                Action action;
                lock (syncRoot)
                {
                    if (pendingActions.Count == 0)
                    {
                        return;
                    }

                    action = pendingActions.Dequeue();
                }

                action?.Invoke();
            }
        }
    }
}
