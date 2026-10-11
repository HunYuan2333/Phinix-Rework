using System;
using System.Collections.Generic;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // One general registry for typed action contributions: context menus, toolbars and row/detail operations share the contract.
    public interface IClientExtensionPointService
    {
        IClientExtensionPointScope ForModule(IPhinixExtensionModule module);
        long Revision { get; }
        IReadOnlyList<ClientExtensionPointInfo> CapturePoints();
    }
    public interface IClientExtensionPointScope : IDisposable
    {
        IClientActionPoint<TContext> DefineActions<TContext>(string pointId) where TContext : class;
        // A missing optional point is allowed; contributions become discoverable when its owner defines the matching context.
        void ContributeActions<TContext>(string pointId, Func<TContext,IEnumerable<ClientActionDefinition>> factory, float order=0) where TContext : class;
    }
    public interface IClientActionPoint<TContext> where TContext : class
    {
        // Context is a read snapshot. The view owns this short-lived set; dispose on close/replacement.
        IClientActionSet Collect(TContext context, Func<bool> isContextCurrent=null);
    }
    public interface IClientActionSet : IDisposable
    {
        IReadOnlyList<IClientContributedAction> Actions { get; }
    }
    public interface IClientContributedAction
    {
        string OwnerId { get; }
        string Id { get; }
        string Label { get; }
        string DisabledReason { get; }
        // At most one callback invocation per collected action. Completion is not business confirmation.
        // Invoked failures may have side effects; callers must not retry blindly.
        ClientActionExecutionResult TryExecute();
    }
    public sealed class ClientActionDefinition
    {
        public ClientActionDefinition(string id,string label,Action execute,string disabledReason=null)
        { Id=id; Label=label; Execute=execute; DisabledReason=disabledReason; }
        public string Id { get; }
        public string Label { get; }
        public Action Execute { get; }
        public string DisabledReason { get; }
    }
    public sealed class ClientActionExecutionResult
    {
        public ClientActionExecutionResult(bool completed,bool invoked,string code) { Completed=completed; Invoked=invoked; Code=code; }
        public bool Completed { get; }
        public bool Invoked { get; }
        public string Code { get; }
    }
    public sealed class ClientExtensionPointInfo
    {
        public ClientExtensionPointInfo(string id,string ownerId,Type contextType,int contributorCount)
        { Id=id; OwnerId=ownerId; ContextType=contextType; ContributorCount=contributorCount; }
        public string Id { get; }
        public string OwnerId { get; }
        public Type ContextType { get; }
        public int ContributorCount { get; }
    }
}
