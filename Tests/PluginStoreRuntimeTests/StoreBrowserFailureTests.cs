using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void StoreBrowserFailureRegression()
    {
        var logs=new List<string>();
        using(var browser=new StoreBrowserController(repositoryLog:logs.Add))
        {
            browser.ReadIndex(token=>{throw new IOException("SECRET /private/path");},"test.source").GetAwaiter().GetResult();
            Assert(browser.Snapshot.State==StoreBrowserState.Failed && browser.Snapshot.ErrorCode=="InstallationStorageFailed","Legacy browser uses the common filesystem error classification.");
            Assert(!browser.Snapshot.Error.Contains("SECRET") && !browser.Snapshot.Diagnostic.Contains("SECRET"),"Legacy browser no longer publishes exception messages/stacks.");
            Assert(logs.Any(l=>l.Contains("repository.task_failed") && l.Contains("ReadingIndex")),"Browser errors are audited even without an endpoint-specific audit.");
        }
    }
}
