using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private sealed class MenuCommand : IClientContributedAction
    {
        internal int Calls; internal string Disabled;
        public string Label => "Menu action";
        public string DisabledReason => Disabled;
        public ClientActionExecutionResult TryExecute() { Calls++; return new ClientActionExecutionResult { Completed = true }; }
    }
    private static void CheckMenuSnapshots()
    {
        bool main = true;
        var command = new MenuCommand();
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => main, null).Open(new Module(), "page"))
        {
            Check(session.CreateActionMenuItems().Count == 0, "Unpublished views return an empty neutral menu");
            session.Publish(Page(1, new ClientPresentationAction("menu", command)), new ActionSet { Actions = new[] { command } }, () => true);
            var old = session.CreateActionMenuItems();
            Check(old.Count == 1 && old[0].Label == command.Label && old[0].Request != null, "Neutral menu preserves the published binding label and request");
            Reject(() => ((IList<ClientPresentationMenuItem>)old).Clear(), "Menu snapshots cannot be changed through collection casts");
            var game = ClientPresentationMenus.ToGameOptions(old);
            Check(game.Count == 1 && game[0].Label == old[0].Label && ReferenceEquals(game[0].Action, old[0].Request), "Game bridge copies appearance while retaining the exact guarded request");
            game.Clear(); Check(old.Count == 1, "Changing a native menu list cannot mutate its neutral snapshot");
            Reject(() => ClientPresentationMenus.ToGameOptions(null), "Game bridge rejects missing snapshots");
            Event.current.type = EventType.Layout; old[0].Request();
            Event.current.type = EventType.Repaint; old[0].Request();
            Check(command.Calls == 0, "Menu callbacks do not execute on layout or repaint");
            main = false; Reject(() => old[0].Request(), "Menu requests reject off-thread invocation before reading GUI state"); main = true;
            command.Disabled = "disabled";
            Check(session.CreateActionMenuItems()[0].Request == null, "Disabled bindings create disabled neutral entries");
            Check(ClientPresentationMenus.ToGameOptions(session.CreateActionMenuItems())[0].Action == null, "Disabled entries remain disabled in the game bridge");
            Event.current.type = EventType.MouseDown; old[0].Request();
            Check(command.Calls == 0, "A captured enabled menu rechecks current disabled state");
            command.Disabled = null;
            var replacement = new MenuCommand();
            session.Publish(Page(2, new ClientPresentationAction("menu", replacement)), new ActionSet { Actions = new[] { replacement } }, () => true);
            old[0].Request();
            Check(command.Calls == 0 && replacement.Calls == 0, "An old menu cannot resolve the same ID against a replacement page");
            var fresh = session.CreateActionMenuItems(); fresh[0].Request(); fresh[0].Request();
            Check(replacement.Calls == 1, "Fresh requests use the original one-attempt action guard");
            session.Dispose(); fresh[0].Request();
            Check(replacement.Calls == 1, "Closing a view revokes outstanding menu requests");
            Reject(() => session.CreateActionMenuItems(), "Closed views cannot capture another menu");
        }
        var confirmed = new MenuCommand();
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => true, null).Open(new Module(), "page"))
        {
            var binding = new ClientPresentationAction("confirmed", confirmed, confirmation: new ClientPresentationConfirmation("Confirm", "Body", "Accept", "Cancel"));
            session.Publish(Page(1, binding), new ActionSet { Actions = new[] { confirmed } }, () => true);
            Event.current.type = EventType.MouseDown; session.CreateActionMenuItems()[0].Request();
            var window = Find.WindowStack.Last;
            Check(window != null && confirmed.Calls == 0, "Menu requests open the same confirmation before business execution");
            Event.current.type = EventType.Repaint; window.DoWindowContents(new Rect(0, 0, 600, 300));
            Check(confirmed.Calls == 0, "Confirmation drawing does not acknowledge or execute the business action");
            confirmed.Disabled = "disabled";
            Widgets.ClickLabel = "Accept"; Event.current.type = EventType.MouseDown; window.DoWindowContents(new Rect(0, 0, 600, 300));
            Check(confirmed.Calls == 0 && session.LastActionResult == null, "A disabled action is rechecked after opening confirmation, without inventing a result");
            confirmed.Disabled = null; session.CreateActionMenuItems()[0].Request(); window = Find.WindowStack.Last;
            Check(confirmed.Calls == 0, "A disabled request does not consume the later legitimate action attempt");
            Widgets.ClickLabel = "Accept"; window.DoWindowContents(new Rect(0, 0, 600, 300));
            Check(confirmed.Calls == 1, "Explicit confirmation executes through the same public action path");
        }
        Widgets.ClickLabel = null; Event.current.type = EventType.Layout;
    }
}
