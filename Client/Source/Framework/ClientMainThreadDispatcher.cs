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
                    Verse.Log.Error($"[Phinix] ClientMainThreadDispatcher queue overflow ({MaxPendingActions}), dropping oldest action. Background producers may be outpacing the main thread.");
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

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    // 设计哲学 §3.5：单个后台动作异常不得中断整条队列（否则剩余动作堆积 → 溢出）。
                    // 记录后可观测，继续消费后续动作。
                    Verse.Log.Error($"[Phinix] Main-thread action threw: {ex}");
                }
            }
        }
    }
}
