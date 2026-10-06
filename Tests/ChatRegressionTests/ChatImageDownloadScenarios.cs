using System;
using System.Collections.Generic;
using System.Threading;
using Phinix.ChatExtension.Client;

internal static class ChatImageDownloadScenarios
{
    private sealed class Attempt
    {
        public string Url;
        public int Timeout;
        public Action<string, ChatImageFailure> Complete;
        public bool Cancelled;
    }

    private sealed class Transport
    {
        public readonly List<Attempt> Attempts = new List<Attempt>();
        public Action Start(string url, int timeout, Action<string, ChatImageFailure> complete)
        {
            Attempt attempt = new Attempt { Url = url, Timeout = timeout, Complete = complete };
            Attempts.Add(attempt);
            return () => attempt.Cancelled = true;
        }
    }

    public static void Run()
    {
        ConcurrencyAndDuplicateUrlsAreBounded();
        TransientFailuresBackOffAndStop();
        RetrySuccessIgnoresStaleAttempts();
        PermanentFailuresAndManualRetriesAreDistinct();
        CancellationRejectsLateCompletions();
        WorkerCompletionsAndCallbackFailuresAreIsolated();
        QueueCapacityAndStartFailuresAreBounded();
    }

    private static void ConcurrencyAndDuplicateUrlsAreBounded()
    {
        Transport transport = new Transport();
        var queue = new ChatImageDownloadQueue<string>(transport.Start, null);
        int delivered = 0;
        queue.Request("a", (_, __) => delivered++);
        queue.Request("a", (_, __) => delivered++);
        queue.Request("b", (_, __) => { });
        queue.Request("c", (_, __) => { });
        queue.Pump(0);
        Check(transport.Attempts.Count == 2, "two active requests and URL deduplication");
        transport.Attempts[0].Complete("texture", ChatImageFailure.None);
        queue.Pump(1);
        Check(delivered == 2 && transport.Attempts.Count == 3, "all subscribers delivered and slot released");
        queue.CancelAll();
    }

    private static void TransientFailuresBackOffAndStop()
    {
        Transport transport = new Transport();
        var queue = new ChatImageDownloadQueue<string>(transport.Start, null);
        int failures = 0;
        queue.Request("slow", (_, failure) => { Check(failure == ChatImageFailure.Transient, "timeout stays network failure"); failures++; });
        queue.Pump(0);
        transport.Attempts[0].Complete(null, ChatImageFailure.Transient);
        queue.Pump(1);
        queue.Pump(2);
        Check(transport.Attempts.Count == 1 && failures == 0, "first retry waits two seconds");
        queue.Pump(3);
        transport.Attempts[1].Complete(null, ChatImageFailure.Transient);
        queue.Pump(4);
        queue.Pump(11);
        Check(transport.Attempts.Count == 2, "second retry waits eight seconds");
        queue.Pump(12);
        transport.Attempts[2].Complete(null, ChatImageFailure.Transient);
        queue.Pump(13);
        queue.Pump(1000);
        Check(transport.Attempts.Count == 3 && failures == 1, "exactly three attempts, one terminal failure");
        Check(transport.Attempts[0].Timeout == 30 && transport.Attempts[1].Timeout == 60 &&
            transport.Attempts[2].Timeout == 90, "slow transfers receive bounded longer timeouts");
    }

    private static void PermanentFailuresAndManualRetriesAreDistinct()
    {
        Check(ChatImageDownloadQueue<string>.ClassifyFailure(false, true, 200) == ChatImageFailure.Unsupported, "decode failure classification");
        foreach (long status in new long[] { 408, 429, 500, 502, 503, 504 })
            Check(ChatImageDownloadQueue<string>.ClassifyFailure(false, false, status) == ChatImageFailure.Transient, "temporary HTTP status retry");
        foreach (long status in new long[] { 400, 403, 404 })
            Check(ChatImageDownloadQueue<string>.ClassifyFailure(false, false, status) == ChatImageFailure.Permanent, "permanent HTTP status no automatic retry");
        Transport transport = new Transport();
        var queue = new ChatImageDownloadQueue<string>(transport.Start, null);
        string value = null;
        queue.Request("image", (_, __) => { });
        queue.Pump(0);
        transport.Attempts[0].Complete(null, ChatImageFailure.Unsupported);
        queue.Pump(1);
        queue.Pump(100);
        Check(transport.Attempts.Count == 1, "unsupported formats do not loop");
        queue.Request("image", (texture, _) => value = texture);
        queue.Pump(101);
        transport.Attempts[1].Complete("recovered", ChatImageFailure.None);
        queue.Pump(102);
        Check(value == "recovered", "explicit new request can recover after failure");
    }

    private static void RetrySuccessIgnoresStaleAttempts()
    {
        Transport transport = new Transport();
        var queue = new ChatImageDownloadQueue<string>(transport.Start, _ => { throw new InvalidOperationException("logger failed"); });
        int delivered = 0;
        queue.Request("retry", (_, __) => { throw new InvalidOperationException("subscriber failed"); });
        queue.Request("retry", (value, failure) => { Check(value == "recovered" && failure == ChatImageFailure.None, "retry success"); delivered++; });
        queue.Pump(0);
        transport.Attempts[0].Complete(null, ChatImageFailure.Transient);
        queue.Pump(1);
        transport.Attempts[0].Complete(null, ChatImageFailure.Transient);
        queue.Pump(2);
        queue.Pump(3);
        transport.Attempts[0].Complete(null, ChatImageFailure.Transient);
        transport.Attempts[1].Complete("recovered", ChatImageFailure.None);
        queue.Pump(4);
        queue.Pump(1000);
        Check(delivered == 1 && transport.Attempts.Count == 2, "late attempt and logger errors cannot consume retry or repeat delivery");
    }

    private static void CancellationRejectsLateCompletions()
    {
        Transport transport = new Transport();
        var queue = new ChatImageDownloadQueue<string>(transport.Start, null);
        int delivered = 0;
        queue.Request("same", (_, __) => delivered++);
        queue.Request("other", (_, __) => delivered++);
        queue.Request("queued", (_, __) => delivered++);
        queue.Pump(0);
        queue.CancelAll();
        Check(transport.Attempts.TrueForAll(a => a.Cancelled), "active transports cancelled");
        queue.Request("same", (_, __) => delivered++);
        transport.Attempts[0].Complete("stale", ChatImageFailure.None);
        queue.Pump(1);
        Check(delivered == 0 && transport.Attempts.Count == 3, "late result cannot affect new request or start abandoned queue");
        transport.Attempts[2].Complete("new", ChatImageFailure.None);
        queue.Pump(2);
        Check(delivered == 1, "fresh result after cancellation delivered once");
    }

    private static void WorkerCompletionsAndCallbackFailuresAreIsolated()
    {
        Transport transport = new Transport();
        int logs = 0, delivered = 0;
        var queue = new ChatImageDownloadQueue<string>(transport.Start, _ => logs++);
        queue.Request("shared", (_, __) => { throw new InvalidOperationException("subscriber failure"); });
        queue.Request("shared", (_, __) => delivered++);
        queue.Pump(0);
        Thread worker = new Thread(() => transport.Attempts[0].Complete("texture", ChatImageFailure.None));
        worker.Start(); worker.Join();
        Check(delivered == 0 && logs == 0, "worker result does not mutate UI subscribers");
        queue.Pump(1);
        Check(delivered == 1 && logs == 1, "subscriber exception cannot block other subscribers");
    }

    private static void QueueCapacityAndStartFailuresAreBounded()
    {
        int starts = 0, failures = 0;
        var queue = new ChatImageDownloadQueue<string>((url, timeout, done) => { starts++; throw new InvalidOperationException("start failed"); }, null);
        for (int i = 0; i < 64; i++) Check(queue.Request("url-" + i, (_, __) => failures++), "bounded queue accepts capacity");
        Check(!queue.Request("overflow", (_, __) => { }), "bounded queue rejects overflow");
        queue.Pump(0);
        Check(starts == 2, "start exceptions cannot bypass concurrency budget");
        queue.CancelAll();
        queue.Pump(1000);
        Check(failures == 0 && starts == 2, "cancelled failures cannot restart queue");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Image assertion failed: " + message);
    }
}
