using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    /// <summary>Filesystem work stays off Draw; completion is consumed by the host UI thread.</summary>
    internal sealed class ManagedExtensionManagerController : IDisposable
    {
        private sealed class Outcome
        { internal ManagedExtensionManagementSnapshot Snapshot; internal ManagedExtensionStateChangeResult Change; internal string Code; internal Exception Error; }
        private readonly Action<Exception> internalError;
        private Task<Outcome> task;
        private CancellationTokenSource cancellation;
        private bool disposed;
        public ManagedExtensionManagerController(Action<Exception> internalError = null) { this.internalError=internalError; }
        public ManagedExtensionManagementSnapshot Snapshot { get; private set; }
        public bool Busy => task!=null;
        public int Version { get; private set; }
        public string MessageCode { get; private set; }
        public ManagedExtensionStateChangeResult LastChange { get; private set; }
        public void Refresh(IManagedExtensionManagementService service, IEnumerable<string> disabled)
        { Start(service,null,ManagedExtensionDesiredState.Unknown,disabled); }
        public void Change(IManagedExtensionManagementService service, ManagedExtensionPackageSnapshot expected, ManagedExtensionDesiredState desired, IEnumerable<string> disabled)
        { if(expected==null) throw new ArgumentNullException(nameof(expected)); Start(service,expected,desired,disabled); }
        private void Start(IManagedExtensionManagementService service, ManagedExtensionPackageSnapshot expected, ManagedExtensionDesiredState desired, IEnumerable<string> disabled)
        {
            if(disposed) throw new ObjectDisposedException(nameof(ManagedExtensionManagerController));
            if(Busy) return;
            if(service==null) { MessageCode="ManagedManagementUnavailable"; Version++; return; }
            string[] captured=(disabled??new string[0]).ToArray();
            cancellation=new CancellationTokenSource(); var token=cancellation.Token;
            MessageCode="ManagedOperationRunning"; LastChange=null; Version++;
            task=Task.Run(()=>
            {
                var result=new Outcome();
                try
                {
                    if(expected!=null) result.Change=service.ChangeDesiredState(expected,desired,captured,token);
                    result.Snapshot=service.Refresh(captured,token);
                    result.Code=result.Change?.Code??"ManagedInventoryReady";
                }
                catch(OperationCanceledException) { result.Code=result.Change?.Succeeded==true?result.Change.Code:"ManagedOperationCanceled"; }
                catch(Exception ex)
                {
                    result.Code=result.Change?.Succeeded==true?"ManagedStateSavedRefreshFailed":"ManagedInventoryReadFailed";
                    result.Error=ex;
                }
                return result;
            });
        }
        public bool Poll()
        {
            if(task==null || !task.IsCompleted) return false;
            Outcome result=task.GetAwaiter().GetResult(); task=null; cancellation.Dispose(); cancellation=null;
            if(result.Snapshot!=null) Snapshot=result.Snapshot;
            LastChange=result.Change; MessageCode=result.Code; Version++;
            if(result.Error!=null) try { internalError?.Invoke(result.Error); } catch { }
            return true;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; cancellation?.Cancel(); cancellation?.Dispose(); cancellation=null;
            task=null; Snapshot=null; LastChange=null;
        }
    }
}
