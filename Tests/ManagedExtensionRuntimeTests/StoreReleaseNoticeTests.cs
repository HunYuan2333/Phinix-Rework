using System;
using System.Collections.Generic;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void StoreReleaseNoticeRegression()
    {
        bool persisted=false; int shown=0,writes=0;
        var queued=new List<Action>();
        Func<StoreReleaseNotice> create=()=>new StoreReleaseNotice(()=>persisted,()=> { writes++; persisted=true; },queued.Add,()=>shown++,null);
        var first=create(); first.Start(); first.Start();
        Assert(queued.Count==1 && shown==0 && !persisted,"Release notices are queued once rather than opening during activation.");
        queued[0]();
        Assert(shown==1 && writes==1 && persisted,"A successfully opened release notice saves its player-wide marker.");
        var restarted=create(); restarted.Start();
        Assert(queued.Count==1 && shown==1,"Restarting or changing saves does not repeat the release announcement.");
        persisted=false; queued.Clear();
        var stopped=create(); stopped.Start(); stopped.Stop(); queued[0]();
        Assert(shown==1 && !persisted,"A callback from a stopped module neither opens a window nor acknowledges it.");
        stopped.Start(); queued[0]();
        Assert(shown==1 && !persisted,"Reactivation cannot revive an older queued callback.");
        queued[1](); Assert(shown==2 && persisted,"The current activation can still show the notice.");
        queued.Clear(); persisted=false;
        var alreadyAcknowledged=create(); alreadyAcknowledged.Start(); persisted=true; queued[0]();
        Assert(shown==2,"A marker saved before the queued callback runs prevents duplicate notices.");
        queued.Clear(); persisted=false;
        var messages=new List<string>();
        var failedOpen=new StoreReleaseNotice(()=>false,()=>persisted=true,queued.Add,
            ()=> { throw new InvalidOperationException("private detail"); },messages.Add);
        failedOpen.Start(); queued[0]();
        Assert(!persisted && messages.Count==1 && !messages[0].Contains("private detail"),"Failed window creation leaves the notice unacknowledged and reports only an error type.");
        queued.Clear();
        var failedWrite=new StoreReleaseNotice(()=>false,()=> { throw new Exception(); },queued.Add,()=>shown++,
            _=> { throw new Exception("Logger failure"); });
        failedWrite.Start(); queued[0]();
        Assert(shown==3,"Persistence/logging failures do not abort extension startup or duplicate the window.");
    }
}
