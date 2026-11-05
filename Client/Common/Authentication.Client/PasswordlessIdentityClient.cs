#if PHINIX_RIMWORLD_17
#nullable enable
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Connections;
using ServerTransport;

namespace Authentication;

internal sealed class PasswordlessIdentityClient : IDisposable
{
    private readonly object gate = new();
    private readonly IClientTransport transport;
    private readonly Func<bool> enabled;
    private readonly string root;
    private readonly Func<string, string> previousProof;
    private readonly Func<string, bool> hasPreviousMapping;
    private bool previousRequired;
    private readonly Action<string, bool> authenticated;
    private readonly Action<string> failed;
    private readonly Timer timer;
    private long dueAt = long.MaxValue;
    private IdentityCredentialDirectory? files;
    private TrustedIdentityServer? server;
    private PendingIdentityCredential? credential;
    private string requestId = "", pendingKind = "", proof = "", session = "";
    private bool disposed, authenticatingCandidate, rotateAfterAuthentication, published;
    internal bool Required => enabled();
    internal PasswordlessIdentityClient(IClientTransport transport, Func<bool> enabled, string root, Func<string, string> previousProof,
        Action<string, bool> authenticated, Action<string> failed, Func<string, bool> hasPreviousMapping)
    {
        this.transport = transport; this.enabled = enabled; this.root = root; this.previousProof = previousProof;
        this.authenticated = authenticated; this.failed = failed; this.hasPreviousMapping = hasPreviousMapping;
        timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        try { transport.RegisterPacketHandler(IdentityWire.Module, Handle); }
        catch { timer.Dispose(); throw; }
        transport.OnDisconnect += Disconnect; transport.OnConnecting += Connecting;
    }
    private static void EnsureDirectory(string path)
    {
        IdentityCredentialDirectory.CheckLinks(path);
        if (Directory.Exists(path)) return;
        if (!OperatingSystem.IsWindows()) Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
        {
            Directory.CreateDirectory(path);
            using var user = WindowsIdentity.GetCurrent(); var acl = new DirectorySecurity(); acl.SetOwner(user.User!); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { user.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        IdentityCredentialDirectory.CheckLinks(path);
    }
    private void Arm(int milliseconds)
    { dueAt = Environment.TickCount64 + milliseconds; timer.Change(milliseconds, Timeout.Infinite); }
    private void Disarm() { dueAt = long.MaxValue; timer.Change(Timeout.Infinite, Timeout.Infinite); }
    private void Send(IdentityEnvelope request)
    {
        request.ServerId = server!.ServerId; requestId = request.RequestId = Guid.NewGuid().ToString("D"); pendingKind = request.Kind;
        Arm(12000); transport.Send(IdentityWire.Module, IdentityWire.Encode(request));
    }
    private void Enroll()
    {
        byte[] secret = credential!.CopySecret();
        try { Send(new() { Kind = "Enroll", OperationId = credential.OperationId, CredentialId = credential.CredentialId,
            Secret = Convert.ToBase64String(secret), PrincipalId = credential.ExpectedPrincipalId ?? "", PreviousProof = proof }); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret); }
    }
    private void Authenticate(bool candidate = false)
    {
        authenticatingCandidate = candidate;
        byte[] secret = candidate ? credential!.CopyNextSecret() : credential!.CopySecret();
        try { Send(new() { Kind = "Authenticate", CredentialId = candidate ? credential.NextCredentialId! : credential.CredentialId, Secret = Convert.ToBase64String(secret) }); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret); }
    }
    private void Handle(string module, string connection, byte[] data)
    {
        lock (gate)
        {
            if (disposed) return;
            try
            {
                if (!Required || transport is not IValidatedClientTransport secure || !secure.TryGetValidatedTrust(out var trust))
                    throw new InvalidOperationException("SecureIdentityRequired");
                var packet = IdentityWire.Decode(data);
                if (packet.Kind == "Hello")
                {
                    if (server != null || packet.RequestId != "") throw new InvalidOperationException("UnexpectedGreeting");
                    server = new TrustedIdentityServer(packet.ServerId, trust.ServerName, trust.CopyPin());
                    if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new IOException("Absolute user identity directory is unavailable.");
                    EnsureDirectory(root); files = new IdentityCredentialDirectory(root);
                    if (files.TryLoadExisting(server, out credential))
                    {
                        if (credential!.IsRevoked) throw new InvalidOperationException("CredentialRevoked");
                        if (credential.RevocationOperationId != null) QueryRevocation();
                        else if (credential.IsConfirmed) Authenticate(credential.RotationOperationId != null);
                        else
                        {
                            proof = credential.ExpectedPrincipalId == null ? "" : previousProof(packet.ServerName);
                            if (credential.ExpectedPrincipalId != null && string.IsNullOrEmpty(proof)) throw new InvalidOperationException("Original identity proof is unavailable.");
                            Enroll();
                        }
                    }
                    else
                    {
                        proof = previousProof(packet.ServerName); previousRequired = hasPreviousMapping(packet.ServerName);
                        if (!string.IsNullOrEmpty(proof)) Send(new() { Kind = "Resolve", PreviousProof = proof });
                        else { credential = files.CreatePending(server); Enroll(); }
                    }
                    return;
                }
                if (packet.Kind != "Result" || server == null || packet.ServerId != server.ServerId || packet.RequestId != requestId || pendingKind == "")
                    throw new InvalidOperationException("UnexpectedIdentityResponse");
                string action = pendingKind; pendingKind = ""; requestId = ""; Disarm();
                if (packet.Failure == "OperationNotFound" && action == "RevocationStatus") { Authenticate(false); return; }
                if (packet.Failure == "InvalidCredential" && action == "Authenticate" && authenticatingCandidate)
                { rotateAfterAuthentication = true; Authenticate(false); return; }
                if (packet.Failure != "") { Fail("Identity request refused; reconnect after checking server trust and identity state."); return; }
                if (action == "Resolve")
                {
                    if (previousRequired && string.IsNullOrEmpty(packet.PrincipalId)) throw new InvalidOperationException("Expected previous identity mapping is missing.");
                    credential = files!.CreatePending(server, string.IsNullOrEmpty(packet.PrincipalId) ? null : packet.PrincipalId); Enroll();
                }
                else if (action == "Enroll")
                {
                    if (packet.OperationId != credential!.OperationId || packet.CredentialId != credential.CredentialId) throw new InvalidOperationException("EnrollmentConflict");
                    var confirmed = files!.Confirm(server, credential.OperationId, packet.PrincipalId); credential.Dispose(); credential = confirmed; proof = ""; Authenticate();
                }
                else if (action == "Authenticate")
                {
                    if (packet.PrincipalId != credential!.PrincipalId || packet.SessionId.Length != 43 || packet.ExpiresInMilliseconds < 1000 || packet.ExpiresInMilliseconds > 3600000)
                        throw new InvalidOperationException("InvalidSessionResponse");
                    if (authenticatingCandidate)
                    {
                        var promoted = files!.ConfirmRotation(server, credential.RotationOperationId!, credential.NextCredentialId!);
                        credential.Dispose(); credential = promoted; authenticatingCandidate = false;
                    }
                    bool first = !published; session = packet.SessionId;
                    if (rotateAfterAuthentication) { rotateAfterAuthentication = false; SubmitRotation(); return; }
                    if (credential.RevocationOperationId != null) { SubmitRevocation(); return; }
                    authenticated(session, first); published = true;
                    Arm((int)Math.Max(1000, Math.Min(120000, packet.ExpiresInMilliseconds / 2)));
                }
                else if (action == "Rotate")
                {
                    if (packet.OperationId != credential!.RotationOperationId || packet.PrincipalId != credential.PrincipalId || packet.CredentialId != credential.NextCredentialId) throw new InvalidOperationException("RotationConflict");
                    Authenticate(true);
                }
                else if (action == "Revoke" || action == "RevocationStatus")
                {
                    if (packet.OperationId != credential!.RevocationOperationId || packet.PrincipalId != credential.PrincipalId || packet.CredentialId != credential.CredentialId) throw new InvalidOperationException("RevocationConflict");
                    var revoked = files!.ConfirmRevocation(server, packet.OperationId); credential.Dispose(); credential = revoked;
                    Fail("Credential revoked. Existing identity and operation records were retained.");
                }
                else throw new InvalidOperationException("UnknownIdentityResponse");
            }
            catch (Exception) { Fail("Secure identity or local credential storage is unavailable; no fallback was attempted."); }
        }
    }
    internal void RotateCredential()
    {
        lock (gate)
        {
            if (disposed || server == null || credential == null || session == "" || pendingKind != "") throw new InvalidOperationException("No idle authenticated identity.");
            var next = files!.BeginRotation(server); credential.Dispose(); credential = next; authenticated("", false); SubmitRotation();
        }
    }
    private void SubmitRotation()
    {
        byte[] next = credential!.CopyNextSecret();
        try { Send(new() { Kind = "Rotate", SessionId = session, OperationId = credential.RotationOperationId!, CredentialId = credential.CredentialId,
            NextCredentialId = credential.NextCredentialId!, NextSecret = Convert.ToBase64String(next) }); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(next); }
    }
    internal void RevokeCredential()
    {
        lock (gate)
        {
            if (disposed || server == null || credential == null || session == "" || pendingKind != "") throw new InvalidOperationException("No idle authenticated identity.");
            var pending = files!.BeginRevocation(server); credential.Dispose(); credential = pending; authenticated("", false); SubmitRevocation();
        }
    }
    private void QueryRevocation()
    {
        byte[] secret = credential!.CopySecret();
        try { Send(new() { Kind = "RevocationStatus", OperationId = credential.RevocationOperationId!, CredentialId = credential.CredentialId, Secret = Convert.ToBase64String(secret) }); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret); }
    }
    private void SubmitRevocation() => Send(new() { Kind = "Revoke", SessionId = session, OperationId = credential!.RevocationOperationId!, CredentialId = credential.CredentialId });
    private void Connecting(object? _, EventArgs args) { lock (gate) { if (!disposed && Required) Arm(12000); } }
    internal void RejectPreviousGreeting() { lock (gate) if (!disposed) Fail("This connection requires modern secure identity; the previous login protocol was refused."); }
    private void Tick(bool force = false)
    {
        lock (gate)
        {
            if (disposed || !Required || !transport.Connected) return;
            if (!force && (dueAt == long.MaxValue || Environment.TickCount64 < dueAt))
            {
                if (dueAt != long.MaxValue) timer.Change((int)Math.Max(1, Math.Min(int.MaxValue, dueAt - Environment.TickCount64)), Timeout.Infinite);
                return;
            }
            if (server == null) { Fail("Secure identity greeting timed out; no fallback was attempted."); return; }
            if (pendingKind != "") { Fail("Identity response timed out; the original pending operation is retained."); return; }
            try { Authenticate(); } catch { Fail("Identity renewal failed; reconnect to recover the saved identity."); }
        }
    }
    private void Fail(string reason)
    { Disarm(); failed(reason); transport.Disconnect(); }
    private void Disconnect(object? _, EventArgs args)
    {
        lock (gate) { if (disposed) return; Disarm(); credential?.Dispose(); credential = null; files = null; server = null; pendingKind = requestId = proof = session = ""; authenticatingCandidate = rotateAfterAuthentication = published = false; }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; Disconnect(null, EventArgs.Empty); disposed = true; }
        transport.UnregisterPacketHandler(IdentityWire.Module); transport.OnDisconnect -= Disconnect; transport.OnConnecting -= Connecting; timer.Dispose();
    }
}
#endif
