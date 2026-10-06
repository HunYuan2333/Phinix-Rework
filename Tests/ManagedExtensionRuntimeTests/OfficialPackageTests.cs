using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Phinix.LegacyTalentTradeExtension.Client;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void OfficialPackageRegression()
    {
        foreach (var package in new[] { "LegacyRedPacket", "LegacyTalentTrade" })
        {
            string prefix = package == "LegacyRedPacket" ? "Phinix_legacyRedpacket_" : "Phinix_legacyTalentTrade_";
            string order = package == "LegacyRedPacket" ? "14" : "16";
            string assembly = package + "Extension.Client";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OfficialPackages", package,
                order + "-" + assembly + ".dll");
            var catalog = ExtensionLocalizationCatalog.LoadCompanion(path, assembly, CancellationToken.None);
            Assert(catalog.Languages.Count() == 2 && catalog.DefaultLocale == "en-US", package + " has verified EN/ZH resources");
            var en = catalog.Languages.Single(l => l.Locale == "en-US");
            var zh = catalog.Languages.Single(l => l.Locale == "zh-CN");
            var originalKeys=File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"OfficialResourceKeys",package+".txt"));
            Assert(originalKeys.Length>0 && originalKeys.All(en.Strings.ContainsKey), package + " migration retains every fixed source key while allowing additions");
            Assert(en.Strings.Keys.OrderBy(k => k).SequenceEqual(zh.Strings.Keys.OrderBy(k => k)), package + " complete bilingual key sets");
            Assert(en.Strings.Values.All(v => !v.Contains("\\n")) && zh.Strings.Values.All(v => !v.Contains("\\n")), "XML newline escapes become actual JSON text newlines");
            foreach (string key in en.Strings.Keys)
            {
                Assert(key.StartsWith(prefix, StringComparison.Ordinal), "Only owner keys enter scoped resources");
                Assert(catalog.Resolve(key, "en-US") == en.Strings[key], "English owner text");
                Assert(catalog.Resolve(key, "zh-CN") == zh.Strings[key], "Chinese owner text");
                Assert(catalog.Resolve(key, "ja-JP") == en.Strings[key], "Unsupported language falls back to English");
            }
            Assert(catalog.Resolve("Phinix_inventory_rewardStored", "en-US") == null, "Inventory texts stay with their owner");
            var module = new LocalizationModule(true);
            using (var service = new ClientLocalizationService(new[] { typeof(LocalizationModule) }, t => catalog, () => true, null))
            {
                service.UpdateLanguage("en-US"); service.OnActivating(module);
                var localizer = service.ForModule(module);
                Assert(localizer.Text(prefix + "tab") == en.Strings[prefix + "tab"], "Bundled resources use ordinary host localization");
                service.UpdateLanguage("zh-CN");
                Assert(localizer.Text(prefix + "tab") == zh.Strings[prefix + "tab"], "Existing plugin handle changes language");
                string formatKey = prefix + (package == "LegacyRedPacket" ? "rewardMessage" : "pawnRestored");
                string text = localizer.Format(formatKey, "Pawn", 7, "Sender");
                Assert(text.Contains("Pawn") && !text.Contains("{0}"), "Owner numeric parameters are formatted");
                if (package == "LegacyTalentTrade")
                {
                    Assert(localizer.Format(prefix + "age", 25) == "25岁", "Chinese age format retains its spacing semantics");
                    service.UpdateLanguage("en-US");
                    Assert(localizer.Format(prefix + "age", 25) == "25 yrs", "English age format retains its spacing semantics");
                }
                service.OnStopped(module);
                Assert(localizer.Text(prefix + "tab") == prefix + "tab", "Stopped plugin releases its localizer");
            }
        }
        PendingPawnReturnRegression();
    }

    private static PendingPawnReturnLedger ReturnLedger(string[] ids, string[] data, string[] uncertain = null)
    { return new PendingPawnReturnLedger(ids, data, uncertain); }

    private static void PendingPawnReturnRegression()
    {
        List<string> ids, data, uncertain;
        var none = new KeyValuePair<string, string>[0];
        var ledger = ReturnLedger(new[] { "a", "b" }, new[] { "raw-a", "raw-b" });
        var a = ledger.Records[0]; var b = ledger.Records[1];
        Assert(ledger.TryQueue(a) && !ledger.TryQueue(a), "A recovery record has only one queue ticket");
        ledger.Snapshot(none, out ids, out data, out uncertain);
        Assert(data.SequenceEqual(new[] { "raw-a", "raw-b" }), "Saving before callbacks retains all payloads");
        ledger.Finish(a, PawnReturnOutcome.Deferred);
        Assert(ledger.TryQueue(a), "Definitely undelivered records remain eligible after queue rejection/failure");
        ledger.BeginHandoff(a);
        ledger.Snapshot(none, out ids, out data, out uncertain);
        Assert(uncertain.SequenceEqual(new[] { "a" }) && data[0] == "raw-a", "Handoff checkpoint retains raw data and uncertainty");
        var interrupted = ReturnLedger(ids.ToArray(), data.ToArray(), uncertain.ToArray());
        Assert(!interrupted.TryQueue(interrupted.Records[0]), "A persisted ambiguous handoff does not automatically replay");
        ledger.Finish(a, PawnReturnOutcome.Uncertain);
        Assert(!ledger.TryQueue(a), "Uncertain delivery remains blocked in the current session");
        Assert(ledger.TryQueue(b), "Independent recovery remains eligible");
        ledger.BeginHandoff(b); ledger.Finish(b, PawnReturnOutcome.Returned);
        Assert(!ledger.TryQueue(b), "Late duplicate tickets cannot deliver a completed record twice");
        ledger.Snapshot(new[] { new KeyValuePair<string, string>("c", "live-c"), new KeyValuePair<string, string>("a", "conflict") }, out ids, out data, out uncertain);
        Assert(ids.SequenceEqual(new[] { "a", "c" }) && data.SequenceEqual(new[] { "raw-a", "live-c" }), "Save merges live backups without erasing unresolved returns");
        ledger.Snapshot(none, out ids, out data, out uncertain);
        Assert(ids.SequenceEqual(new[] { "a" }), "A removed live listing is not kept as a stale backup");

        var corrupt = ReturnLedger(new[] { "dup", "dup", "missing" }, new[] { "first", "second" });
        Assert(corrupt.Records.All(r => !corrupt.TryQueue(r)), "Duplicate IDs and mismatched lists are never materialized");
        corrupt.Snapshot(none, out ids, out data, out uncertain);
        Assert(ids.Count == 3 && data.Count == 3 && data[0] == "first" && data[1] == "second" && data[2] == null,
            "Malformed records retain their IDs and original payloads for diagnosis");
        corrupt = ReturnLedger(new string[0], new[] { "orphan-payload" });
        corrupt.Snapshot(none, out ids, out data, out uncertain);
        Assert(ids.Count == 1 && ids[0] == null && data[0] == "orphan-payload", "An unpaired payload is preserved");

        int handoffs = 0, marks = 0; string stage = null;
        Action fail = () => { throw new InvalidOperationException("Injected failure"); };
        Action ok = () => { };
        Action<string, Exception> log = (s, e) => stage = s;
        Assert(PawnReturnDelivery.Run(fail, () => marks++, () => handoffs++, ok, log) == PawnReturnOutcome.Deferred && marks == 0 && handoffs == 0,
            "Preflight failures never cross the ownership boundary");
        Assert(PawnReturnDelivery.Run(ok, fail, () => handoffs++, ok, log) == PawnReturnOutcome.Deferred && handoffs == 0,
            "Checkpoint failure prevents world delivery");
        Assert(PawnReturnDelivery.Run(ok, () => marks++, fail, ok, log) == PawnReturnOutcome.Uncertain && marks == 1 && stage == "handoff",
            "Exceptions at the ownership boundary are uncertain");
        Assert(PawnReturnDelivery.Run(ok, () => marks++, () => handoffs++, fail, log) == PawnReturnOutcome.Returned && handoffs == 1 && stage == "after-delivery",
            "Graphics failures after delivery cannot create a retry");
        Assert(PawnReturnDelivery.Run(ok, () => marks++, () => handoffs++, ok, log) == PawnReturnOutcome.Returned && handoffs == 2,
            "Successful ownership transfer produces a local delivery receipt");
    }
}
