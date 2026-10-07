using System;
using Stateless;

namespace Phinix.PluginStore
{
    internal enum ManagedStoreState { Idle, Reading, Ready, Planning, PlanReady, Downloading, Verified, Installing, Installed, Managing, Failed, Canceled, Stopped }

    // Pure transitions only. The controller serializes all calls under its gate,
    // owns cancellation/work, and publishes snapshots after validating the generation.
    internal sealed class ManagedStoreOperation
    {
        private enum Trigger { Read, Plan, Download, Install, Manage, Ready, Idle, PlanReady, Verified, Installed, Fail, Cancel, CancelRequested, RepositoryChanged, Stop }
        private readonly StateMachine<ManagedStoreState, Trigger> machine;
        private long generation;
        private bool cancellationRequested;

        internal ManagedStoreOperation()
        {
            machine = new StateMachine<ManagedStoreState, Trigger>(ManagedStoreState.Idle, FiringMode.Immediate);
            foreach (ManagedStoreState state in Enum.GetValues(typeof(ManagedStoreState)))
            {
                var configuration = machine.Configure(state);
                if (state == ManagedStoreState.Stopped)
                {
                    configuration.Ignore(Trigger.Stop);
                    continue;
                }
                configuration.Permit(Trigger.Stop, ManagedStoreState.Stopped);
                if (IsBusy(state))
                {
                    configuration.Permit(Trigger.Fail, ManagedStoreState.Failed)
                        .Permit(Trigger.Cancel, ManagedStoreState.Canceled)
                        .InternalTransition(Trigger.CancelRequested, _ => cancellationRequested = true);
                }
                else
                {
                    configuration.Permit(Trigger.Read, ManagedStoreState.Reading)
                        .Permit(Trigger.Plan, ManagedStoreState.Planning)
                        .Permit(Trigger.Download, ManagedStoreState.Downloading)
                        .Permit(Trigger.Install, ManagedStoreState.Installing)
                        .Permit(Trigger.Manage, ManagedStoreState.Managing);
                    if (state == ManagedStoreState.Idle) configuration.Ignore(Trigger.RepositoryChanged);
                    else configuration.Permit(Trigger.RepositoryChanged, ManagedStoreState.Idle);
                }
            }
            machine.Configure(ManagedStoreState.Reading).Permit(Trigger.Ready, ManagedStoreState.Ready).Permit(Trigger.Idle, ManagedStoreState.Idle);
            machine.Configure(ManagedStoreState.Planning).Permit(Trigger.PlanReady, ManagedStoreState.PlanReady);
            machine.Configure(ManagedStoreState.Downloading).Permit(Trigger.Verified, ManagedStoreState.Verified);
            machine.Configure(ManagedStoreState.Installing).Permit(Trigger.Installed, ManagedStoreState.Installed);
            machine.Configure(ManagedStoreState.Managing).Permit(Trigger.Ready, ManagedStoreState.Ready);
        }

        internal ManagedStoreState State => machine.State;
        internal bool Busy => IsBusy(State);
        internal bool CancellationRequested => cancellationRequested;
        internal long Generation => generation;
        internal static bool IsBusy(ManagedStoreState state)
        {
            return state == ManagedStoreState.Reading || state == ManagedStoreState.Planning
                || state == ManagedStoreState.Downloading || state == ManagedStoreState.Installing || state == ManagedStoreState.Managing;
        }
        internal long Begin(ManagedStoreState state)
        {
            if (State == ManagedStoreState.Stopped) throw new ObjectDisposedException(nameof(ManagedStoreOperation));
            if (Busy) throw new InvalidOperationException("A store operation is already running.");
            long next = checked(generation + 1);
            machine.Fire(StartTrigger(state));
            generation = next;
            cancellationRequested = false;
            return generation;
        }
        internal bool IsCurrent(long expected) => Busy && generation == expected;
        internal bool AcceptsProgress(long expected) => IsCurrent(expected) && !cancellationRequested;
        internal bool RequestCancel(long expected)
        {
            if (!IsCurrent(expected)) return false;
            machine.Fire(Trigger.CancelRequested);
            return true;
        }
        internal bool Complete(long expected, ManagedStoreState outcome)
        {
            if (!IsCurrent(expected)) return false;
            // Unconfigured outcomes throw before changing state: no optimistic success.
            machine.Fire(CompletionTrigger(outcome));
            return true;
        }
        internal void ResetRepository() { machine.Fire(Trigger.RepositoryChanged); }
        internal void Stop() { machine.Fire(Trigger.Stop); }

        private static Trigger StartTrigger(ManagedStoreState state)
        {
            switch (state)
            {
                case ManagedStoreState.Reading: return Trigger.Read;
                case ManagedStoreState.Planning: return Trigger.Plan;
                case ManagedStoreState.Downloading: return Trigger.Download;
                case ManagedStoreState.Installing: return Trigger.Install;
                case ManagedStoreState.Managing: return Trigger.Manage;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }
        private static Trigger CompletionTrigger(ManagedStoreState state)
        {
            switch (state)
            {
                case ManagedStoreState.Ready: return Trigger.Ready;
                case ManagedStoreState.Idle: return Trigger.Idle;
                case ManagedStoreState.PlanReady: return Trigger.PlanReady;
                case ManagedStoreState.Verified: return Trigger.Verified;
                case ManagedStoreState.Installed: return Trigger.Installed;
                case ManagedStoreState.Failed: return Trigger.Fail;
                case ManagedStoreState.Canceled: return Trigger.Cancel;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }
    }
}
