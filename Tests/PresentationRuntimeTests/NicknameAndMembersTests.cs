using System;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using Phinix.ChatExtension.Client;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static void CheckNicknameDraft()
    {
        var plain = NicknameDisplayMarkup.Identity("我：", "Alice", new ClientRgba(.5f,.5f,.5f), new ClientRgba(0,1,1));
        Check(plain == "<color=#808080FF>我：</color> <color=#00FFFFFF>Alice</color>", "Identity prefix and plain nickname use independent theme colors");
        string customName = "<color=#FF0000>红</color><color=#00FF00>绿</color>";
        Check(NicknameDisplayMarkup.Name(customName, new ClientRgba(0,0,1)) == "<color=#0000FFFF>" + customName + "</color>", "Explicit per-character markup remains exact inside the display-only fallback");
        Check(NicknameDisplayMarkup.Name("Alice", new ClientRgba(1,0,0,.5f)).StartsWith("<color=#FF000080>"), "Name display retains theme alpha");
        var facts = new NicknameFacts("old", "old", "user-a", true);
        int saves = 0, sends = 0;
        Action<string> persist = name => { saves++; facts = new NicknameFacts(name, facts.Observed, facts.Uuid, facts.Online); };
        Func<string, bool> submit = name => { sends++; return true; };
        var edit = new NicknameEditState(() => facts, persist, submit);
        string tagged = "<color=#FFAA00>星</color><color=#66CCFF>河</color>";
        edit.Change(tagged);
        Check(saves == 0 && sends == 0 && facts.Saved == "old", "Editing or abandoning a draft has no settings/network side effects");
        edit.Save(); edit.Save();
        Check(saves == 1 && sends == 1 && facts.Saved == tagged, "One explicit save preserves the exact tags and cannot be dispatched twice");
        Check(edit.Status(facts) == "submitted", "Dispatch is not an online name acknowledgement");
        facts = new NicknameFacts(tagged, tagged, "user-a", true);
        Check(edit.Status(facts) == "observed" && sends == 1, "Observed directory facts update feedback without resending");

        edit = new NicknameEditState(() => facts, persist, submit); edit.Change("draft");
        facts = new NicknameFacts("external", "external", "user-a", true);
        edit.Save();
        Check(edit.Message == "changed" && saves == 1 && edit.Draft == "draft", "External name changes require explicit review without losing the draft");
        edit.Save();
        Check(saves == 2 && sends == 2, "A second deliberate save can commit the reviewed draft");

        facts = new NicknameFacts("offline", null, "", false);
        edit = new NicknameEditState(() => facts, persist, submit); edit.Change("next login"); edit.Save();
        Check(edit.Message == "savedOffline" && facts.Saved == "next login" && sends == 2, "Offline saves never claim an online submission");
        facts = new NicknameFacts(facts.Saved, "server name", "user-a", true);
        Check(edit.Status(facts) == "savedDifferent", "Login does not keep showing a stale currently-offline message");
        facts = new NicknameFacts(facts.Saved, facts.Saved, "user-a", true);
        Check(edit.Status(facts) == "observed", "An offline saved name can later match actual login facts without invented request correlation");
        facts = new NicknameFacts("old", "old", "user-a", true);
        edit = new NicknameEditState(() => facts, persist, name => { sends++; throw new Exception("unknown transport result"); });
        edit.Change("uncertain"); edit.Save(); edit.Save();
        Check(edit.Message == "sendUnknown" && sends == 3 && edit.Committed, "A throwing transport remains uncertain and blocks automatic duplicate saves");
        facts = new NicknameFacts(facts.Saved, null, "", false);
        Check(edit.Status(facts) != "observed", "Disconnect is not observed success");
        facts = new NicknameFacts("old", "old", "user-a", true);
        edit = new NicknameEditState(() => facts, persist, submit); edit.Change("new");
        facts = new NicknameFacts("old", "old", "user-b", true); edit.Save();
        Check(edit.Message == "changed" && sends == 3, "A changed session identity cannot receive the previous draft without review");
        int failedSaves = 0;
        edit = new NicknameEditState(() => facts, name => { if (++failedSaves == 1) throw new Exception(); persist(name); }, submit);
        edit.Change("retry"); edit.Save();
        Check(edit.Message == "saveFailed" && !edit.Committed && sends == 3, "A failed settings save sends nothing and retains an editable draft");
        edit.Save();
        Check(edit.Committed && sends == 4, "A known local save failure may be explicitly retried");
        foreach (var invalid in new[] { "", "  ", "a\nb", "a\0b", new string('x', 4097) })
        { edit = new NicknameEditState(() => facts, persist, submit); edit.Change(invalid); edit.Save(); Check(!edit.Committed && sends == 4, "Invalid input cannot be saved: " + invalid.Length); }
    }

    private sealed class MemberCommand : IClientContributedAction
    {
        public string OwnerId { get; set; } = "test";
        public string Id { get; set; } = "collapse-blocked";
        public string Label => "Show blocked";
        public string DisabledReason => null;
        public ClientActionExecutionResult TryExecute() => new ClientActionExecutionResult { Completed = true };
    }
    private static void CheckMemberSidebarDensity()
    {
        var toggle = new ClientPresentationAction("toggle", new MemberCommand());
        var rows = Enumerable.Range(0, 119).Select(i => new ClientPresentationRow("user." + i, "member-name." + i)).ToArray();
        var oldPage = new ClientPresentationPage("members", "Users", 1, new[] {
            new ClientPresentationSection("member-controls", "", "Select a member to view available actions.", new[] { toggle },
                new ClientPresentationStatus("119 online"), fields: new[] { new ClientPresentationTextField("member-filter", "Search members", "", "Search members") }),
            new ClientPresentationSection("member-rows", "", "", list: new ClientPresentationList("members-list", rows, ""))
        });
        var compact = MemberSidebarPresentation.Build(2, "test", "119 online", "Search members", "", null, "More actions", new[] { toggle }, rows, "", null);
        Check(compact.Sections.Count == 2 && compact.Title == "" && compact.Sections[1].List.CompactRows && !compact.Sections[0].Plain && !compact.Sections[1].Plain && compact.Sections[1].Title == "119 online", "Themed tools/list cards retain compact rows and attach the count to the list heading");
        var extra = new ClientPresentationAction("extra", new MemberCommand { OwnerId = "third-party", Id = "inspect" });
        var selected = MemberSidebarPresentation.Build(3, "test", "119 online", "Search members", "", "Alice", "More actions", new[] { toggle, extra }, rows, "", "user.1");
        Check(selected.Sections[1].Title == "Alice" && !selected.Sections[1].Plain && selected.Sections[1].Actions.Single().Id == "extra" && selected.Sections[1].ActionMenus.Single().ActionIds.Single() == "extra", "Third-party member actions remain reachable through the ordinary Host menu");
        Event.current.type = EventType.Repaint; Widgets.FieldValue = null;
        Text.MeasureWrapping = true;
        foreach (int width in new[] { 180, 270, 400 })
        {
            var service = new ClientPresentationService(new Lifetime(), new Theme(), () => true, _ => { });
            var session = service.Open(new Module(), "members");
            session.Publish(oldPage, new ActionSet { Actions = new[] { toggle.Action } }, () => true); Widgets.Labels.Clear(); session.Draw(new ClientPresentationBounds(0, 0, width, 500));
            int normal = Widgets.Labels.Count(l => l.Text.StartsWith("member-name."));
            session.Publish(compact, new ActionSet { Actions = new[] { toggle.Action } }, () => true); Widgets.Labels.Clear(); session.Draw(new ClientPresentationBounds(0, 0, width - 12, 436));
            int dense = Widgets.Labels.Count(l => l.Text.StartsWith("member-name."));
            Check(session.FailureCode == null && dense >= normal, "Framed sidebar's 64px reserve does not reduce stub-visible member rows at width " + width + ": " + normal + " -> " + dense);
            Console.WriteLine("Member density (stub, width " + width + ", new viewport reserves 64px for identity/frame and 12px in width): " + normal + " -> " + dense + " rows");
            session.Dispose();
        }
        Text.MeasureWrapping = false;
    }
}
