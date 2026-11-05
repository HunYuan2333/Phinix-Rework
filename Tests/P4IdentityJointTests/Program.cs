using System.Net;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Security.Authentication;
using Google.Protobuf;
using Utils;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Authentication;
using Connections;
using UserManagement;
using Utils.Framework;
using PhinixServer.Framework;
using ServerTransport;
using Microsoft.Data.Sqlite;

internal static class Program
{
    static int checks;
    static readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(120));
    static void Check(bool yes, string why) { Interlocked.Increment(ref checks); if (!yes) throw new Exception(why); }
    static string Id() => Guid.NewGuid().ToString("D");
    static async Task Wait(Func<bool> predicate) { while (!predicate()) await Task.Delay(10, Deadline.Token); }
    static X509Certificate2 Root()
    {
        using var key = ECDsa.Create(); var request = new CertificateRequest("CN=synthetic-identity-ca", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
    static X509Certificate2 Leaf(X509Certificate2 root)
    {
        using var key = ECDsa.Create(); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var issued = request.Create(root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        return issued.CopyWithPrivateKey(key);
    }
    static ConfiguredClientTransport Transport(string pin, string ca) => new((_, _) => new TlsNetClient("localhost", pin, ca, X509RevocationMode.NoCheck));
    static ClientAuthenticator Auth(IClientTransport transport, string root, bool modern)
    {
        var auth = new ClientAuthenticator(transport, (_, _, _, _, _) => throw new Exception("Interactive credential prompt forbidden"), Path.Combine(root, "old-client-credentials"), deferHistoricalCredentials: modern);
        if (modern) auth.ConfigureModernIdentity(() => true, Path.Combine(root, "modern-client"));
        return auth;
    }
    static async Task<IdentityEnvelope> Read(TlsPacketChannel channel)
    {
        var packet = TlsModulePacket.Decode((await channel.ReadAsync(Deadline.Token))!);
        Check(packet.Module == IdentityWire.Module, "Modern connection received a previous authentication greeting"); return IdentityWire.Decode(packet.Payload);
    }
    static async Task<IdentityEnvelope> Request(TlsPacketChannel channel, IdentityEnvelope request)
    {
        request.RequestId = Id(); await channel.WriteAsync(TlsModulePacket.Encode(IdentityWire.Module, IdentityWire.Encode(request), 1024 * 1024), Deadline.Token);
        var reply = await Read(channel); Check(reply.RequestId == request.RequestId, "Identity response lost request association"); return reply;
    }
    static long PrincipalCount(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM state WHERE id LIKE 'identity.principal/%'"; return (long)command.ExecuteScalar()!;
    }
    static void Hello(IClientTransport client, ClientAuthenticator auth, ClientUserManager users) => client.Send(FrameworkProtocol.ModuleName,
        FrameworkSerialization.SerializePacket(new FrameworkPacket { Kind = FrameworkProtocol.KindHello, Version = 2, MessageId = Id(), SessionId = auth.SessionId,
            SenderUuid = users.Uuid, PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload { Capabilities = new List<string>() }) }));
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "server") { ChildServer(args); return; }
        string directory = Path.Combine(Path.GetTempPath(), "phinix-p4-joint-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var ca = Root(); using var cert = Leaf(ca);
        string rootFile = Path.Combine(directory, "ca.pem"), certFile = Path.Combine(directory, "server.pem"), keyFile = Path.Combine(directory, "server.key");
        File.WriteAllText(rootFile, ca.ExportCertificatePem()); File.WriteAllText(certFile, cert.ExportCertificatePem());
        using (var key = cert.GetECDsaPrivateKey()) File.WriteAllText(keyFile, key!.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string pin = Convert.ToBase64String(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo()));
        try
        {
            using var server = new TlsNetServer(new IPEndPoint(IPAddress.Loopback, 0), cert, 8, 4);
            var serverAuth = new ServerAuthenticator(server, "identity-fixture", "synthetic", AuthTypes.ClientKey);
            var serverUsers = new ServerUserManager(server, serverAuth);
            string oldPath = Path.Combine(directory, "old-server-credentials"), userPath = Path.Combine(directory, "users"), dbPath = Path.Combine(directory, "identity.sqlite3");
            serverAuth.Load(oldPath); serverUsers.Load(userPath);
            var context = new ExtensionHostContext { HostKind = "server", StorageProvider = new FileSystemExtensionStorageProvider(Path.Combine(directory, "plugins")) };
            context.AddService<IServerUserManager>(serverUsers); var framework = new PhinixFrameworkServer(server, serverAuth, serverUsers, context);
            string oldPrincipal;
            server.Start();
            using (var old = Transport(pin, rootFile)) using (var auth = Auth(old, directory, false))
            {
                var users = new ClientUserManager(old, auth); auth.OnAuthenticationSuccess += (_, _) => users.SendLogin("original-player");
                old.Connect("127.0.0.1", server.LocalPort); await Wait(() => users.LoggedIn); oldPrincipal = users.Uuid;
                old.Disconnect(); await Wait(() => !users.LoggedIn);
            }
            string secondRoot = Path.Combine(directory, "second-player"); Directory.CreateDirectory(secondRoot); string secondPrincipal;
            using (var oldSecond = Transport(pin, rootFile)) using (var oldAuth = Auth(oldSecond, secondRoot, false))
            {
                var secondUsers = new ClientUserManager(oldSecond, oldAuth); oldAuth.OnAuthenticationSuccess += (_, _) => secondUsers.SendLogin("second-original-player");
                oldSecond.Connect("127.0.0.1", server.LocalPort); await Wait(() => secondUsers.LoggedIn); secondPrincipal = secondUsers.Uuid;
                oldSecond.Disconnect(); await Wait(() => !secondUsers.LoggedIn);
            }
            server.Stop(); var store = IdentityAuthorityBootstrap.Open(dbPath, true);
            using var endpoint = new PasswordlessIdentityEndpoint(server, serverAuth, store, serverUsers.IdentityAdapter.ResolveProof, "identity-fixture"); server.Start();
            string untrustedRoot = Path.Combine(directory, "untrusted"); Directory.CreateDirectory(untrustedRoot);
            using (var rejected = Transport(Convert.ToBase64String(new byte[32]), rootFile)) using (var rejectedAuth = Auth(rejected, untrustedRoot, true))
            {
                bool refused = false; try { rejected.Connect("127.0.0.1", server.LocalPort); } catch (AuthenticationException) { refused = true; }
                Check(refused && !rejected.Connected && !Directory.Exists(Path.Combine(untrustedRoot, "modern-client")) && PrincipalCount(dbPath) == 0,
                    "Untrusted connection reached modern credential creation or registration");
            }
            string unavailableRoot = Path.Combine(directory, "missing-user-directory"); Directory.CreateDirectory(unavailableRoot);
            using (var noDirectory = Transport(pin, rootFile)) using (var noDirectoryAuth = Auth(noDirectory, unavailableRoot, false))
            {
                int rejected = 0; noDirectoryAuth.ConfigureModernIdentity(() => true, ""); noDirectoryAuth.OnAuthenticationFailure += (_, _) => rejected++;
                noDirectory.Connect("127.0.0.1", server.LocalPort); await Wait(() => !noDirectory.Connected && rejected > 0);
                Check(PrincipalCount(dbPath) == 0 && !noDirectoryAuth.Authenticated, "Missing user storage silently selected the current game directory");
            }
            using var client = Transport(pin, rootFile); using var modern = Auth(client, directory, true);
            var modernUsers = new ClientUserManager(client, modern); modern.OnAuthenticationSuccess += (_, _) => modernUsers.SendLogin("migrated-player");
            int failed = 0; modern.OnAuthenticationFailure += (_, _) => Interlocked.Increment(ref failed);
            var capabilities = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.RegisterPacketHandler(FrameworkProtocol.ModuleName, (_, _, body) => { if (FrameworkSerialization.DeserializePacket(body).Kind == FrameworkProtocol.KindCapabilities) capabilities.TrySetResult(); });
            client.Connect("127.0.0.1", server.LocalPort); await Wait(() => modernUsers.LoggedIn || failed > 0);
            Check(failed == 0 && modern.Authenticated && modernUsers.Uuid == oldPrincipal, "Automatic migration failed or changed original UUID");
            var binding = new TrustedIdentityServer(store.ServerId, "localhost", Convert.FromBase64String(pin));
            var files = new IdentityCredentialDirectory(Path.Combine(directory, "modern-client")); using var initial = files.Load(binding);
            Check(initial.IsConfirmed && initial.PrincipalId == oldPrincipal && initial.ExpectedPrincipalId == oldPrincipal && PrincipalCount(dbPath) == 1, "Migration did not retain its original principal and pending association");
            Hello(client, modern, modernUsers); await capabilities.Task.WaitAsync(Deadline.Token);
            string connection = ""; await Wait(() => serverUsers.TryGetConnection(oldPrincipal, out _));
            serverUsers.TryGetConnection(oldPrincipal, out connection);
            var registry = (ServerRuntime.IServerSessionReadiness)typeof(PhinixFrameworkServer).GetField("sessionRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(framework)!;
            var clientAdmission = new PhinixClient.Framework.FrameworkAdmissionControlState();
            Check(clientAdmission.Begin(modern.SessionId, "p4-test-hello") && clientAdmission.ObserveCapabilities(modern.SessionId, "p4-test-hello"), "Client admission fixture did not negotiate");
            modern.SessionRenewed += (oldId, newId) => Check(clientAdmission.ReplaceSession(oldId, newId), "Client admission session replacement was not authorized");
            string beforeSession = modern.SessionId;
            object coordinator = typeof(ClientAuthenticator).GetField("modernIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modern)!;
            // A stale queued Timer callback must not treat a new pending request as already timed out.
            object coordinatorGate = coordinator.GetType().GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            lock (coordinatorGate)
            {
                var tick = coordinator.GetType().GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                tick.Invoke(coordinator, tick.GetParameters().Length == 0 ? null : new object[] { true }); // Schedule one real credential reauthentication now.
                tick.Invoke(coordinator, tick.GetParameters().Length == 0 ? null : new object[] { false }); // Old callback, before the new monotonic deadline.
                Check(client.Connected && modern.Authenticated && modern.SessionId == beforeSession,
                    "Stale timer callback timed out the newly scheduled identity request");
            }
            await Wait(() => modern.SessionId != beforeSession && modern.Authenticated);
            Check(!serverAuth.IsAuthenticated(connection, beforeSession) && registry.TryGetReady(connection, out var renewed) && renewed.SessionId == modern.SessionId, "Session refresh lost Framework readiness or retained old token");
            Check(!clientAdmission.NegotiationRejected && !clientAdmission.MayTimeoutFallback, "Renewal reopened legacy fallback or rejected modern admission");
            modern.RotateIdentityCredential(); await Wait(() => modern.Authenticated || !client.Connected || failed > 0);
            Check(client.Connected && failed == 0, "Credential rotation interrupted the authenticated connection");
            using var rotated = files.Load(binding);
            Check(rotated.RotationOperationId == null && rotated.CredentialId != initial.CredentialId && rotated.PrincipalId == oldPrincipal && store.Authenticate(initial.CredentialId, initial.CopySecret()) == null,
                "Rotation did not durably promote candidate and revoke old material");
            Check(registry.TryGetReady(connection, out var afterRotation) && afterRotation.SessionId == modern.SessionId, "Rotation lost negotiated capabilities");
            client.Disconnect(); await Wait(() => !modernUsers.LoggedIn);
            string historicalClientPath = Path.Combine(directory, "old-client-credentials"); byte[] historicalClient = File.ReadAllBytes(historicalClientPath);
            File.WriteAllBytes(historicalClientPath, new byte[] { 255, 255 });
            using (var freshTransport = Transport(pin, rootFile)) using (var freshAuth = Auth(freshTransport, directory, true))
            {
                var freshUsers = new ClientUserManager(freshTransport, freshAuth); freshAuth.OnAuthenticationSuccess += (_, _) => freshUsers.SendLogin("modern-only-reconnect");
                freshTransport.Connect("127.0.0.1", server.LocalPort); await Wait(() => freshUsers.LoggedIn);
                Check(freshUsers.Uuid == oldPrincipal, "Confirmed modern identity still depended on historical credential parsing");
                freshTransport.Disconnect(); await Wait(() => !freshUsers.LoggedIn);
            }
            File.WriteAllBytes(historicalClientPath, historicalClient);
            client.Connect("127.0.0.1", server.LocalPort); await Wait(() => modernUsers.LoggedIn);
            Check(modernUsers.Uuid == oldPrincipal && PrincipalCount(dbPath) == 1, "Reconnect duplicated or changed identity");
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync(IPAddress.Loopback, server.LocalPort, Deadline.Token);
                await using var channel = await TlsPacketChannel.ConnectAsync(socket.GetStream(), "localhost", Convert.FromBase64String(pin), ca, X509RevocationMode.NoCheck, cancellation: Deadline.Token);
                var hello = await Read(channel); Check(hello.Kind == "Hello" && hello.ServerId == store.ServerId, "Bootstrap identity differs");
                var cross = await Request(channel, new() { Kind = "Renew", ServerId = store.ServerId, SessionId = modern.SessionId });
                Check(cross.Failure == "Refused" && modern.Authenticated && client.Connected, "Another connection renewed a session it did not own");
                var previous = CredentialStore.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(directory, "old-client-credentials")));
                var replay = await Request(channel, new() { Kind = "Enroll", ServerId = store.ServerId, OperationId = initial.OperationId, CredentialId = initial.CredentialId,
                    Secret = Convert.ToBase64String(initial.CopySecret()), PreviousProof = previous.ClientKey, PrincipalId = oldPrincipal });
                Check(replay.Failure == "" && replay.PrincipalId == oldPrincipal && store.Authenticate(initial.CredentialId, initial.CopySecret()) == null,
                    "Original migration replay lost result or reactivated revoked credential");
                var conflict = await Request(channel, new() { Kind = "Enroll", ServerId = store.ServerId, OperationId = initial.OperationId, CredentialId = Id(),
                    Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), PreviousProof = previous.ClientKey, PrincipalId = oldPrincipal });
                Check(conflict.Failure == "Conflict" && PrincipalCount(dbPath) == 1, "Conflicting migration changed the identity ledger");
            }
            modern.RevokeIdentityCredential(); await Wait(() => !client.Connected);
            using var revoked = files.Load(binding);
            Check(revoked.IsRevoked && store.Authenticate(rotated.CredentialId, rotated.CopySecret()) == null, "Revocation was not persisted on both sides");
            int beforeRevokedReconnect = failed;
            client.Connect("127.0.0.1", server.LocalPort); await Wait(() => !client.Connected && failed > beforeRevokedReconnect);
            Check(PrincipalCount(dbPath) == 1 && !modern.Authenticated, "Revoked client silently registered a replacement identity");
            using (var raw = new TcpClient())
            {
                await raw.ConnectAsync(IPAddress.Loopback, server.LocalPort, Deadline.Token);
                await using var channel = await TlsPacketChannel.ConnectAsync(raw.GetStream(), "localhost", Convert.FromBase64String(pin), ca, X509RevocationMode.NoCheck, cancellation: Deadline.Token);
                await Read(channel); bool limited = false;
                for (int i = 0; i < 12; i++)
                {
                    var refusal = await Request(channel, new() { Kind = "Enroll", ServerId = store.ServerId, OperationId = Id() });
                    limited |= refusal.Failure == "RateLimited";
                }
                Check(limited && PrincipalCount(dbPath) == 1, "Registration budget failed or invalid payload created a principal");
            }
            using (var raw = new TcpClient())
            {
                await raw.ConnectAsync(IPAddress.Loopback, server.LocalPort, Deadline.Token);
                await using var channel = await TlsPacketChannel.ConnectAsync(raw.GetStream(), "localhost", Convert.FromBase64String(pin), ca, X509RevocationMode.NoCheck, cancellation: Deadline.Token);
                await Read(channel);
                var previous = CredentialStore.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(directory, "old-client-credentials")));
                var packet = ProtobufPacketHelper.Pack(new AuthenticatePacket { AuthType = AuthTypes.ClientKey, Username = previous.ClientKey, SessionId = Id() }).ToByteArray();
                await channel.WriteAsync(TlsModulePacket.Encode("Authentication", packet, 1024 * 1024), Deadline.Token);
                Check(await channel.ReadAsync(Deadline.Token) == null && PrincipalCount(dbPath) == 1, "Previous authentication entry was not retired in modern mode");
            }
            server.Stop(); endpoint.Dispose(); framework.ShutdownExtensions(); serverAuth.Stop();
            string freshPrincipal = await ProcessRecovery(directory, pin, rootFile, oldPrincipal, "new-player", null, store.ServerId);
            await ProcessRecovery(directory, pin, rootFile, oldPrincipal, "second-player", secondPrincipal, store.ServerId);
            await CredentialTransitionRecovery(directory, pin, rootFile, freshPrincipal, store.ServerId, "rotate");
            await CredentialTransitionRecovery(directory, pin, rootFile, freshPrincipal, store.ServerId, "revoke");
            await ProductionMain(directory, pin, rootFile, secondPrincipal, args[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, profile = "P4 modern TLS identity + real client + independent server restart", faultIds = new[] { "identity.enroll.after-commit-before-response", "identity.rotate.after-commit-before-response", "identity.revoke.after-commit-before-response" }, processKills = 4, productionMainValidated = true, chatReplyMentionValidated = true, emptyTradeCreateValidated = true, nativeGameValidated = false, secretsInOutput = false }));
        }
        finally { Directory.Delete(directory, true); }
    }
    static void ChildServer(string[] args)
    {
        string root = args[1]; using var certificate = X509Certificate2.CreateFromPemFile(Path.Combine(root, "server.pem"), Path.Combine(root, "server.key"));
        using var server = new TlsNetServer(new IPEndPoint(IPAddress.Loopback, 0), certificate, 8, 4);
        var auth = new ServerAuthenticator(server, "identity-fixture", "synthetic", AuthTypes.ClientKey); var users = new ServerUserManager(server, auth);
        auth.Load(Path.Combine(root, "old-server-credentials")); users.Load(Path.Combine(root, "users"));
        var store = IdentityAuthorityBootstrap.Open(Path.Combine(root, "identity.sqlite3"), false);
        Action<string> barrier = phase => { if (phase == args[2]) { File.WriteAllText(Path.Combine(root, "fault-ready"), phase); Thread.Sleep(Timeout.Infinite); } };
        using var endpoint = (PasswordlessIdentityEndpoint)Activator.CreateInstance(typeof(PasswordlessIdentityEndpoint), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { server, auth, store, (Func<string, string>)users.IdentityAdapter.ResolveProof, "identity-fixture", barrier }, null)!;
        server.Start(); File.WriteAllText(Path.Combine(root, "port.tmp"), server.LocalPort.ToString()); File.Move(Path.Combine(root, "port.tmp"), Path.Combine(root, "port"), true); Thread.Sleep(Timeout.Infinite);
    }
    static Process StartChild(string root, string fault)
    {
        File.Delete(Path.Combine(root, "port")); File.Delete(Path.Combine(root, "fault-ready"));
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "server", root, fault }) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }
    static async Task<string> ProcessRecovery(string directory, string pin, string ca, string original, string clientFolder, string expectedPrincipal, string serverId)
    {
        string newClient = Path.Combine(directory, clientFolder); Directory.CreateDirectory(newClient);
        using var client = Transport(pin, ca); using var auth = Auth(client, newClient, true); var users = new ClientUserManager(client, auth);
        auth.OnAuthenticationSuccess += (_, _) => users.SendLogin("new-player");
        long before = PrincipalCount(Path.Combine(directory, "identity.sqlite3"));
        string operation = "", committedPrincipal = "";
        using (var child = StartChild(directory, "identity.enroll.after-commit-before-response"))
        {
            try
            {
                await Wait(() => File.Exists(Path.Combine(directory, "port")) || child.HasExited); Check(!child.HasExited, "Independent server failed startup");
                client.Connect("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port")))); await Wait(() => File.Exists(Path.Combine(directory, "fault-ready")) || child.HasExited);
                Check(!child.HasExited && PrincipalCount(Path.Combine(directory, "identity.sqlite3")) == before + 1, "Post-COMMIT barrier did not retain the committed registration");
                using var pending = new IdentityCredentialDirectory(Path.Combine(newClient, "modern-client")).Load(new TrustedIdentityServer(serverId, "localhost", Convert.FromBase64String(pin)));
                operation = pending.OperationId;
                committedPrincipal = StateField(Path.Combine(directory, "identity.sqlite3"), "identity.credential/" + pending.CredentialId, "PrincipalId");
                Check(!pending.IsConfirmed && (expectedPrincipal == null || committedPrincipal == expectedPrincipal), "Lost response changed the original UUID or prematurely confirmed the client");
                Check(OperationCount(Path.Combine(directory, "identity.sqlite3"), operation) == 1, "Committed registration operation is not unique");
                child.Kill(true); Check(child.WaitForExit(10000), "Fault server did not terminate"); await Wait(() => !client.Connected);
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(10000); } }
        }
        using (var child = StartChild(directory, "normal"))
        {
            try
            {
                await Wait(() => File.Exists(Path.Combine(directory, "port")) || child.HasExited); Check(!child.HasExited, "Independent server failed to reopen authority");
                client.Connect("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port")))); await Wait(() => users.LoggedIn);
                Check(users.Uuid == committedPrincipal && (expectedPrincipal == null || users.Uuid == expectedPrincipal) && PrincipalCount(Path.Combine(directory, "identity.sqlite3")) == before + 1, "Lost registration response changed UUID or created a second principal after restart");
                using var recovered = new IdentityCredentialDirectory(Path.Combine(newClient, "modern-client")).Load(new TrustedIdentityServer(serverId, "localhost", Convert.FromBase64String(pin)));
                Check(recovered.IsConfirmed && recovered.OperationId == operation && OperationCount(Path.Combine(directory, "identity.sqlite3"), operation) == 1, "Recovery did not retain the exact original operation");
                var persisted = CredentialStore.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(directory, "old-server-credentials")));
                Check(persisted.Credentials.Count == 2, "Modern recovery added another historical credential");
                client.Disconnect(); await Wait(() => !users.LoggedIn);
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(10000); } }
        }
        return committedPrincipal;
    }
    static string StateField(string path, string key, string field)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT value FROM state WHERE id=$id"; command.Parameters.AddWithValue("$id", key);
        using var value = JsonDocument.Parse((byte[])command.ExecuteScalar()!); return value.RootElement.GetProperty(field).GetString()!;
    }
    static long OperationCount(string path, string operation)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM operations WHERE operation=$id"; command.Parameters.AddWithValue("$id", operation); return (long)command.ExecuteScalar()!;
    }
    static async Task CredentialTransitionRecovery(string directory, string pin, string ca, string principal, string serverId, string kind)
    {
        string clientRoot = Path.Combine(directory, "new-player"), db = Path.Combine(directory, "identity.sqlite3");
        var files = new IdentityCredentialDirectory(Path.Combine(clientRoot, "modern-client")); var binding = new TrustedIdentityServer(serverId, "localhost", Convert.FromBase64String(pin));
        using var client = Transport(pin, ca); using var auth = Auth(client, clientRoot, true); var users = new ClientUserManager(client, auth);
        auth.OnAuthenticationSuccess += (_, _) => users.SendLogin("transition-player");
        string operation = "", nextCredential = "";
        using (var child = StartChild(directory, "identity." + kind + ".after-commit-before-response"))
        {
            try
            {
                await Wait(() => File.Exists(Path.Combine(directory, "port")) || child.HasExited); Check(!child.HasExited, "Transition fault server did not start");
                client.Connect("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port")))); await Wait(() => users.LoggedIn);
                if (kind == "rotate") auth.RotateIdentityCredential(); else auth.RevokeIdentityCredential();
                await Wait(() => File.Exists(Path.Combine(directory, "fault-ready")) || child.HasExited); Check(!child.HasExited, "Transition barrier was not reached");
                using var pending = files.Load(binding); operation = kind == "rotate" ? pending.RotationOperationId! : pending.RevocationOperationId!; nextCredential = pending.NextCredentialId ?? "";
                Check(operation != "" && OperationCount(db, operation) == 1, "Transition intent was not saved before the server commit");
                child.Kill(true); Check(child.WaitForExit(10000), "Transition worker did not terminate"); await Wait(() => !client.Connected);
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(10000); } }
        }
        using (var child = StartChild(directory, "normal"))
        {
            try
            {
                await Wait(() => File.Exists(Path.Combine(directory, "port")) || child.HasExited); Check(!child.HasExited, "Transition recovery server did not start");
                client.Connect("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port"))));
                if (kind == "rotate") await Wait(() => users.LoggedIn); else await Wait(() => !client.Connected);
                using var recovered = files.Load(binding);
                Check(recovered.PrincipalId == principal && OperationCount(db, operation) == 1, "Transition recovery changed the stable identity or duplicated the operation");
                Check(kind == "rotate" ? recovered.CredentialId == nextCredential && recovered.RotationOperationId == null : recovered.IsRevoked && !auth.Authenticated,
                    "Lost transition response was not reconciled with durable authority");
                if (client.Connected) { client.Disconnect(); await Wait(() => !users.LoggedIn); }
            }
            finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(10000); } }
        }
    }

    static async Task ProductionMain(string directory, string pin, string ca, string principal, string entrypoint)
    {
        var config = new PhinixServer.Config { Address = IPAddress.Loopback, Port = 0, MaxConnections = 8,
            CredentialDatabasePath = Path.Combine(directory, "old-server-credentials"), UserDatabasePath = Path.Combine(directory, "users"),
            LogPath = Path.Combine(directory, "production.log"), Tls = new PhinixServer.TlsServerSettings {
                CertificateFile = Path.Combine(directory, "server.pem"), PrivateKeyFile = Path.Combine(directory, "server.key"), MaximumHandshakes = 4 },
            ModernIdentity = new PhinixServer.ModernIdentitySettings { DatabaseFile = Path.Combine(directory, "identity.sqlite3"), InitializeNew = false } };
        config.Save(Path.Combine(directory, "server.conf"));
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(entrypoint); using var host = Process.Start(start)!;
        var port = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OutputDataReceived += (_, e) => { if (e.Data == null) return; var match = Regex.Match(e.Data, @"listening on .*:(\d+)"); if (match.Success) port.TrySetResult(int.Parse(match.Groups[1].Value)); };
        host.BeginOutputReadLine(); host.BeginErrorReadLine();
        try
        {
            await Wait(() => port.Task.IsCompleted || host.HasExited); Check(!host.HasExited, "Published modern server Main failed startup"); int boundPort = await port.Task;
            string ownerRoot = Path.Combine(directory, "second-player"), peerRoot = Path.Combine(directory, "production-peer"); Directory.CreateDirectory(peerRoot);
            using var owner = Transport(pin, ca); using var ownerAuth = Auth(owner, ownerRoot, true); var ownerUsers = new ClientUserManager(owner, ownerAuth);
            using var peer = Transport(pin, ca); using var peerAuth = Auth(peer, peerRoot, true); var peerUsers = new ClientUserManager(peer, peerAuth);
            ownerAuth.OnAuthenticationSuccess += (_, _) => ownerUsers.SendLogin("production-owner"); peerAuth.OnAuthenticationSuccess += (_, _) => peerUsers.SendLogin("production-peer");
            var ownerFrames = new ConcurrentQueue<FrameworkPacket>(); var peerFrames = new ConcurrentQueue<FrameworkPacket>();
            owner.RegisterPacketHandler(FrameworkProtocol.ModuleName, (_, _, body) => ownerFrames.Enqueue(FrameworkSerialization.DeserializePacket(body)));
            peer.RegisterPacketHandler(FrameworkProtocol.ModuleName, (_, _, body) => peerFrames.Enqueue(FrameworkSerialization.DeserializePacket(body)));
            owner.Connect("127.0.0.1", boundPort); peer.Connect("127.0.0.1", boundPort); await Wait(() => ownerUsers.LoggedIn && peerUsers.LoggedIn);
            Check(ownerUsers.Uuid == principal && peerUsers.Uuid != principal, "Published Main changed the migrated identity or mixed principals");
            string[] capabilities = { "builtin.trade", "trade.create.request", "trade.create.response", "trade.state.snapshot", "trade.status.update.request", "trade.status.update.response", "trade.cancelled.event", "builtin.chat.message", "builtin.chat.history.request", "builtin.chat.history.sync-complete" };
            void Negotiate(IClientTransport client, ClientAuthenticator auth, ClientUserManager users) => client.Send(FrameworkProtocol.ModuleName,
                FrameworkSerialization.SerializePacket(new FrameworkPacket { Kind = FrameworkProtocol.KindHello, Version = 2, MessageId = Id(), SessionId = auth.SessionId, SenderUuid = users.Uuid,
                    PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload { Capabilities = capabilities.ToList() }) }));
            Negotiate(owner, ownerAuth, ownerUsers); Negotiate(peer, peerAuth, peerUsers);
            await Wait(() => ownerFrames.Any(f => f.Kind == FrameworkProtocol.KindCapabilities) && peerFrames.Any(f => f.Kind == FrameworkProtocol.KindCapabilities));
            var negotiated = FrameworkSerialization.DeserializePayload<FrameworkCapabilitiesPayload>(ownerFrames.First(f => f.Kind == FrameworkProtocol.KindCapabilities).PayloadJson);
            Check(negotiated.Capabilities.Contains("trade.create.request") && negotiated.Capabilities.Contains("builtin.chat.message"), "Production plugins did not negotiate through modern authentication");
            void Chat(IClientTransport client, ClientAuthenticator auth, ClientUserManager users, Phinix.Framework.BuiltInChatMessagePayload message) => client.Send(FrameworkProtocol.ModuleName,
                FrameworkSerialization.SerializePacket(new FrameworkPacket { Kind = FrameworkProtocol.KindMessage, Flow = Phinix.Framework.FrameworkFlow.Message, Version = 2, MessageId = Id(), SessionId = auth.SessionId,
                    SenderUuid = users.Uuid, MessageType = "builtin.chat.message", PayloadBytes = message.ToByteArray() }));
            Chat(owner, ownerAuth, ownerUsers, new() { Message = "synthetic-p4-base" });
            await Wait(() => peerFrames.Any(f => f.MessageType == "builtin.chat.message"));
            var basis = Phinix.Framework.BuiltInChatMessagePayload.Parser.ParseFrom(peerFrames.First(f => f.MessageType == "builtin.chat.message").PayloadBytes);
            Check(basis.SenderUuid == ownerUsers.Uuid && basis.Message.Contains("synthetic-p4-base"), "Modern TLS changed chat sender or body");
            var reply = new Phinix.Framework.BuiltInChatMessagePayload { Message = "synthetic-p4-reply", ReplyToMessageId = basis.MessageId, ReplyToSnippet = "synthetic-p4-base" }; reply.MentionedUuids.Add(ownerUsers.Uuid);
            Chat(peer, peerAuth, peerUsers, reply);
            await Wait(() => ownerFrames.Any(f => f.MessageType == "builtin.chat.message" && Phinix.Framework.BuiltInChatMessagePayload.Parser.ParseFrom(f.PayloadBytes).Message.Contains("synthetic-p4-reply")));
            var received = ownerFrames.Where(f => f.MessageType == "builtin.chat.message").Select(f => Phinix.Framework.BuiltInChatMessagePayload.Parser.ParseFrom(f.PayloadBytes)).First(f => f.Message.Contains("synthetic-p4-reply"));
            Check(received.SenderUuid == peerUsers.Uuid && received.ReplyToMessageId == basis.MessageId && received.ReplyToSnippet == "synthetic-p4-base" && received.MentionedUuids.SequenceEqual(new[] { ownerUsers.Uuid }),
                "Modern production chat dropped reply or mention semantics");
            peerUsers.UpdateSelf(acceptingTrades: true); await Wait(() => ownerUsers.TryGetAcceptingTrades(peerUsers.Uuid, out var accepting) && accepting);
            string create = Id(); owner.Send(FrameworkProtocol.ModuleName, FrameworkSerialization.SerializePacket(new FrameworkPacket {
                Kind = FrameworkProtocol.KindCommand, CommandKind = Phinix.Framework.FrameworkCommandKind.Request, Flow = Phinix.Framework.FrameworkFlow.Command, Version = 2, MessageId = create, SessionId = ownerAuth.SessionId, SenderUuid = ownerUsers.Uuid,
                MessageType = "trade.create.request", PayloadJson = FrameworkSerialization.SerializePayload(new Phinix.TradeExtension.FrameworkTradeCreateRequest { OtherPartyUuid = peerUsers.Uuid }) }));
            await Wait(() => ownerFrames.Any(f => f.MessageType == "trade.create.response"));
            var created = FrameworkSerialization.DeserializePayload<Phinix.TradeExtension.FrameworkTradeCreateResponse>(ownerFrames.First(f => f.MessageType == "trade.create.response").PayloadJson);
            Check(created.Success && !string.IsNullOrEmpty(created.TradeId) && created.OtherPartyUuid == peerUsers.Uuid, "Modern production identity could not create a durable empty quote");
            await Wait(() => peerFrames.Any(f => f.MessageType == "trade.state.snapshot")); Check(true, "Peer received the actual trade snapshot");
            owner.Disconnect(); peer.Disconnect(); await Wait(() => !ownerUsers.LoggedIn && !peerUsers.LoggedIn);
            await host.StandardInput.WriteLineAsync("stop"); await host.StandardInput.FlushAsync(); await host.WaitForExitAsync(Deadline.Token);
            Check(host.ExitCode == 0 && File.Exists(config.CredentialDatabasePath + ".modern-identity-required"), "Published Main did not stop cleanly or pin retired identity mode");
            Check(File.Exists(Path.Combine(directory, "identity-authority.required")), "Production Main did not pin the data-directory authority");
            string configuration = Path.Combine(directory, "server.conf"); byte[] userSnapshot = File.ReadAllBytes(config.UserDatabasePath);
            File.Move(configuration, configuration + ".saved");
            using (var missingConfig = Process.Start(start)!)
            {
                await missingConfig.WaitForExitAsync(Deadline.Token);
                Check(missingConfig.ExitCode != 0 && !File.Exists(configuration) && userSnapshot.SequenceEqual(File.ReadAllBytes(config.UserDatabasePath)),
                    "Missing configuration silently created default identities or overwrote player records");
            }
            File.Move(configuration + ".saved", configuration);
            var modernSettings = config.ModernIdentity; config.ModernIdentity = null; config.CredentialDatabasePath = Path.Combine(directory, "unrecognized-old-source"); config.Save(configuration);
            using (var downgraded = Process.Start(start)!)
            {
                await downgraded.WaitForExitAsync(Deadline.Token);
                Check(downgraded.ExitCode != 0 && !File.Exists(config.CredentialDatabasePath), "Changed credential path bypassed the modern authority guard");
            }
            config.ModernIdentity = modernSettings; config.CredentialDatabasePath = Path.Combine(directory, "old-server-credentials"); config.Save(configuration);
        }
        finally { if (!host.HasExited) { host.Kill(true); host.WaitForExit(10000); } }
    }

}
