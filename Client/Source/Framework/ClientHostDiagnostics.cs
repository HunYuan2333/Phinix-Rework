using System;
using Connections;

namespace PhinixClient
{
    internal static class ClientHostDiagnostics
    {
        internal static void Report(Exception error, string operation, bool notify = true)
        {
            var host = Client.Instance;
            Action<string,Exception> report=host==null?(Action<string,Exception>)null:(name,cause)=>host.ReportHostFailure(cause,name,notify);
            new ClientConnectionDiagnostics(report).Report(operation,error);
        }
        internal static void Cleanup(Action cleanup, string operation)
        { try { cleanup(); } catch (Exception error) { Report(error, operation, false); } }
    }
}
