using System;
using PhinixClient.Framework;

namespace Phinix.InventoryExtension
{
    public static class InventoryDiagnostics
    {
        public static readonly ClientModuleDiagnostics Scope = new ClientModuleDiagnostics();
        public static void Report(string operation,Exception error,bool notify=false) => Scope.Report(operation,error,notify);
        public static void Cleanup(string operation,Action action) => Scope.Cleanup(operation,action);
    }
}
