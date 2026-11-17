using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace PhinixClient.Storage
{
    // No database, game API, SQL text or parameter values are used to report a failure.
    internal sealed class DatabaseDiagnostics
    {
        private readonly Action<string,Exception> report;
        [ThreadStatic] private static bool tracing;
        [ThreadStatic] private static HashSet<DatabaseDiagnostics> active;
        internal DatabaseDiagnostics(Action<string,Exception> report) { this.report=report; }
        internal void Report(string operation,Exception error)
        {
            var current=active??(active=new HashSet<DatabaseDiagnostics>());
            if(current.Count>=8 || !current.Add(this)){Trace(operation,error);return;}
            try {
                if(report!=null)try {report(operation,error);return;}catch(Exception sink){Trace("DatabaseDiagnosticObserverFailed",sink);}
                Trace(operation,error);
            }finally{current.Remove(this);}
        }
        private static void Trace(string operation,Exception error)
        {
            if(tracing)return;tracing=true;
            try {
                var text=new StringBuilder("[Phinix database] "+operation+"; messages omitted\n");
                var queue=new Queue<Exception>();queue.Enqueue(error);int remaining=64;
                while(queue.Count>0 && remaining-->0 && text.Length<65500){
                    var current=queue.Dequeue();text.AppendLine(current.GetType().FullName);text.AppendLine(new StackTrace(current,false).ToString());
                    if(current is AggregateException aggregate){foreach(var cause in aggregate.InnerExceptions)if(queue.Count<64)queue.Enqueue(cause);}
                    else if(current.InnerException!=null)queue.Enqueue(current.InnerException);
                }
                string rendered=text.ToString(0,Math.Min(text.Length,65500));
                if(queue.Count>0 || text.Length>65500)rendered+="\n[diagnostic size limit reached]";
                foreach(TraceListener listener in System.Diagnostics.Trace.Listeners)try{listener.TraceEvent(new TraceEventCache(),"Phinix database",TraceEventType.Error,0,rendered);}catch{}
            }catch{}finally{tracing=false;}
        }
    }
}
