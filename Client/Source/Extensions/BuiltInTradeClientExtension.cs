using System.Collections.Generic;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using Utils.Framework;

namespace PhinixClient.Extensions
{
    [PhinixExtension(FrameworkTradeProtocol.Capability)]
    public sealed class BuiltInTradeClientExtension : IPhinixExtensionModule, ICapabilityProvider, IClientCommandHandler
    {
        private BuiltInTradeClientHostServices hostServices;

        public string ExtensionId => FrameworkTradeProtocol.Capability;

        public int Priority => 1100;

        public void Register(IExtensionComponentSink sink, ExtensionHostContext hostContext)
        {
            hostServices = hostContext.GetRequiredService<BuiltInTradeClientHostServices>();
            sink.AddCapabilityProvider(this);
            sink.AddClientCommandHandler(this);
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkTradeProtocol.Capability;
            yield return FrameworkTradeProtocol.CreateRequestType;
            yield return FrameworkTradeProtocol.CreateResponseType;
            yield return FrameworkTradeProtocol.SnapshotType;
            yield return FrameworkTradeProtocol.OfferUpdateRequestType;
            yield return FrameworkTradeProtocol.OfferUpdateResponseType;
            yield return FrameworkTradeProtocol.StatusUpdateRequestType;
            yield return FrameworkTradeProtocol.StatusUpdateResponseType;
            yield return FrameworkTradeProtocol.CompletedEventType;
            yield return FrameworkTradeProtocol.CancelledEventType;
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null &&
                   (command.MessageType == FrameworkTradeProtocol.SnapshotType ||
                    command.MessageType == FrameworkTradeProtocol.CreateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.OfferUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.StatusUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.CompletedEventType ||
                    command.MessageType == FrameworkTradeProtocol.CancelledEventType);
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            switch (command.MessageType)
            {
                case FrameworkTradeProtocol.SnapshotType:
                    hostServices.TradeService.HandleSnapshot(command);
                    break;
                case FrameworkTradeProtocol.CreateResponseType:
                    hostServices.TradeService.HandleCreateResponse(command);
                    break;
                case FrameworkTradeProtocol.OfferUpdateResponseType:
                    hostServices.TradeService.HandleOfferUpdateResponse(command);
                    break;
                case FrameworkTradeProtocol.StatusUpdateResponseType:
                    hostServices.TradeService.HandleStatusUpdateResponse(command);
                    break;
                case FrameworkTradeProtocol.CompletedEventType:
                    hostServices.TradeService.HandleCompletedEvent(command);
                    break;
                case FrameworkTradeProtocol.CancelledEventType:
                    hostServices.TradeService.HandleCancelledEvent(command);
                    break;
            }

            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }
    }
}
