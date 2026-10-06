using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Phinix.ChatExtension.Client
{
    internal enum ChatImageFailure { None, Transient, Permanent, Unsupported }

    /// <summary>Main-thread pump; transport completions may arrive on any thread.</summary>
    internal sealed class ChatImageDownloadQueue<T> where T : class
    {
        internal const int MaximumConcurrentRequests = 2;
        internal const int MaximumAttempts = 3;
        private const int MaximumPendingRequests = 64;
        private readonly Func<string, int, Action<T, ChatImageFailure>, Action> start;
        private readonly Action<Exception> log;
        private readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>(StringComparer.Ordinal);
        private readonly List<Job> waiting = new List<Job>();
        private readonly ConcurrentQueue<Completion> completions = new ConcurrentQueue<Completion>();
        private int active;

        private sealed class Job
        {
            public string Url;
            public int Attempts;
            public double Due;
            public Action Cancel;
            public readonly List<Action<T, ChatImageFailure>> Callbacks = new List<Action<T, ChatImageFailure>>();
        }

        private sealed class Completion
        {
            public Job Job;
            public int Attempt;
            public T Value;
            public ChatImageFailure Failure;
        }

        public ChatImageDownloadQueue(Func<string, int, Action<T, ChatImageFailure>, Action> start,
            Action<Exception> log)
        {
            this.start = start ?? throw new ArgumentNullException(nameof(start));
            this.log = log;
        }

        public bool Request(string url, Action<T, ChatImageFailure> callback)
        {
            if (jobs.TryGetValue(url, out Job existing))
            {
                existing.Callbacks.Add(callback);
                return true;
            }
            if (jobs.Count >= MaximumPendingRequests) return false;
            Job job = new Job { Url = url };
            job.Callbacks.Add(callback);
            jobs.Add(url, job);
            waiting.Add(job);
            return true;
        }

        public void Pump(double now)
        {
            while (completions.TryDequeue(out Completion completion))
            {
                Job job = completion.Job;
                if (!jobs.TryGetValue(job.Url, out Job current) || !ReferenceEquals(current, job) ||
                    completion.Attempt != job.Attempts || waiting.Contains(job)) continue;
                active--;
                job.Cancel = null;
                if (completion.Value == null && completion.Failure == ChatImageFailure.Transient &&
                    job.Attempts < MaximumAttempts)
                {
                    job.Due = now + (job.Attempts == 1 ? 2 : 8);
                    waiting.Add(job);
                    continue;
                }
                jobs.Remove(job.Url);
                foreach (Action<T, ChatImageFailure> callback in job.Callbacks)
                {
                    try { callback(completion.Value, completion.Failure); }
                    catch (Exception ex) { Report(ex); }
                }
            }

            for (int i = 0; i < waiting.Count && active < MaximumConcurrentRequests;)
            {
                Job job = waiting[i];
                if (job.Due > now) { i++; continue; }
                waiting.RemoveAt(i);
                active++;
                int attempt = ++job.Attempts;
                Action<T, ChatImageFailure> complete = (value, failure) => completions.Enqueue(
                    new Completion { Job = job, Attempt = attempt, Value = value, Failure = failure });
                try { job.Cancel = start(job.Url, TimeoutSeconds(attempt), complete); }
                catch (Exception ex)
                {
                    Report(ex);
                    complete(null, ChatImageFailure.Transient);
                }
            }
        }

        public void CancelAll()
        {
            foreach (Job job in jobs.Values)
            {
                try { job.Cancel?.Invoke(); }
                catch (Exception ex) { Report(ex); }
            }
            jobs.Clear();
            waiting.Clear();
            active = 0;
            while (completions.TryDequeue(out _)) { }
        }

        internal static int TimeoutSeconds(int attempt) => Math.Min(90, attempt * 30);

        private void Report(Exception exception)
        {
            try { log?.Invoke(exception); }
            catch (Exception) { /* A logger cannot hold a download slot or block other subscribers. */ }
        }

        internal static ChatImageFailure ClassifyFailure(bool connectionError, bool processingError, long status)
        {
            if (processingError) return ChatImageFailure.Unsupported;
            if (connectionError || status == 408 || status == 429 || status == 500 || status == 502 ||
                status == 503 || status == 504) return ChatImageFailure.Transient;
            return ChatImageFailure.Permanent;
        }
    }
}
