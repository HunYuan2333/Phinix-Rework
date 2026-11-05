using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Authentication;

internal static class Program
{
    static int checks;
    static readonly byte[] Pin = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    static readonly List<string> Faults = new();
    static string Id() => Guid.NewGuid().ToString("D");
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Refuses<T>(Action action, string why) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception(why); }
    static string Private(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    static string FilePath(string root, string id) => Path.Combine(root, Guid.Parse(id).ToString("N") + ".credential");
    static IdentityCredentialDirectory FaultStore(string root, Action<string> barrier) => (IdentityCredentialDirectory)Activator.CreateInstance(
        typeof(IdentityCredentialDirectory), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { root, barrier }, null)!;
    static void Envelope(string path, byte[] payload)
    { using var output = new FileStream(path, FileMode.Create); output.Write(Encoding.ASCII.GetBytes("PHNXIC01")); output.Write(SHA256.HashData(payload)); output.Write(payload); }
    static JsonObject Document(string path) => JsonNode.Parse(File.ReadAllBytes(path).AsSpan(40))!.AsObject();
    static Process Worker(string root, string id, string phase, string mode)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { Assembly.GetExecutingAssembly().Location, "worker", root, id, phase, mode }) info.ArgumentList.Add(a);
        return Process.Start(info)!;
    }
    static void WaitReady(Process child, string root)
    {
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "ready")))
        {
            if (child.HasExited || timer.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("Worker did not reach the credential barrier.");
            Thread.Sleep(10);
        }
    }
    static void KillCases(string root)
    {
        foreach (string mode in new[] { "create", "confirm" }) foreach (string phase in new[] { "after-flush", "after-commit" })
        {
            string dir = Private(Path.Combine(root, mode + phase)), id = Id(); var binding = new TrustedIdentityServer(id, "localhost", Pin);
            var store = new IdentityCredentialDirectory(dir); string? operation = null; byte[]? original = null;
            if (mode == "confirm") { using var before = store.CreatePending(binding); operation = before.OperationId; original = File.ReadAllBytes(FilePath(dir, id)); }
            using var child = Worker(dir, id, phase, mode);
            try
            {
                WaitReady(child, dir);
                Refuses<IOException>(() => store.Load(binding), "Concurrent process bypassed the credential ownership lock");
                child.Kill(entireProcessTree: true); Check(child.WaitForExit(10000), "Killed worker did not terminate");
                string path = FilePath(dir, id);
                if (mode == "create" && phase == "after-flush")
                {
                    Check(!File.Exists(path) && Directory.GetFiles(dir, "*.tmp").Length == 1, "Uncommitted create became authoritative or lost its fault evidence");
                    Refuses<FileNotFoundException>(() => store.Load(binding), "Missing credential automatically regenerated after kill");
                }
                else
                {
                    // Independent envelope and JSON oracle after a real process kill.
                    byte[] committed = File.ReadAllBytes(path);
                    Check(committed.AsSpan(0, 8).SequenceEqual(Encoding.ASCII.GetBytes("PHNXIC01")) && committed.AsSpan(8, 32).SequenceEqual(SHA256.HashData(committed.AsSpan(40))), "Killed write left a partial authority file");
                    using var read = store.Load(binding);
                    Check(read.OperationId == (operation ?? Document(path)["OperationId"]!.GetValue<string>()), "Restart changed durable enrollment operation");
                    Check(read.IsConfirmed == (mode == "confirm" && phase == "after-commit"), "Kill boundary changed confirmation semantics");
                    if (mode == "confirm" && phase == "after-flush") Check(original!.SequenceEqual(committed), "Failed confirmation destroyed the previous credential");
                }
                Faults.Add("identity.client." + mode + ".kill-" + phase);
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(10000); } }
        }
    }
    static void Lifecycle(string root)
    {
        string dir = Private(Path.Combine(root, "lifecycle")), id = Id(), principal = Id();
        var binding = new TrustedIdentityServer(id, "localhost", Pin); var store = new IdentityCredentialDirectory(dir);
        Check(!store.TryLoadExisting(binding, out _), "Fresh identity slot was not distinguished from missing expected credentials");
        using var pending = store.CreatePending(binding, principal); using var active = store.Confirm(binding, pending.OperationId, principal);
        string path = FilePath(dir, id);
        var old = Document(path); old["Version"] = 1;
        foreach (string field in new[] { "RotationOperationId", "NextCredentialId", "NextSecret", "RevocationOperationId", "Revoked" }) old.Remove(field);
        Envelope(path, Encoding.UTF8.GetBytes(old.ToJsonString()));
        Check(store.TryLoadExisting(binding, out var upgraded) && upgraded!.CredentialId == active.CredentialId, "Prior version-one credential could not be loaded"); upgraded!.Dispose();
        using var candidate = store.BeginRotation(binding); using var replay = store.BeginRotation(binding);
        Check(candidate.RotationOperationId == replay.RotationOperationId && candidate.NextCredentialId == replay.NextCredentialId &&
            candidate.CopyNextSecret().SequenceEqual(replay.CopyNextSecret()) && candidate.CopySecret().SequenceEqual(active.CopySecret()), "Pending rotation lost its original operation or active credential");
        byte[] before = File.ReadAllBytes(path);
        Refuses<InvalidOperationException>(() => store.ConfirmRotation(binding, Id(), candidate.NextCredentialId!), "Unrelated operation promoted rotation");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Refused rotation changed durable state");
        using var next = store.ConfirmRotation(binding, candidate.RotationOperationId!, candidate.NextCredentialId!);
        Check(next.CredentialId == candidate.NextCredentialId && next.CopySecret().SequenceEqual(candidate.CopyNextSecret()) && next.PrincipalId == principal && next.RotationOperationId == null,
            "Rotation confirmation failed to retain stable identity and promote exact material");
        using var repeated = store.ConfirmRotation(binding, candidate.RotationOperationId!, candidate.NextCredentialId!);
        Check(repeated.CredentialId == next.CredentialId, "Rotation confirmation replay was not idempotent");
        using var revoking = store.BeginRevocation(binding); using var revokingAgain = store.BeginRevocation(binding);
        Check(revoking.RevocationOperationId == revokingAgain.RevocationOperationId && !revoking.IsRevoked, "Revocation intent was not stable before dispatch");
        Refuses<InvalidOperationException>(() => store.BeginRotation(binding), "Rotation overwrote unresolved revocation");
        Refuses<InvalidOperationException>(() => store.ConfirmRevocation(binding, Id()), "Unrelated operation confirmed revocation");
        using var revoked = store.ConfirmRevocation(binding, revoking.RevocationOperationId!);
        using var revokedReplay = store.ConfirmRevocation(binding, revoking.RevocationOperationId!);
        Check(revoked.IsRevoked && revokedReplay.IsRevoked && revoked.PrincipalId == principal, "Confirmed revocation disappeared or changed identity");
        var damaged = Document(path); damaged.Remove("Revoked"); Envelope(path, Encoding.UTF8.GetBytes(damaged.ToJsonString()));
        Refuses<InvalidDataException>(() => store.Load(binding), "Missing current-format revoked state defaulted to active");
        File.Delete(path);
        Refuses<IOException>(() => store.TryLoadExisting(binding, out _), "Missing expected credential became a fresh registration slot");
        Refuses<IOException>(() => store.CreatePending(binding), "Missing expected credential silently recreated another identity");
    }
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "worker")
        {
            var server = new TrustedIdentityServer(args[2], "localhost", Pin);
            var store = FaultStore(args[1], phase =>
            {
                if (phase != "identity.client." + args[3]) return;
                File.WriteAllText(Path.Combine(args[1], "ready"), phase); Thread.Sleep(Timeout.Infinite);
            });
            if (args[4] == "create") using (store.CreatePending(server)) { }
            else { using var old = new IdentityCredentialDirectory(args[1]).Load(server); using var confirmed = store.Confirm(server, old.OperationId, "00000000-0000-4000-8000-000000000099"); }
            return 1;
        }
        string root = Private(Path.Combine(Path.GetTempPath(), "phinix-identity-client-" + Guid.NewGuid().ToString("N")));
        try
        {
            string dir = Private(Path.Combine(root, "normal")), id = Id(), principal = Id().ToUpperInvariant();
            var server = new TrustedIdentityServer(id, "LOCALHOST.", Pin); var store = new IdentityCredentialDirectory(dir);
            Refuses<FileNotFoundException>(() => store.Load(server), "Missing credentials silently generated a new identity");
            using var pending = store.CreatePending(server, principal); string path = FilePath(dir, id);
            Check(!pending.IsConfirmed && pending.CopySecret().Length == 32 && Guid.TryParse(pending.OperationId, out _), "Enrollment not durably pending with a strong secret");
            byte[] original = File.ReadAllBytes(path), secret = pending.CopySecret();
            using var loaded = new IdentityCredentialDirectory(dir).Load(new TrustedIdentityServer(id, "localhost", Pin));
            Check(loaded.OperationId == pending.OperationId && loaded.CredentialId == pending.CredentialId && loaded.CopySecret().SequenceEqual(secret), "Restart did not retain exact pending material");
            Refuses<IOException>(() => store.CreatePending(server), "Enrollment overwrote an existing identity");
            Refuses<InvalidOperationException>(() => store.Confirm(server, Id(), principal), "Unrelated response confirmed enrollment");
            Refuses<InvalidOperationException>(() => store.Confirm(server, pending.OperationId, Id()), "Response silently changed the expected original principal");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "Refusal changed the pending file");
            byte[] copy = pending.CopySecret(); copy[0] ^= 255;
            Check(pending.CopySecret().SequenceEqual(secret) && !pending.ToString().Contains(Convert.ToBase64String(secret)), "Caller mutated a retained secret or diagnostics exposed it");
            using var cancel = new CancellationTokenSource();
            var cancelling = FaultStore(dir, phase => { if (phase == "identity.client.after-flush") cancel.Cancel(); });
            Refuses<OperationCanceledException>(() => cancelling.Confirm(server, pending.OperationId, principal, cancel.Token), "Cancelled write was reported committed");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "Cancellation damaged the previous credential");
            using var confirmed = store.Confirm(server, pending.OperationId, principal);
            Check(confirmed.IsConfirmed && confirmed.PrincipalId == principal && confirmed.CopySecret().SequenceEqual(secret), "Confirmation lost original UUID or secret");
            byte[] bound = File.ReadAllBytes(path);
            using var replay = store.Confirm(server, pending.OperationId, principal);
            Check(bound.SequenceEqual(File.ReadAllBytes(path)), "Confirmation replay rewrote the authority file");
            Refuses<InvalidDataException>(() => store.Load(new TrustedIdentityServer(id, "other.example", Pin)), "Credentials leaked to another trusted name");
            Refuses<InvalidDataException>(() => store.Load(new TrustedIdentityServer(id, "localhost", new byte[32])), "Credentials leaked across a pin change");
            Refuses<InvalidDataException>(() => store.Load(new TrustedIdentityServer(Id(), "localhost", Pin)), "Different server inherited credentials");
            foreach (byte[] corrupt in new[] { Array.Empty<byte>(), bound.Take(45).ToArray(), bound.Select((b, i) => i == 50 ? (byte)(b ^ 1) : b).ToArray() })
            {
                File.WriteAllBytes(path, corrupt); Refuses<InvalidDataException>(() => store.Load(server), "Corrupt credential was accepted");
                Check(corrupt.SequenceEqual(File.ReadAllBytes(path)), "Corrupt credential was silently replaced");
            }
            File.WriteAllBytes(path, bound);
            foreach (string field in new[] { "Version", "Secret", "PrincipalId" })
            {
                var doc = Document(path); if (field == "Version") doc[field] = 3; else doc.Remove(field);
                Envelope(path, Encoding.UTF8.GetBytes(doc.ToJsonString()));
                Refuses<InvalidDataException>(() => store.Load(server), "Unsupported version or required field was accepted"); File.WriteAllBytes(path, bound);
            }
            Envelope(path, Encoding.UTF8.GetBytes("{\"Version\":1,\"Version\":1}"));
            Refuses<InvalidDataException>(() => store.Load(server), "Duplicate credential fields were accepted"); File.WriteAllBytes(path, bound);
            Envelope(path, Encoding.UTF8.GetBytes("{ broken json"));
            Refuses<InvalidDataException>(() => store.Load(server), "Malformed JSON with valid checksum was accepted"); File.WriteAllBytes(path, bound);
            if (!OperatingSystem.IsWindows())
            {
                Check((File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "Credential secret created with public read permissions");
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                Refuses<IOException>(() => store.Load(server), "Publicly readable secret was silently accepted"); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                string link = Path.Combine(root, "linked"); Directory.CreateSymbolicLink(link, dir);
                Refuses<IOException>(() => new IdentityCredentialDirectory(link), "Linked parent was accepted"); Directory.Delete(link);
                File.Delete(path); string outside = Path.Combine(root, "outside"); File.WriteAllText(outside, "sentinel"); File.CreateSymbolicLink(path, outside);
                Refuses<IOException>(() => store.CreatePending(server), "Linked credential destination was accepted");
                Check(File.ReadAllText(outside) == "sentinel", "Unsafe destination modified unrelated data"); File.Delete(path);
                File.Delete(path + ".lock"); File.CreateSymbolicLink(path + ".lock", outside);
                Refuses<IOException>(() => store.Load(server), "Linked credential ownership file was accepted"); File.Delete(path + ".lock");
            }
            else
            {
                var info = new FileInfo(path); var acl = info.GetAccessControl(); byte[] beforeAcl = acl.GetSecurityDescriptorBinaryForm();
                try
                {
                    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow));
                    info.SetAccessControl(acl);
                    Refuses<IOException>(() => store.Load(server), "Windows Everyone-readable credential was accepted");
                }
                finally
                {
                    var restored = new FileSecurity(); restored.SetSecurityDescriptorBinaryForm(beforeAcl, AccessControlSections.Access); info.SetAccessControl(restored);
                }
                Check(store.Load(server).IsConfirmed, "Restoring Windows ACL did not restore safe read capability");
            }
            pending.Dispose(); Refuses<ObjectDisposedException>(() => pending.CopySecret(), "Disposed secret remained accessible");
            Lifecycle(root);
            KillCases(root);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, faultIds = Faults, profile = "modern .NET10 credential persistence component", secretsInOutput = false, nativeGameValidated = false, windowsAclValidated = OperatingSystem.IsWindows(), windowsLinksValidated = false }));
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
