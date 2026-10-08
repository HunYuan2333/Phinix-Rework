using System;
using System.Collections.Generic;
using System.Threading;
using PhinixClient.Framework;

internal static partial class Program
{
    private static void AssertClientLinkOpening()
    {
        int mainThread=Thread.CurrentThread.ManagedThreadId, probes=0, overlay=0, external=0;
        bool available=true;
        var messages=new List<string>();
        ClientLinkService links=null;
        var initializer=new Thread(()=>links=new ClientLinkService(
            ()=>Thread.CurrentThread.ManagedThreadId==mainThread,
            ()=> { probes++; return available; },url=>overlay++,url=>external++,messages.Add));
        initializer.Start(); Assert(initializer.Join(5000),"Link service construction completes on the loading thread.");
        Assert(probes==0 && overlay==0 && external==0,"Construction never probes Steam or opens a browser.");
        const string url="https://steamcommunity.com/sharedfiles/filedetails/?id=3735269431";
        Assert(links.Open(url)==ClientLinkOpenResult.GameBrowserRequested && overlay==1 && external==0,
            "An available overlay receives one request without also opening the external browser.");
        available=false;
        Assert(links.Open(url)==ClientLinkOpenResult.ExternalBrowserRequested && overlay==1 && external==1,
            "Non-Steam or disabled overlay falls back to the external browser.");
        int previousProbes=probes;
        available=true;
        Assert(links.Open(url,ClientLinkOpenPreference.ExternalBrowser)==ClientLinkOpenResult.ExternalBrowserRequested && probes==previousProbes,
            "Explicit external preference skips the Steam probe.");
        int previousExternal=external;
        Exception workerFailure=null;
        var worker=new Thread(()=> { try { links.Open(url); } catch(Exception ex) { workerFailure=ex; } });
        worker.Start(); Assert(worker.Join(5000),"Worker rejection completes promptly.");
        Assert(workerFailure is InvalidOperationException && probes==previousProbes && external==previousExternal,
            "Wrong-thread links are rejected before touching browser APIs.");
        foreach(string invalid in new[]{null,"","relative/path","file:///tmp/test","javascript:alert(1)","steam://url/test",
            "https://user:secret@example.com/","https://example.com/\nextra",new string('x',4097)})
        {
            bool rejected=false;
            try { links.Open(invalid); } catch(ArgumentException) { rejected=true; }
            Assert(rejected && probes==previousProbes && external==previousExternal,"Invalid links do not reach native APIs.");
        }
        bool badPreference=false;
        try { links.Open(url,(ClientLinkOpenPreference)99); } catch(ArgumentOutOfRangeException) { badPreference=true; }
        Assert(badPreference && probes==previousProbes,"Unknown browser preferences are rejected.");
        foreach(bool probeFailure in new[]{true,false})
        {
            int fallbacks=0;
            var broken=new ClientLinkService(()=>true,()=> {
                if(probeFailure) throw new DllNotFoundException("private-token"); return true;
            },_=> { throw new InvalidOperationException("private-token"); },_=>fallbacks++,messages.Add);
            Assert(broken.Open("https://example.com/private-token?token=private-token")==ClientLinkOpenResult.ExternalBrowserRequested && fallbacks==1,
                "Probe and native overlay failures both fall back once.");
        }
        Assert(messages.TrueForAll(message=>!message.Contains("private-token") && !message.Contains("?id=")),
            "Link diagnostics exclude paths, queries and native exception messages.");
        int successfulRequests=0, unexpectedFallbacks=0;
        var badLogger=new ClientLinkService(()=>true,()=>true,_=>successfulRequests++,_=>unexpectedFallbacks++,
            _=> { throw new Exception("Logger unavailable."); });
        Assert(badLogger.Open(url)==ClientLinkOpenResult.GameBrowserRequested && successfulRequests==1 && unexpectedFallbacks==0,
            "A logger failure cannot duplicate a successful browser request.");
        var unavailable=new ClientLinkService(()=>true,()=>false,_=> { throw new Exception(); },_=> { throw new Exception(); },messages.Add);
        Assert(unavailable.Open(url)==ClientLinkOpenResult.Unavailable,"External launch failure returns an actionable result.");
    }
}
