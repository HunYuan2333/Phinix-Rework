using System;
using PhinixClient.Framework;

namespace Phinix.ChatExtension.Client
{
    internal static class ChatDiagnostics
    {
        internal static readonly ClientModuleDiagnostics Scope = new ClientModuleDiagnostics();
        internal static void Report(string operation,Exception error,bool notify=false) => Scope.Report(operation,error,notify);
        internal static void Cleanup(string operation,Action action) => Scope.Cleanup(operation,action);
    }
}
