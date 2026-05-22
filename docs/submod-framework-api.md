# Phinix Framework API for Submods

## Summary
This document describes the Phase 2 public extension surface for Phinix submod authors.

Phase 2 is about stabilizing framework contracts, not migrating every built-in Phinix feature yet.
That means:

- the message pipeline is usable now
- capability negotiation is enforced now
- reflection-based discovery is usable now
- item codec and trade completion contracts are defined now
- stock trading/chat behavior is not fully migrated onto those contracts until Phase 3

## Discovery Model
Phinix discovers extensions by scanning loaded assemblies for classes that:

- have a public parameterless constructor
- implement one or more framework interfaces
- optionally carry `[PhinixExtension("your.extension.id")]`

Use a globally unique extension ID. A reverse-DNS-style or package-style ID is recommended.

Example:

```csharp
[PhinixExtension("sample.red_packet")]
public class RedPacketClientExtension : IPhinixExtension, ICapabilityProvider, IClientMessageHandler, IMessageRenderer
{
    public string ExtensionId => "sample.red_packet";
    public int Priority => 100;
}
```

If extension construction fails, Phinix now soft-fails and logs a warning instead of crashing bootstrap.

## Capability Negotiation
After login, the client and server exchange capability sets.

Current Phase 2 rule:

- a framework message type should also be treated as its required capability ID
- if the remote side did not negotiate that capability, the message should not be sent or broadcast

Practical implication:

- if your extension emits `MessageType = "sample.red_packet"`, your capability provider should also expose `sample.red_packet`

Example:

```csharp
public IEnumerable<string> GetCapabilities()
{
    yield return ExtensionId;
}
```

On the client, unsupported framework capabilities surface as user-visible framework messages.
On the server, unnegotiated framework messages are rejected with warnings.

## Message Pipeline Contracts
Phase 2 stabilizes three main message roles:

- `IClientMessageHandler`
- `IServerMessageHandler`
- `IMessageRenderer`

### `IClientMessageHandler`
Use this when the client needs to:

- convert outgoing user text into framework envelopes
- handle incoming framework envelopes before renderers run

```csharp
public interface IClientMessageHandler : IMessageHandler
{
    bool CanHandleOutgoingText(string rawMessage);
    ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context);

    bool CanHandleIncomingEnvelope(FrameworkEnvelope envelope);
    ClientIncomingMessageResult HandleIncomingEnvelope(FrameworkEnvelope envelope, ClientFrameworkContext context);
}
```

### `IServerMessageHandler`
Use this when the server needs to:

- validate or transform incoming framework envelopes
- broadcast them
- fan them out selectively
- trigger other server-side behavior

```csharp
public interface IServerMessageHandler : IMessageHandler
{
    bool CanHandleIncomingEnvelope(FrameworkEnvelope envelope);
    ServerIncomingMessageResult HandleIncomingEnvelope(FrameworkEnvelope envelope, ServerFrameworkContext context);
}
```

### `IMessageRenderer`
Use this when your incoming framework envelope should render into chat/UI output.

```csharp
public interface IMessageRenderer
{
    bool CanRender(FrameworkEnvelope envelope);
    FrameworkDisplayMessage Render(FrameworkEnvelope envelope);
}
```

## Handler Result Semantics
Phase 2 formalizes handler result actions through `MessageHandlingResultAction`.

Available actions:

- `Continue`: continue to the next handler or renderer
- `Handled`: consume the message and stop propagation
- `ReplacePayload`: replace the working envelope and continue
- `SuppressDefault`: suppress default display behavior
- `StopPropagation`: stop later handlers from running
- `LegacyFallback`: abandon framework handling and fall back to legacy/default flow

### Outgoing Result

```csharp
public sealed class ClientOutgoingMessageResult
{
    public MessageHandlingResultAction Action { get; set; }
    public FrameworkEnvelope Envelope { get; set; }
}
```

Typical patterns:

- return `Handled + Envelope` to send a framework message
- return `Handled` with no envelope to consume input without sending
- return `LegacyFallback` to let stock chat send the message normally

### Incoming Client Result

```csharp
public sealed class ClientIncomingMessageResult
{
    public MessageHandlingResultAction Action { get; set; }
    public FrameworkEnvelope Envelope { get; set; }
    public FrameworkDisplayMessage DisplayMessage { get; set; }
}
```

Typical patterns:

- return `Handled + DisplayMessage` to fully handle and render
- return `ReplacePayload + Envelope` to normalize one extension payload into another envelope shape
- return `Continue` to let renderers or later handlers keep working

### Incoming Server Result

```csharp
public sealed class ServerIncomingMessageResult
{
    public MessageHandlingResultAction Action { get; set; }
    public FrameworkEnvelope Envelope { get; set; }
}
```

Typical patterns:

- perform side effects through `ServerFrameworkContext`, then return `Handled`
- return `ReplacePayload + Envelope` if later handlers should see a rewritten envelope

## Display Messages and Localization
Phase 2 keeps payloads language-agnostic and moves localization to the client render layer.

Use `FrameworkDisplayMessage` for rendered output.

Important fields:

- `Text`: final fallback text
- `TranslationKey`: keyed translation entry
- `TranslationArgs`: string arguments for keyed translation
- `SuppressDefaultDisplay`: whether default display should be suppressed

Recommended pattern:

- network payload carries structured data only
- renderer converts payload to `FrameworkDisplayMessage`
- renderer prefers `TranslationKey + TranslationArgs`
- `Text` stays available as a fallback

Example:

```csharp
return new FrameworkDisplayMessage
{
    MessageId = envelope.MessageId,
    SenderUuid = envelope.SenderUuid,
    TimestampUtcTicks = envelope.TimestampUtcTicks,
    Source = "red_packet",
    TranslationKey = "Phinix_framework_redPacketMessage",
    TranslationArgs = new List<string> { payload.Body }
};
```

## Interceptors
`IMessageInterceptor` is the framework seam for suppressing or filtering display output.

```csharp
public interface IMessageInterceptor
{
    int Priority { get; }
    MessageHandlingResultAction Intercept(FrameworkDisplayMessage message);
}
```

Use this when you want to:

- suppress stock display for certain messages
- stop blocked content from reaching default UI
- layer custom filtering on top of rendered output

## Context Objects
### `ClientFrameworkContext`
Key fields:

- `SessionId`
- `SenderUuid`
- `CompatibilityMode`
- `SendEnvelope`
- `RemoteCapabilities`
- `HasRemoteCapability`
- `Log`

### `ServerFrameworkContext`
Key fields:

- `ConnectionId`
- `SessionId`
- `SenderUuid`
- `SendEnvelope`
- `BroadcastEnvelope`
- `IsConnectionFrameworkCapable`
- `RemoteCapabilities`
- `ServerCapabilities`
- `HasRemoteCapability`
- `ConnectionHasCapability`
- `Log`

## Item Codec and Trade Completion Contracts
These contracts are defined in Phase 2 so submod authors can code against a stable shape.
`IItemCodec` is a general-purpose item payload contract, not a trade-only interface.
Trading is currently the first built-in consumer of this pipeline, and other submods are expected to reuse the same contract directly.

### `IItemCodec`

```csharp
public interface IItemCodec
{
    string CodecId { get; }
    bool CanEncode(object item, ItemCodecContext context);
    FrameworkItemPayload Encode(object item, ItemCodecContext context);
    bool CanDecode(FrameworkItemPayload payload, ItemCodecContext context);
    object Decode(FrameworkItemPayload payload, ItemCodecContext context);
}
```

### `ITradeCompletionHandler`

```csharp
public interface ITradeCompletionHandler
{
    string HandlerId { get; }
    int Priority { get; }
    bool CanHandle(TradeCompletionContext context);
    void Handle(TradeCompletionContext context);
}
```

Phase 3 is where built-in trade migration is expected to start consuming these interfaces more directly.

## Legacy Mode Boundary
When connected to a legacy server:

- stock chat/trade should still work
- framework-only message types should not send
- users should receive clear UI messaging that framework capability is unavailable

Submods should assume:

- `CompatibilityMode.Legacy` means custom framework traffic is unavailable
- framework handlers should prefer graceful fallback instead of forcing errors

## Recommended Authoring Rules
- Keep one clear capability ID per extension feature.
- Treat `MessageType` as a negotiated capability unless you have a strong reason not to.
- Keep payloads structured and language-neutral.
- Put user-visible strings in `Languages/...` and render them through `TranslationKey`.
- Use `Handled` when your extension fully owns the result.
- Use `Continue` only when you intentionally want later handlers/renderers to participate.
- Use `LegacyFallback` when stock Phinix behavior is still acceptable.

## Current Sample
The built-in red packet example demonstrates:

- extension discovery
- capability declaration
- outgoing text capture
- server broadcast
- client-side rendering
- keyed translation for final display

Relevant code:

- `Client/Source/Extensions/RedPacketClientExtension.cs`
- `Server/Extensions/RedPacketServerExtension.cs`
- `Common/Utils/Framework/*`
