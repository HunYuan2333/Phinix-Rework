# Packaged Trade offer regression

Runs the actual Trade DLL's adapter, service request builder and serializer. Only the external encoder, session and transport are synthetic; no fake transaction is accepted as product evidence. The literal expected capability is independently specified as `builtin.trade`, rather than copied from the packet helper or server Adapter.

Prepare `ProbeReferences` by copying a candidate's 1.7 Assemblies/Extensions DLLs under their unnumbered assembly filenames into an isolated directory. Use compile-only game references; never copy them into the candidate ZIP.

```sh
dotnet run --project Tests/TradeOfferWireTests -c Release \
  -p:ProbeReferences=/tmp/probe-refs -p:GameReferences=/path/to/game-references
```

Optional single program argument: an isolated output directory. This writes `item.bin` and `offer.bin` using the actual adapter with a single synthetic opaque item `[17,0,255]`. Set `PHINIX_TEST_ADAPTER_WIRE` to that directory when running the server's `tools/quality/trade_network_probe.py`. Its initial phase uses these packets instead of manually constructing a correctly labelled item. Only recipient session/principal, dynamic trade target and command Kind normalization are supplied by the network harness; the item's capability, ID, bytes and offer references remain untouched. Restart replays the original MessageId, distinct from reply correlation.

The 1.6 DLL can also be exercised with its package references under this .NET 10 component runner. This is not proof of execution in RimWorld 1.6/Mono. Neither runner proves physical items, inventory materialization, durable game ACK or save/reload correctness. The existing third-party claimed outgoing-item path does not return its transmitted ID through the current transport API and is outside this unclaimed-handler regression.
