using System;
using Connections;
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
        { internal ManagedExtensionManagementSnapshot Snapshot; internal ManagedExtensionStateChangeResult Change; internal string Code; }
        private readonly ClientConnectionDiagnostics diagnostics;
        private Task<Outcome> task;
        private CancellationTokenSource cancellation;
        private bool disposed;
        // The reporter is thread-safe: it runs at the worker boundary, including after Dispose.
        public ManagedExtensionManagerController(Action<Exception> internalError = null)
        { diagnostics=new ClientConnectionDiagnostics(internalError == null ? (Action<string,Exception>)null : (operation,error)=>internalError(error)); }
        public ManagedExtensionManagementSnapshot Snapshot { get; private set; }
        public bool InventoryKnown { get; private set; }
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
            if(service==null) { InventoryKnown=false; MessageCode="ManagedManagementUnavailable"; Version++; return; }
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
                catch(OperationCanceledException) when(token.IsCancellationRequested) { result.Code=result.Change?.Succeeded==true?result.Change.Code:"ManagedOperationCanceled"; }
                catch(Exception ex)
                {
                    result.Code=result.Change?.Succeeded==true?"ManagedStateSavedRefreshFailed":"ManagedInventoryReadFailed";
                    diagnostics.Report(result.Code,ex);
                }
                return result;
            });
        }
        public bool Poll()
        {
            if(task==null || !task.IsCompleted) return false;
            Outcome result=task.GetAwaiter().GetResult(); task=null; cancellation.Dispose(); cancellation=null;
            if(result.Snapshot!=null) Snapshot=result.Snapshot;
            InventoryKnown=result.Snapshot!=null;
            LastChange=result.Change; MessageCode=result.Code; Version++;
            return true;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            var owned=cancellation; cancellation=null;
            task=null; Snapshot=null; LastChange=null; InventoryKnown=false;
            try { owned?.Cancel(); }
            catch(Exception error) { diagnostics.Report("ManagedOperationCancelFailed",error); }
            finally {
                try { owned?.Dispose(); }
                catch(Exception error) { diagnostics.Report("ManagedOperationCancellationCleanupFailed",error); }
            }
        }
    }
}
