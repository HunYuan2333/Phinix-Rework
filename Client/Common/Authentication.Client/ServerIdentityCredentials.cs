#if PHINIX_RIMWORLD_17
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Authentication;

// Supplied only after TLS trust validation and an authenticated server-identity greeting.
// This value is a storage binding, not proof that a handshake took place.
public sealed class TrustedIdentityServer
{
    public string ServerId { get; }
    public string ServerName { get; }
    internal string Pin { get; }
    public TrustedIdentityServer(string serverId, string serverName, byte[] spkiSha256)
    {
        ServerId = IdentityCredentialDirectory.Uuid(serverId);
        if (string.IsNullOrWhiteSpace(serverName) || serverName.Length > 253 || serverName != serverName.Trim() ||
            Uri.CheckHostName(serverName) == UriHostNameType.Unknown || spkiSha256 == null || spkiSha256.Length != 32)
            throw new ArgumentException("Invalid trusted server binding.");
        ServerName = serverName.Contains(':') ? serverName.ToLowerInvariant() : new IdnMapping().GetAscii(serverName.TrimEnd('.')).ToLowerInvariant();
        Pin = Convert.ToBase64String(spkiSha256);
    }
}

public sealed class PendingIdentityCredential : IDisposable
{
    private readonly byte[] secret;
    private readonly byte[]? nextSecret;
    private bool disposed;
    public string OperationId { get; }
    public string CredentialId { get; }
    public string? ExpectedPrincipalId { get; }
    public string? PrincipalId { get; }
    public bool IsConfirmed => PrincipalId != null;
    public string? RotationOperationId { get; }
    public string? NextCredentialId { get; }
    public string? RevocationOperationId { get; }
    public bool IsRevoked { get; }
    public byte[] CopyNextSecret() { lock (secret) { ObjectDisposedException.ThrowIf(disposed, this); return nextSecret == null ? throw new InvalidOperationException("No pending credential rotation.") : (byte[])nextSecret.Clone(); } }
    internal PendingIdentityCredential(string operation, string credential, string? expected, string? principal, byte[] secret, string? rotationOperation = null, string? nextCredential = null, byte[]? nextSecret = null, string? revocationOperation = null, bool revoked = false)
    { OperationId = operation; CredentialId = credential; ExpectedPrincipalId = expected; PrincipalId = principal; this.secret = (byte[])secret.Clone(); RotationOperationId = rotationOperation; NextCredentialId = nextCredential; this.nextSecret = nextSecret == null ? null : (byte[])nextSecret.Clone(); RevocationOperationId = revocationOperation; IsRevoked = revoked; }
    public byte[] CopySecret() { lock (secret) { ObjectDisposedException.ThrowIf(disposed, this); return (byte[])secret.Clone(); } }
    public void Dispose() { lock (secret) { if (disposed) return; CryptographicOperations.ZeroMemory(secret); if (nextSecret != null) CryptographicOperations.ZeroMemory(nextSecret); disposed = true; } }
    public override string ToString() => "Identity credential (secret omitted)";
}

// Modern identity only. No installation ClientKey, display-name lookup or automatic reset on read failure.
// No network registration is performed here. Callers may send only material returned after a successful write.
public sealed class IdentityCredentialDirectory
{
    private const int MaximumBytes = 8192;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHNXIC01");
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    private sealed class Document
    {
        [JsonRequired] public int Version { get; set; }
        [JsonRequired] public string ServerId { get; set; } = "";
        [JsonRequired] public string ServerName { get; set; } = "";
        [JsonRequired] public string Pin { get; set; } = "";
        [JsonRequired] public string OperationId { get; set; } = "";
        [JsonRequired] public string CredentialId { get; set; } = "";
        [JsonRequired] public string Secret { get; set; } = "";
        [JsonRequired] public string? ExpectedPrincipalId { get; set; }
        [JsonRequired] public string? PrincipalId { get; set; }
        public string? RotationOperationId { get; set; }
        public string? NextCredentialId { get; set; }
        public string? NextSecret { get; set; }
        public string? RevocationOperationId { get; set; }
        public bool Revoked { get; set; }
    }
    private readonly string directory;
    private readonly Action<string>? barrier;
    public IdentityCredentialDirectory(string existingPrivateDirectory) : this(existingPrivateDirectory, null) { }
    internal IdentityCredentialDirectory(string existingPrivateDirectory, Action<string>? barrier)
    {
        directory = Path.GetFullPath(existingPrivateDirectory);
        this.barrier = barrier;
        CheckDirectory();
    }
    internal static string Uuid(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty) throw new ArgumentException("Expected nonempty UUID.");
        return id.ToString("D");
    }
    internal static void CheckLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Identity storage filesystem link is unsafe."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void CheckPrivate(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var currentUser = identity.User ?? throw new IOException("Identity storage user cannot be established.");
            var acl = Directory.Exists(path) ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            bool Trusted(SecurityIdentifier? sid) => sid != null && (sid.Equals(currentUser) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
            var descriptor = new RawSecurityDescriptor(acl.GetSecurityDescriptorBinaryForm(), 0);
            if (descriptor.DiscretionaryAcl == null || !Trusted(descriptor.Owner))
                throw new IOException("Identity storage owner or discretionary access rules are unsafe.");
            const FileSystemRights dangerous = FileSystemRights.ReadData | FileSystemRights.WriteData | FileSystemRights.AppendData |
                FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
                FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & dangerous) != 0 && !Trusted(sid))
                    throw new IOException("Identity storage access rules are not private.");
            }
        }
        else if ((File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("Identity storage permissions are not private.");
    }
    private void CheckDirectory()
    {
        CheckLinks(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Identity storage directory is missing.");
        CheckPrivate(directory);
    }
    private string Target(TrustedIdentityServer server) => Path.Combine(directory, Guid.Parse(server.ServerId).ToString("N") + ".credential");
    private static FileStream OpenPrivate(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share, Options = FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }
    private string TrustMarker(TrustedIdentityServer server) => Path.Combine(directory,
        "trust-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.ServerName + "\0" + server.Pin))) + ".authority");
    private void CheckTrustBinding(TrustedIdentityServer server, bool create)
    {
        string path = TrustMarker(server); CheckLinks(path);
        if (File.Exists(path))
        {
            CheckPrivate(path);
            if (new FileInfo(path).Length != 36 || File.ReadAllText(path) != server.ServerId)
                throw new InvalidDataException("Server identity changed for an existing trusted authority.");
        }
        else if (create)
        {
            using var marker = OpenPrivate(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            marker.Write(Encoding.ASCII.GetBytes(server.ServerId)); marker.Flush(true);
        }
    }
    private FileStream Acquire(TrustedIdentityServer server)
    {
        CheckDirectory(); CheckTrustBinding(server, false);
        string path = Target(server) + ".lock"; CheckLinks(path);
        var stream = OpenPrivate(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { CheckLinks(path); CheckPrivate(path); return stream; } catch { stream.Dispose(); throw; }
    }
    private Document Read(TrustedIdentityServer server)
    {
        string path = Target(server); CheckDirectory(); CheckLinks(path); CheckPrivate(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < 41 || input.Length > MaximumBytes) throw new InvalidDataException("Identity credential length is invalid.");
        byte[] bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
        try
        {
            if (!bytes.AsSpan(0, 8).SequenceEqual(Magic) || !CryptographicOperations.FixedTimeEquals(bytes.AsSpan(8, 32), SHA256.HashData(bytes.AsSpan(40))))
                throw new InvalidDataException("Identity credential integrity check failed.");
            using var parsed = JsonDocument.Parse(bytes.AsMemory(40));
            if (parsed.RootElement.ValueKind != JsonValueKind.Object || parsed.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != parsed.RootElement.EnumerateObject().Count())
                throw new InvalidDataException("Identity credential structure is invalid.");
            var document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(40), Json)!;
            if (document.Version == 2)
                foreach (string field in new[] { "RotationOperationId", "NextCredentialId", "NextSecret", "RevocationOperationId", "Revoked" })
                    if (!parsed.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("Identity credential state evidence is missing.");
            Validate(server, document); return document;
        }
        catch (JsonException) { throw new InvalidDataException("Identity credential format is invalid."); }
        catch (ArgumentException) { throw new InvalidDataException("Identity credential fields are invalid."); }
        catch (FormatException) { throw new InvalidDataException("Identity credential fields are invalid."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void Validate(TrustedIdentityServer server, Document d)
    {
        if ((d.Version != 1 && d.Version != 2) || d.ServerId != server.ServerId || d.ServerName != server.ServerName || d.Pin != server.Pin)
            throw new InvalidDataException("Identity credential version or trusted server binding differs.");
        Uuid(d.OperationId); Uuid(d.CredentialId);
        byte[] secret = Convert.FromBase64String(d.Secret);
        try { if (secret.Length != 32) throw new InvalidDataException("Identity credential secret length is invalid."); }
        finally { CryptographicOperations.ZeroMemory(secret); }
        if (d.Version == 1 && (d.RotationOperationId != null || d.NextCredentialId != null || d.NextSecret != null || d.RevocationOperationId != null || d.Revoked))
            throw new InvalidDataException("Credential lifecycle requires the current format.");
        bool rotating = d.RotationOperationId != null || d.NextCredentialId != null || d.NextSecret != null;
        if (rotating)
        {
            if (d.PrincipalId == null || d.RevocationOperationId != null || d.Revoked || d.RotationOperationId == null || d.NextCredentialId == null || d.NextSecret == null)
                throw new InvalidDataException("Credential rotation is incomplete.");
            Uuid(d.RotationOperationId); Uuid(d.NextCredentialId);
            var next = Convert.FromBase64String(d.NextSecret);
            try { if (next.Length != 32) throw new InvalidDataException("Credential rotation material is invalid."); }
            finally { CryptographicOperations.ZeroMemory(next); }
        }
        if (d.RevocationOperationId != null) { Uuid(d.RevocationOperationId); if (d.PrincipalId == null) throw new InvalidDataException("Unconfirmed identity cannot be revoked."); }
        if (d.Revoked && d.RevocationOperationId == null) throw new InvalidDataException("Revocation evidence is missing.");
        if (d.ExpectedPrincipalId != null) Uuid(d.ExpectedPrincipalId);
        if (d.PrincipalId != null) Uuid(d.PrincipalId);
        if (d.ExpectedPrincipalId != null && d.PrincipalId != null && !string.Equals(d.ExpectedPrincipalId, d.PrincipalId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Identity credential principal differs from authorized binding.");
    }
    private static PendingIdentityCredential Snapshot(Document d)
    {
        byte[] secret = Convert.FromBase64String(d.Secret);
        try
        {
            byte[]? next = d.NextSecret == null ? null : Convert.FromBase64String(d.NextSecret);
            try { return new(d.OperationId, d.CredentialId, d.ExpectedPrincipalId, d.PrincipalId, secret, d.RotationOperationId, d.NextCredentialId, next, d.RevocationOperationId, d.Revoked); }
            finally { if (next != null) CryptographicOperations.ZeroMemory(next); }
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    private void Write(TrustedIdentityServer server, Document d, bool replace, CancellationToken cancellation)
    {
        d.Version = 2; Validate(server, d); cancellation.ThrowIfCancellationRequested();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(d, Json);
        string target = Target(server), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CheckDirectory(); CheckLinks(target);
            using (var output = OpenPrivate(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                barrier?.Invoke("identity.client.before-write"); output.Write(Magic); output.Write(SHA256.HashData(payload)); output.Write(payload); output.Flush(true);
            }
            barrier?.Invoke("identity.client.after-flush"); cancellation.ThrowIfCancellationRequested();
            CheckDirectory(); CheckLinks(target); CheckLinks(temporary); CheckPrivate(temporary);
            // Same-directory rename, never copy/delete the authoritative file. Cooperating writers hold the sidecar lock.
            EnsureExpectation(server);
            File.Move(temporary, target, replace);
            barrier?.Invoke("identity.client.after-commit");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            if (File.Exists(temporary)) { CheckDirectory(); CheckLinks(temporary); File.Delete(temporary); }
        }
    }
    private void EnsureExpectation(TrustedIdentityServer server)
    {
        CheckTrustBinding(server, true);
        string expected = Target(server) + ".expected"; CheckLinks(expected);
        if (File.Exists(expected))
        {
            CheckPrivate(expected);
            if (new FileInfo(expected).Length != 36 || File.ReadAllText(expected) != server.ServerId) throw new InvalidDataException("Identity enrollment expectation differs.");
            return;
        }
        using var marker = OpenPrivate(expected, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Write(Encoding.ASCII.GetBytes(server.ServerId)); marker.Flush(true);
    }
    public bool TryLoadExisting(TrustedIdentityServer server, out PendingIdentityCredential? credential)
    {
        using var ownership = Acquire(server); credential = null;
        string target = Target(server); CheckLinks(target); CheckLinks(target + ".expected");
        if (File.Exists(target)) { var document = Read(server); EnsureExpectation(server); credential = Snapshot(document); return true; }
        if (File.Exists(target + ".expected")) throw new IOException("Expected identity credential is missing; automatic registration is forbidden.");
        return false;
    }
    public PendingIdentityCredential Load(TrustedIdentityServer server)
    { using var ownership = Acquire(server); return Snapshot(Read(server)); }
    // Explicit first enrollment only. Missing/corrupt Load never calls this or silently generates a replacement identity.
    public PendingIdentityCredential CreatePending(TrustedIdentityServer server, string? expectedPrincipalId = null, CancellationToken cancellation = default)
    {
        if (expectedPrincipalId != null) Uuid(expectedPrincipalId);
        using var ownership = Acquire(server);
        CheckLinks(Target(server)); CheckLinks(Target(server) + ".expected");
        if (File.Exists(Target(server)) || File.Exists(Target(server) + ".expected")) throw new IOException("Identity credential already exists; load the original operation.");
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            var d = new Document { Version = 1, ServerId = server.ServerId, ServerName = server.ServerName, Pin = server.Pin,
                OperationId = Guid.NewGuid().ToString("D"), CredentialId = Guid.NewGuid().ToString("D"), Secret = Convert.ToBase64String(secret), ExpectedPrincipalId = expectedPrincipalId };
            Write(server, d, false, cancellation); return Snapshot(d);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    public PendingIdentityCredential BeginRotation(TrustedIdentityServer server)
    {
        using var ownership = Acquire(server); var d = Read(server);
        if (d.PrincipalId == null || d.Revoked || d.RevocationOperationId != null) throw new InvalidOperationException("Identity cannot rotate in its current state.");
        if (d.RotationOperationId == null)
        {
            byte[] next = RandomNumberGenerator.GetBytes(32);
            try { d.RotationOperationId = Guid.NewGuid().ToString("D"); d.NextCredentialId = Guid.NewGuid().ToString("D"); d.NextSecret = Convert.ToBase64String(next); Write(server, d, true, default); }
            finally { CryptographicOperations.ZeroMemory(next); }
        }
        return Snapshot(d);
    }
    public PendingIdentityCredential ConfirmRotation(TrustedIdentityServer server, string operationId, string credentialId)
    {
        using var ownership = Acquire(server); var d = Read(server);
        operationId = Uuid(operationId); credentialId = Uuid(credentialId);
        if (d.RotationOperationId == null && d.OperationId == operationId && d.CredentialId == credentialId) return Snapshot(d);
        if (d.RotationOperationId != operationId || d.NextCredentialId != credentialId) throw new InvalidOperationException("Credential rotation response conflicts.");
        d.OperationId = d.RotationOperationId; d.CredentialId = d.NextCredentialId; d.Secret = d.NextSecret!;
        d.RotationOperationId = d.NextCredentialId = d.NextSecret = null; Write(server, d, true, default); return Snapshot(d);
    }
    public PendingIdentityCredential BeginRevocation(TrustedIdentityServer server)
    {
        using var ownership = Acquire(server); var d = Read(server);
        if (d.PrincipalId == null || d.RotationOperationId != null || d.Revoked) throw new InvalidOperationException("Identity cannot revoke in its current state.");
        if (d.RevocationOperationId == null) { d.RevocationOperationId = Guid.NewGuid().ToString("D"); Write(server, d, true, default); }
        return Snapshot(d);
    }
    public PendingIdentityCredential ConfirmRevocation(TrustedIdentityServer server, string operationId)
    {
        using var ownership = Acquire(server); var d = Read(server);
        if (d.RevocationOperationId != Uuid(operationId)) throw new InvalidOperationException("Credential revocation response conflicts.");
        if (!d.Revoked) { d.Revoked = true; Write(server, d, true, default); } return Snapshot(d);
    }
    // Caller must have verified the authoritative response from the same trusted server. Never driven by a UI prediction.
    public PendingIdentityCredential Confirm(TrustedIdentityServer server, string operationId, string principalId, CancellationToken cancellation = default)
    {
        operationId = Uuid(operationId); Uuid(principalId);
        using var ownership = Acquire(server); var d = Read(server);
        if (d.OperationId != operationId || (d.ExpectedPrincipalId != null && !string.Equals(d.ExpectedPrincipalId, principalId, StringComparison.OrdinalIgnoreCase)) ||
            (d.PrincipalId != null && !string.Equals(d.PrincipalId, principalId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Identity confirmation conflicts with the pending operation.");
        if (d.PrincipalId == null) { d.PrincipalId = principalId; Write(server, d, true, cancellation); }
        return Snapshot(d);
    }
}
#endif
