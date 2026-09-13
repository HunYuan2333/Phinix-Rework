using System;
using System.Collections.Generic;
using Utils;

namespace PhinixClient.Framework
{
    internal sealed class ClientMainThreadDispatcher : IClientMainThreadDispatcher, IClientDispatcherDiagnostics
    {
        private const int MaxPendingActions = 500;
        private const int MaxActionsPerFrame = 100;
        private readonly Queue<Action> pendingActions = new Queue<Action>();
        private readonly object syncRoot = new object();
        private readonly Action<string, LogLevel> log;
        private long droppedCount;

        public ClientMainThreadDispatcher(Action<string, LogLevel> log = null)
        {
            this.log = log;
        }

        public int PendingCount
        {
            get { lock (syncRoot) return pendingActions.Count; }
        }

        public long DroppedCount
        {
            get { lock (syncRoot) return droppedCount; }
        }

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
                    droppedCount++;
                    log?.Invoke($"[Phinix] ClientMainThreadDispatcher queue overflow ({MaxPendingActions}), dropping oldest action. Background producers may be outpacing the main thread.", LogLevel.WARNING);
                }
                pendingActions.Enqueue(action);
            }
        }

        public void DrainPendingActions()
        {
            int processed = 0;
            while (processed < MaxActionsPerFrame)
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
                    log?.Invoke($"[Phinix] Main-thread action threw: {ex}", LogLevel.ERROR);
                }
                processed++;
            }
        }
    }
}
