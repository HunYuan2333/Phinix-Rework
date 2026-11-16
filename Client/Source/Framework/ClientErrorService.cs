using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Owns strings and weak exception identities only. Never retains Game, payloads or exceptions.
    internal sealed class ClientErrorService : IClientErrorService, IExtensionModuleLifecycleObserver
    {
        internal const int MaximumRecords = 64;
        internal const int MaximumDetailCharacters = 65536;
        private readonly object gate = new object();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly ConditionalWeakTable<Exception, Identity> identities = new ConditionalWeakTable<Exception, Identity>();
        private readonly Dictionary<IPhinixExtensionModule, Reporter> modules = new Dictionary<IPhinixExtensionModule, Reporter>(ModuleIdentity.Instance);
        private readonly Action<string> log;
        private readonly Action<string,Utils.LogLevel> levelLog;
        private readonly Func<bool> mainThread;
        private readonly string environment;
        private long sequence, evicted, sinkFailures;
        [ThreadStatic] private static bool reporting;
        private long reentrantFailures;

        internal ClientErrorService(string environment, Func<bool> mainThread, Action<string> log, Action<string,Utils.LogLevel> levelLog = null)
        {
            this.environment = ClientErrorText.Safe(environment, 2048);
            this.mainThread = mainThread ?? throw new ArgumentNullException(nameof(mainThread));
            this.log = log;
            this.levelLog = levelLog;
        }

        public IClientErrorReporter ForModule(IPhinixExtensionModule module)
        {
            RequireMainThread();
            lock (gate)
            {
                if (module == null || !modules.TryGetValue(module, out var reporter))
                    throw new InvalidOperationException("ErrorReporterModuleNotActive");
                return reporter;
            }
        }

        public void OnActivating(IPhinixExtensionModule module)
        {
            RequireMainThread();
            if (module == null) throw new ArgumentNullException(nameof(module));
            lock (gate)
            {
                if (!modules.ContainsKey(module))
                    modules.Add(module, new Reporter(this, ClientErrorText.Safe(module.ExtensionId, 160), module.GetType().Assembly.GetName().Version?.ToString() ?? "unknown", module.GetType().Assembly.GetName().Name));
            }
        }

        public void OnStopped(IPhinixExtensionModule module)
        {
            RequireMainThread();
            lock (gate)
            {
                if (module == null || !modules.TryGetValue(module, out var reporter)) return;
                reporter.Stop();
                modules.Remove(module);
            }
        }

        internal string ReportHost(Exception error, string code, string operation, bool notify = true, bool writeLog = true)
            => Report("builtin.host", "host", error, code, operation, notify, writeLog);

        internal string ReportTask(string owner, string operation, Exception error)
        {
            string version;
            lock (gate) version = modules.Values.FirstOrDefault(value => value.Owner == owner)?.Version ?? "unknown";
            return Report(owner, version, error, "CallbackFailed", "MainThreadCallback", true, true, operation);
        }

        internal string ReportBoundary(string owner, string operation, Exception error)
            => ReportOwnedBoundary(owner, operation, error, true);

        internal string ReportSensitiveBoundary(string owner, string operation, Exception error)
            => ReportSensitiveBoundary(owner, operation, error, true);

        internal string ReportSensitiveBoundary(string owner, string operation, Exception error, bool notify)
            => ReportOwnedBoundary(owner, operation, error, notify, false,
                notify ? Utils.LogLevel.ERROR : Utils.LogLevel.WARNING);

        private string ReportOwnedBoundary(string owner, string operation, Exception error, bool notify, bool includeMessages = true, Utils.LogLevel level = Utils.LogLevel.ERROR)
        {
            string version = "unknown";
            lock (gate)
            {
                var matches = modules.Values.Where(value => value.Owner == owner || value.AssemblyName == owner).ToArray();
                if (matches.Length == 1) { owner = matches[0].Owner; version = matches[0].Version; }
            }
            return Report(owner, version, error, "BoundaryFailed", operation, notify, true, includeMessages: includeMessages, level: level);
        }

        internal void ReportStructured(HostLogEntry entry)
        {
            if (entry?.Exception == null) return;
            ReportOwnedBoundary(entry.ExtensionId, entry.Message, entry.Exception, entry.Level == Utils.LogLevel.ERROR || entry.Level == Utils.LogLevel.FATAL, level: entry.Level);
        }

        private string Report(string owner, string version, Exception error, string code, string operation, bool notify, bool writeLog, string context = null, bool includeMessages = true, Utils.LogLevel level = Utils.LogLevel.ERROR)
        {
            if (error == null) return null;
            if (reporting) { Interlocked.Increment(ref reentrantFailures); return null; }
            reporting = true;
            try
            {
                lock (gate)
                    if (TryKnown(error, notify, level, out var knownId)) return knownId;

                // Snapshot outside the lock: exception properties can be implemented by third-party code.
                var detail = ClientErrorText.Exception(error, includeMessages);
                owner = ClientErrorText.Safe(owner ?? "unknown", 160);
                code = ClientErrorText.Safe(code ?? "UnhandledError", 160);
                operation = ClientErrorText.Safe(operation ?? "unknown", 256);
                string fingerprint;
                using (var sha = SHA256.Create())
                    fingerprint = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(owner + "\n" + code + "\n" + operation + "\n" + detail.Fingerprint)));

                Entry entry;
                bool firstOccurrence;
                lock (gate)
                {
                    if (TryKnown(error, notify, level, out var knownId)) return knownId;
                    entry = entries.FirstOrDefault(e => e.Fingerprint == fingerprint);
                    firstOccurrence = entry == null;
                    if (entry == null)
                    {
                        if (entries.Count == MaximumRecords) { entries.RemoveAt(entries.Count - 1); evicted++; }
                        entry = new Entry {
                            Id = "PX-" + Guid.NewGuid().ToString("N"), Owner = owner, Version = version,
                            Code = code, Operation = operation, Fingerprint = fingerprint, Detail = detail.Text,
                            First = DateTime.UtcNow, Last = DateTime.UtcNow, Count = 1, Pending = notify,
                            Context = ClientErrorText.Safe(context, 256), NotificationRevision = sequence + 1,
                            NotificationRequested = notify, Level = level
                        };
                        entries.Insert(0, entry);
                    }
                    else
                    {
                        entry.Count++;
                        entry.Last = DateTime.UtcNow;
                        PromoteLevel(entry,level);
                        RequestNotification(entry, notify);
                        // A dismissed group stays dismissed. Only a genuinely new group requests another popup.
                    }
                    identities.Remove(error);
                    identities.Add(error, new Identity(entry.Id));
                    sequence++;
                }
                // One complete log per group. Repeated occurrences remain visible as counters in copied reports.
                if (writeLog && firstOccurrence)
                {
                    try { if (levelLog != null) levelLog(BuildReport(entry.Id),entry.Level); else log?.Invoke(BuildReport(entry.Id)); }
                    catch { Interlocked.Increment(ref sinkFailures); }
                }
                return entry.Id;
            }
            catch
            {
                // The original exception must remain the caller's exception even if reporting itself fails.
                Interlocked.Increment(ref sinkFailures);
                return null;
            }
            finally { reporting = false; }
        }

        internal long Revision { get { lock (gate) return sequence; } }
        // Called with gate held. A previously silent diagnostic can be promoted once;
        // a group already shown/dismissed never reopens just because it repeats.
        private void RequestNotification(Entry entry, bool notify)
        {
            if (!notify || entry.NotificationRequested) return;
            entry.Pending = entry.NotificationRequested = true;
            entry.NotificationRevision = ++sequence;
        }
        private void PromoteLevel(Entry entry,Utils.LogLevel level)
        {
            if ((int)level > (int)entry.Level) { entry.Level = level; sequence++; }
        }
        private bool TryKnown(Exception error, bool notify, Utils.LogLevel level, out string id)
        {
            id = null;
            if (!identities.TryGetValue(error, out var identity)) return false;
            var entry = entries.FirstOrDefault(e => e.Id == identity.Id);
            if (entry == null) return false;
            PromoteLevel(entry,level);
            RequestNotification(entry, notify);
            id = entry.Id;
            return true;
        }
        internal IReadOnlyList<ClientErrorSummary> Capture()
        {
            lock (gate) return entries.Select(e => new ClientErrorSummary(e.Id, e.Owner, e.Code, e.Operation, e.First, e.Last, e.Count)).ToArray();
        }
        internal bool HasPending { get { lock (gate) return entries.Any(e => e.Pending); } }
        internal void AcknowledgeNotifications(long throughRevision)
        { lock (gate) foreach (var e in entries) if (e.NotificationRevision <= throughRevision) e.Pending = false; }
        internal string BuildReport(string id)
        {
            lock (gate)
            {
                var e = entries.FirstOrDefault(value => value.Id == id);
                if (e == null) return null;
                return "Phinix Rework error report\n" + environment + "\nError ID: " + e.Id +
                    "\nOwner: " + e.Owner + "\nOwner version: " + e.Version + "\nCode: " + e.Code +
                    "\nOperation: " + e.Operation + "\nFirst UTC: " + e.First.ToString("O", CultureInfo.InvariantCulture) +
                    "\nLevel: " + e.Level +
                    "\nFirst sample context: " + e.Context +
                    "\nLast UTC: " + e.Last.ToString("O", CultureInfo.InvariantCulture) + "\nOccurrences: " + e.Count +
                    "\nEvicted groups: " + evicted + "\nDiagnostic sink failures: " + Interlocked.Read(ref sinkFailures) +
                    "\nSuppressed recursive reports: " + Interlocked.Read(ref reentrantFailures) +
                    "\nSuppressed shared log writes: " + ExtensionDiagnostics.SuppressedWrites +
                    "\nOutcome: not inferred; reporting does not retry, cancel or confirm the operation.\n\n" + e.Detail;
            }
        }
        private void RequireMainThread() { if (!mainThread()) throw new InvalidOperationException("Error reporter binding requires the main thread."); }

        private sealed class Identity { internal readonly string Id; internal Identity(string id) { Id = id; } }
        private sealed class Entry
        {
            internal string Id, Owner, Version, Code, Operation, Fingerprint, Detail, Context;
            internal DateTime First, Last;
            internal long Count;
            internal long NotificationRevision;
            internal bool Pending;
            internal bool NotificationRequested;
            internal Utils.LogLevel Level;
        }
        private sealed class Reporter : IClientSensitiveErrorReporter
        {
            private readonly ClientErrorService service;
            private readonly string owner, version;
            private int stopped;
            internal Reporter(ClientErrorService service, string owner, string version, string assemblyName = null) { this.service = service; this.owner = owner; this.version = version; AssemblyName = assemblyName; }
            internal string AssemblyName { get; }
            internal string Owner => owner;
            internal string Version => version;
            internal void Stop() { Volatile.Write(ref stopped, 1); }
            public string Report(Exception error, string code, string operation, bool notify = true)
                => Volatile.Read(ref stopped) == 0 ? service.Report(owner, version, error, code, operation, notify, true) : null;
            public string ReportSensitive(Exception error,string code,string operation,bool notify=true)
                => Volatile.Read(ref stopped)==0?service.Report(owner,version,error,code,operation,notify,true,includeMessages:false):null;
        }
        private sealed class ModuleIdentity : IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly ModuleIdentity Instance = new ModuleIdentity();
            public bool Equals(IPhinixExtensionModule a, IPhinixExtensionModule b) => ReferenceEquals(a, b);
            public int GetHashCode(IPhinixExtensionModule value) => RuntimeHelpers.GetHashCode(value);
        }
    }

    internal sealed class ClientErrorSummary
    {
        internal ClientErrorSummary(string id, string owner, string code, string operation, DateTime first, DateTime last, long count)
        { Id = id; Owner = owner; Code = code; Operation = operation; First = first; Last = last; Count = count; }
        internal string Id { get; }
        internal string Owner { get; }
        internal string Code { get; }
        internal string Operation { get; }
        internal DateTime First { get; }
        internal DateTime Last { get; }
        internal long Count { get; }
    }

    internal static class ClientErrorText
    {
        private static readonly Regex Credentials = new Regex(@"(?i)(\b(?:password|passwd|(?:next|client|credential)?[_-]?secret|(?:access|refresh|id)?[_-]?token|session(?:[_-]?id)?|authorization|api[_-]?key|credential(?:[_-]?id)?)\b[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;}]+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Bearer = new Regex(@"(?i)\bBearer\s+[^\s,;]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Url = new Regex(@"(?i)\b(?:https?|file)://[^\s<>""']+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Paths = new Regex(@"(?i)(?:[a-z]:\\|/(?:home|users|tmp|var|mnt|media|srv)/)[^\r\n""']+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        internal static string Safe(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            // Refuse oversize arbitrary messages rather than copy an unsanitized truncated credential.
            if (value.Length > maximum) return "[text omitted: size limit]";
            try
            {
                value = Bearer.Replace(value, "Bearer [redacted]");
                value = Credentials.Replace(value, "$1[redacted]");
                value = Url.Replace(value, "[endpoint redacted]");
                return Paths.Replace(value, "[local path redacted]").Replace("\0", "");
            }
            catch { return "[text omitted: sanitization failed]"; }
        }

        internal sealed class Snapshot { internal string Text, Fingerprint; }
        internal static Snapshot Exception(Exception error, bool includeMessages = true)
        {
            var text = new StringBuilder();
            var fingerprint = new StringBuilder();
            var seen = new HashSet<Exception>(ExceptionIdentity.Instance);
            int nodes = 0;
            Append(error, "Exception", 0, text, fingerprint, seen, ref nodes, includeMessages);
            if (text.Length > ClientErrorService.MaximumDetailCharacters)
            { text.Length = ClientErrorService.MaximumDetailCharacters; text.Append("\n[report truncated: size limit]"); }
            return new Snapshot { Text = text.ToString(), Fingerprint = fingerprint.ToString() };
        }
        private static void Append(Exception error, string label, int depth, StringBuilder text, StringBuilder fingerprint, HashSet<Exception> seen, ref int nodes, bool includeMessages)
        {
            if (error == null) return;
            if (depth >= 16 || nodes >= 64 || text.Length >= ClientErrorService.MaximumDetailCharacters)
            { text.AppendLine("[exception tree truncated: limit]"); return; }
            if (!seen.Add(error)) { text.AppendLine(label + ": [already included]"); return; }
            nodes++;
            string type = error.GetType().FullName;
            string message, stack;
            try { message = includeMessages ? Safe(error.Message, 8192) : "[message omitted: sensitive boundary]"; } catch { message = "[message unavailable]"; }
            // StackTrace rather than a plugin's ToString(): retain frames without invoking an arbitrary formatter.
            try
            {
                stack = new System.Diagnostics.StackTrace(error, false).ToString();
                if (stack.Length > ClientErrorService.MaximumDetailCharacters) stack = stack.Substring(0, ClientErrorService.MaximumDetailCharacters) + "\n[stack truncated: size limit]";
            }
            catch { stack = "[stack unavailable]"; }
            text.AppendLine(label + ": " + type).AppendLine(message).AppendLine(stack.Length == 0 ? "[exception has no captured stack]" : stack);
            fingerprint.AppendLine(type).AppendLine(stack);
            if (error is AggregateException aggregate)
            {
                for (int i = 0; i < aggregate.InnerExceptions.Count && i < 64; i++)
                    Append(aggregate.InnerExceptions[i], "Aggregate[" + i + "]", depth + 1, text, fingerprint, seen, ref nodes, includeMessages);
                if (aggregate.InnerExceptions.Count > 64) text.AppendLine("[aggregate branches truncated: limit]");
            }
            else Append(error.InnerException, "Inner", depth + 1, text, fingerprint, seen, ref nodes, includeMessages);
            if (error is ReflectionTypeLoadException reflection && reflection.LoaderExceptions != null)
            {
                for (int i = 0; i < reflection.LoaderExceptions.Length && i < 64; i++)
                    Append(reflection.LoaderExceptions[i], "Loader[" + i + "]", depth + 1, text, fingerprint, seen, ref nodes, includeMessages);
                if (reflection.LoaderExceptions.Length > 64) text.AppendLine("[loader branches truncated: limit]");
            }
        }
        private sealed class ExceptionIdentity : IEqualityComparer<Exception>
        {
            internal static readonly ExceptionIdentity Instance = new ExceptionIdentity();
            public bool Equals(Exception a, Exception b) => ReferenceEquals(a, b);
            public int GetHashCode(Exception value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
