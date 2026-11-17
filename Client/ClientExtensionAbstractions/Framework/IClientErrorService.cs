using System;
using Utils.Framework;

namespace PhinixClient.Framework
{
    /// <summary>Borrowed Host service. Bind the real module during Activate; no game types or business recovery.</summary>
    public interface IClientErrorService
    {
        IClientErrorReporter ForModule(IPhinixExtensionModule module);
    }

    public interface IClientErrorReporter
    {
        // Thread-safe after binding. Records a frozen diagnostic, never retries or rethrows.
        // Returns an error ID, or null if stopped/unavailable. Reporting must not throw.
        // Use stable codes/operation names, not payloads, credentials or player content.
        // notify=false retains diagnostics without requesting a player popup.
        string Report(Exception error, string code, string operation, bool notify = true);
    }
    // Optional capability: old reporter implementations remain binary/source compatible.
    public interface IClientSensitiveErrorReporter : IClientErrorReporter
    {
        string ReportSensitive(Exception error,string code,string operation,bool notify=true);
    }

    public static class ClientErrorReporting
    {
        public static string ReportSensitive(this IClientErrorReporter reporter,Exception error,string code,string operation,bool notify=true)
        {
            if(error==null)return null;
            if(reporter is IClientSensitiveErrorReporter sensitive)
                try{return sensitive.ReportSensitive(error,code,operation,notify);}
                catch(Exception observer){ExtensionDiagnostics.TraceSensitive("ClientErrorReporterFailed",observer);}
            // An older custom reporter cannot promise message privacy. Keep original
            // types/causes/frames via the neutral fallback without exposing payload text.
            ExtensionDiagnostics.TraceSensitive(operation,error);return null;
        }
        public static void DeliverSensitive(Action<string,Exception> sink,string operation,Exception error)
        {
            if(error==null)return;
            if(sink!=null)try{sink(operation,error);return;}catch(Exception observer){ExtensionDiagnostics.TraceSensitive("ClientErrorObserverFailed",observer);}
            ExtensionDiagnostics.TraceSensitive(operation,error);
        }
    }
    /// <summary>Module-owned diagnostic binding. No retry, game work or operation result changes.</summary>
    public sealed class ClientModuleDiagnostics
    {
        [ThreadStatic] private static int reportDepth;
        private IClientErrorReporter reporter;
        public void Bind(IClientErrorReporter value)
        {System.Threading.Volatile.Write(ref reporter,value);}
        public void Report(string operation,Exception error,bool notify=false)
        {
            if(error==null)return;
            if(reportDepth>=8){ExtensionDiagnostics.TraceSensitive(operation,error);return;}
            reportDepth++;
            try{System.Threading.Volatile.Read(ref reporter).ReportSensitive(error,"ModuleBoundaryFailed",operation,notify);}
            finally{reportDepth--;}
        }
        public void Cleanup(string operation,Action action)
        {try{action?.Invoke();}catch(Exception error){Report(operation,error);}}
        public void Unbind(){System.Threading.Volatile.Write(ref reporter,null);}
    }

}
