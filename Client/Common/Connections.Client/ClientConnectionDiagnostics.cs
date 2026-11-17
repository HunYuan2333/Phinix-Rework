using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Connections
{
    // Transport/authentication boundaries may contain credentials in exception messages.
    // The Host callback receives the original Exception and applies its sensitive policy.
    public sealed class ClientConnectionDiagnostics
    {
        private readonly Action<string, Exception> report;
        [ThreadStatic] private static HashSet<ClientConnectionDiagnostics> reporting;
        [ThreadStatic] private static bool tracing;
        public ClientConnectionDiagnostics(Action<string, Exception> report) { this.report = report; }

        public void Report(string operation, Exception error)
        {
            if (error == null) return;
            var active = reporting ?? (reporting = new HashSet<ClientConnectionDiagnostics>());
            if (active.Count >= 8 || !active.Add(this)) { Fallback(operation, error); return; }
            try
            {
                if (report == null) { Fallback(operation, error); return; }
                try { report(operation, error); }
                catch (Exception sinkError) { Fallback(operation, error); Fallback("ConnectionDiagnosticObserverFailed", sinkError); }
            }
            finally { active.Remove(this); }
        }

        private static void Fallback(string operation, Exception error)
        {
            // Deliberately omit arbitrary messages and source paths, while retaining causes/frames.
            // Diagnostics must also work without a Host, and must never replace a business failure.
            if (tracing) return;
            tracing = true;
            try
            {
                var text = new StringBuilder("[Phinix connection] " + operation + "; messages omitted at sensitive boundary\n");
                var pending = new Queue<Exception>(); pending.Enqueue(error);
                int remaining = 64;
                while (pending.Count > 0 && remaining-- > 0 && text.Length < 65536)
                {
                    var current = pending.Dequeue();
                    text.AppendLine(current.GetType().FullName);
                    text.AppendLine(new StackTrace(current, false).ToString());
                    if (current is AggregateException aggregate)
                    {
                        foreach (var cause in aggregate.InnerExceptions) if (cause != null && pending.Count < 64) pending.Enqueue(cause);
                    }
                    else if (current.InnerException != null) pending.Enqueue(current.InnerException);
                    if (current is ReflectionTypeLoadException types && types.LoaderExceptions != null)
                        foreach (var cause in types.LoaderExceptions) if (cause != null && pending.Count < 64) pending.Enqueue(cause);
                }
                bool limited = pending.Count > 0 || text.Length > 65500;
                string rendered = text.ToString(0, Math.Min(text.Length, 65500));
                if (limited) rendered += "\n[diagnostic size limit reached]";
                // Trace.TraceError is conditional on TRACE and may be removed by a caller's
                // build profile. Invoke listeners directly so Release diagnostics still exist.
                foreach (TraceListener listener in Trace.Listeners)
                    try { listener.TraceEvent(new TraceEventCache(), "Phinix connection", TraceEventType.Error, 0, rendered); listener.Flush(); }
                    catch { /* Continue to other listeners; keep the primary exception. */ }
            }
            catch { /* Last-resort Trace listeners cannot break transport shutdown or authentication. */ }
            finally { tracing = false; }
        }
    }
}
