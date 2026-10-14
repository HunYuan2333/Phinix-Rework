using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private sealed class ProbeImage : IClientPresentationImage
    {
        public int Acquires, Returns;
        public bool Valid = true, ThrowRead, ThrowReturn;
        public ClientPresentationImageSize Size => default(ClientPresentationImageSize);
        public long Revision => 0;
        public IClientPresentationImageLease Acquire() { Acquires++; return new Lease(this); }
        private sealed class Lease : IClientTextureImageLease
        {
            private ProbeImage owner;
            public Lease(ProbeImage owner) { this.owner = owner; }
            public bool IsValid => owner != null && owner.Valid;
            public bool TryGetTexture(out Texture texture)
            {
                texture = new Texture(); // Deliberately hostile: also writes a value on failure.
                if (owner.ThrowRead) throw new InvalidOperationException("read");
                return IsValid;
            }
            public void Dispose() { var old = owner; owner = null; old.Returns++; if (old.ThrowReturn) throw new InvalidOperationException("return"); }
        }
    }
    private static void CheckImageSlots()
    {
        var bounds = new ClientPresentationBounds(0, 0, 600, 500);
        int errors = 0;
        var badgeImage = new ProbeImage();
        var badge = new ClientPresentationBadge("badge", "Badge", badgeImage).WithColor("ui.success");
        Check(ReferenceEquals(badge.Image, badgeImage), "Badge color copies retain the borrowed source");
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => true, _ => errors++).Open(new Module(), "badges"))
        {
            session.Publish(new ClientPresentationPage("badges", "", 1, new[] { new ClientPresentationSection("badges", "", "").WithBadges(new[] { badge }) }), null, () => true);
            Event.current.type = EventType.Layout; session.Draw(bounds);
            Event.current.type = EventType.MouseDown; session.Draw(bounds);
            Check(badgeImage.Acquires == 0, "Badge layout and input do not borrow native images");
            Event.current.type = EventType.Repaint; int fitted = Widgets.FittedIcons; session.Draw(bounds);
            Check(badgeImage.Acquires == 1 && badgeImage.Returns == 1 && Widgets.FittedIcons == fitted + 1, "Badge repaint draws and returns a base Texture lease");
            badgeImage.Valid = false; fitted = Widgets.FittedIcons; session.Draw(bounds);
            Check(Widgets.FittedIcons == fitted && badgeImage.Acquires == badgeImage.Returns, "A false lease result cannot draw its stray out value");
            badgeImage.ThrowRead = true; session.Draw(bounds);
            Check(Widgets.FittedIcons == fitted && session.FailureCode == null && errors == 1, "Read failure clears stray out texture and preserves the badge page");
            int beforeHidden = badgeImage.Acquires;
            session.Draw(new ClientPresentationBounds(0, 0, 600, 0));
            Check(badgeImage.Acquires == beforeHidden, "Badge cells outside a zero-height viewport never acquire");
            badgeImage.ThrowRead = false; badgeImage.Valid = true; badgeImage.ThrowReturn = true; Widgets.ThrowFittedIcon = true;
            session.Draw(bounds); Widgets.ThrowFittedIcon = false;
            Check(badgeImage.Acquires == badgeImage.Returns && errors == 1 && session.FailureCode == null, "Draw and release failures remain isolated and log once");
        }
        var mediaImage = new ProbeImage();
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => true, null).Open(new Module(), "media"))
        {
            session.Publish(new ClientPresentationPage("media", "", 1, new[] { new ClientPresentationSection("feed", new ClientPresentationFeed("feed", new[] {
                new ClientPresentationFeedRow("row", "Heading", "", "Body", media: new[] { new ClientPresentationMedia("media", ClientPresentationMediaState.Ready, "Placeholder", mediaImage) })
            }, "")) }), null, () => true);
            Event.current.type = EventType.Layout; session.Draw(bounds);
            Event.current.type = EventType.MouseDown; session.Draw(bounds);
            Check(mediaImage.Acquires == 0, "Media layout and input never acquire");
            Event.current.type = EventType.Repaint; session.Draw(new ClientPresentationBounds(0, 0, 600, 10));
            Check(mediaImage.Acquires == 0, "Media outside the viewport is not acquired");
            session.Draw(bounds); Check(mediaImage.Acquires == 1 && mediaImage.Returns == 1, "Visible Ready media returns its drawing lease");
            mediaImage.Valid = false; session.Draw(bounds);
            Check(!session.TryDequeueInput(out _) && session.FailureCode == null, "Expired Ready media never creates an automatic load intent");
            Check(Widgets.Labels.Exists(l => l.Text == "Placeholder"), "Expired media renders the owner placeholder");
            mediaImage.ThrowRead = true; session.Draw(bounds);
            Check(!session.TryDequeueInput(out _) && mediaImage.Acquires == mediaImage.Returns && session.FailureCode == null, "Throwing media never refetches or changes business state");
        }
        var composerImage = new ProbeImage(); var command = new Command();
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => true, null).Open(new Module(), "composer"))
        {
            session.Publish(new ClientPresentationPage("composer", "", 1, null, new ClientPresentationComposer(new ClientPresentationTextField("draft", "", "draft"), new ClientPresentationAction("submit", command), composerImage)), new ActionSet { Actions = new[] { command } }, () => true);
            Event.current.type = EventType.Layout; float height = session.MeasureComposerHeight(600); session.Draw(bounds);
            Event.current.type = EventType.MouseDown; session.Draw(bounds);
            Check(composerImage.Acquires == 0, "Composer measurement and input never acquire");
            Event.current.type = EventType.Repaint; int icons = GUI.IconCalls; session.Draw(bounds);
            var button = Widgets.LastInvisibleRect;
            Check(composerImage.Acquires == 1 && composerImage.Returns == 1 && GUI.IconCalls == icons + 1, "Composer borrows base Texture only while drawing");
            composerImage.ThrowRead = true; session.Draw(bounds);
            Check(GUI.IconCalls == icons + 1 && session.FailureCode == null && session.MeasureComposerHeight(600) == height, "Failed composer images retain geometry and do not draw stale textures");
            Event.current.type = EventType.MouseDown; Widgets.InvisibleClicks = 1; session.Draw(bounds);
            var failedButton = Widgets.LastInvisibleRect;
            Check(button.x == failedButton.x && button.y == failedButton.y && button.width == failedButton.width && button.height == failedButton.height, "Image failure preserves the full composer submit hit area");
            Check(session.TryDequeueInput(out var input) && input.Kind == ClientPresentationInputKind.SubmitRequested && input.Value == "draft" && !session.TryDequeueInput(out _), "Failed image still permits one explicit submit intent");
            Check(command.Calls == 0, "Image and submit drawing do not execute business commands");
            composerImage.ThrowRead = false; composerImage.ThrowReturn = true; GUI.ThrowIcon = true;
            Event.current.type = EventType.Repaint; session.Draw(bounds); GUI.ThrowIcon = false;
            Check(session.FailureCode == null && composerImage.Acquires == composerImage.Returns, "Composer GPU and release errors do not invalidate actions");
        }
        Widgets.InvisibleClicks = 0; Event.current.type = EventType.Layout;
    }
}
