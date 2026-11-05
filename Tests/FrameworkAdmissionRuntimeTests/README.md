# Framework admission control checks

This console project source-links the production, game-independent admission policy. It uses the client's pinned Common serializers; it does not load the server assembly or a mocked game engine.

From the client repository, build with the installed SDK and run:

```sh
MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.112/Sdks dotnet build Tests/FrameworkAdmissionRuntimeTests/FrameworkAdmissionRuntimeTests.csproj -c Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
dotnet Tests/FrameworkAdmissionRuntimeTests/bin/Release/net10.0/FrameworkAdmissionRuntimeTests.dll --server-fixture /home/hunyuan2333/Phinix/Phinix-Rework-Server/Tests/Fixtures/FrameworkControl/normalized.json
```

The fixture is explicitly synthetic, versioned server encoding with independent literal expectations in this client; it is not a captured legacy packet. Tests cover matched rejection, cross-session/correlation refusal, no downgrade after modern evidence, fresh reconnect, malformed control, and rejection of success/retry/side-effect claims. Recent request tracking is bounded and ephemeral, not a durable operation ledger.

`--probe PORT` is used by the server repository's `tools/quality/admission_probe.py` against its isolated published server. It performs real ClientKey authentication/login followed by a version rejection and an unsupported-capability rejection in two connections. The production policy and client serializers are real; the complete Unity Framework endpoint/UI is not executed here. No game inventory, save, delivery ACK, or full modern profile acceptance is claimed.

The new client requires a correlated capabilities response and must be tested with the matching server candidate. Explicit modern refusal never authorizes automatic legacy resend or refund. Historical no-response fallback still exists in the host and remains P5 migration work.


Trade restart probe adds a test-only reference to this client's pinned Trade contracts. The server repository's `tools/quality/trade_network_probe.py` orchestrates `--trade-probe PORT PHASE PRIVATE_STATE_PATH`: two real UDP peers create/offer, a published host is killed, then fresh peers reuse private synthetic credentials and check the original UUID/snapshot/result without inventing cache items. Every received Framework envelope must match its actual recipient session. The login-failure phase requires refusal when identity snapshots cannot be saved. Explicit UserUpdate sets AcceptingTrades; the historical Login default is preserved. Keys live only in a private temporary state file deleted by the orchestrator; never include it in artifacts. This is a Headless profile subset with no game inventory/save evidence or production-client UI execution.
