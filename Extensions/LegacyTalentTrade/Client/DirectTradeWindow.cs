using System;
using PhinixClient;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Trade negotiation window: split view with my offer vs their offer.
    /// </summary>
    public class DirectTradeWindow : Window
    {
        private const float SPACING = 6f;
        private const float ROW_HEIGHT = 28f;
        private const float PAWN_ROW_HEIGHT = 50f;
        private const float BUTTON_HEIGHT = 30f;

        private readonly TalentTabs offerTabs = new TalentTabs("Phinix_legacyTalentTrade_tradeMyOffer", "Phinix_legacyTalentTrade_tradeTheirOffer");
        private readonly TalentToolbar toolbar;
        private Vector2 myScroll;
        private Vector2 theirScroll;
        private readonly Dictionary<PawnSummary, string> pawnLabels = new Dictionary<PawnSummary, string>();
        private object pawnLanguage;
        private int pawnStateVersion = -1;
        private DirectTrade trade;
        private string silverBuffer = "0";
        private int silverValue;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safe = UiScreenSafeArea.Current;
                return new Vector2(Mathf.Min(800f, safe.width), Mathf.Min(550f, safe.height));
            }
        }

        public DirectTradeWindow(DirectTrade trade)
        {
            this.trade = trade;
            toolbar = new TalentToolbar(ActivateBottomAction, "Phinix_legacyTalentTrade_tradeConfirmSend",
                "Phinix_legacyTalentTrade_tradeLock", "Phinix_legacyTalentTrade_cancel");
            resizeable = true;
            this.doCloseButton = false;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.forcePause = false;
            this.draggable = true;

            string localUuid = TalentTradeManager.GetLocalUuid();
            bool isInitiator = trade.InitiatorUuid == localUuid;
            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;
            if (myOffer != null)
            {
                silverBuffer = myOffer.SilverAmount.ToString();
                silverValue = myOffer.SilverAmount;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DirectTrade current = TalentTradeManager.GetTrade(trade.Id);
            if (current == null || current.State == DirectTradeState.Completed || current.State == DirectTradeState.Cancelled)
            {
                Close();
                return;
            }
            trade = current;
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            bool isInitiator = trade.InitiatorUuid == TalentTradeManager.GetLocalUuid();
            string other = isInitiator ? trade.TargetName : trade.InitiatorName;
            if (string.IsNullOrEmpty(other)) other = isInitiator ? trade.TargetUuid : trade.InitiatorUuid;
            TalentTradeUi.Label(new Rect(inRect.x, inRect.y, inRect.width, Mathf.Min(30f, inRect.height)),
                "Phinix_legacyTalentTrade_tradeWith".Translate() + " " + other);
            float footerHeight = Mathf.Min(62f, Mathf.Max(0f, inRect.height - 36f));
            Rect footer = new Rect(inRect.x, inRect.yMax - footerHeight, inRect.width, footerHeight);
            Rect content = new Rect(inRect.x, inRect.y + Mathf.Min(36f, inRect.height), inRect.width,
                Mathf.Max(0f, inRect.height - 36f - footerHeight - SPACING));
            if (content.height > 0f)
            {
                var split = ResponsiveSplitLayout.Calculate(content, new Vector2(300f, content.height),
                    new Vector2(300f, content.height), new Vector2(content.width / 2f, content.height), SPACING, offerTabs.Selected == 0);
                if (split.Mode == ResponsiveSplitMode.Horizontal)
                {
                    DrawMyOffer(split.FirstRect, isInitiator);
                    DrawTheirOffer(split.SecondRect, isInitiator);
                }
                else
                {
                    content = offerTabs.Draw(content);
                    if (offerTabs.Selected == 0) DrawMyOffer(content, isInitiator);
                    else DrawTheirOffer(content, isInitiator);
                }
            }
            DrawBottomButtons(footer, isInitiator);
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampToScreen();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampToScreen();
        }

        private void ClampToScreen()
        {
            windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(360f, 300f));
        }

        private void DrawMyOffer(Rect rect, bool isInitiator)
        {
            DrawOffer(rect, isInitiator ? trade.InitiatorOffer : trade.TargetOffer, true, isInitiator);
        }

        private void DrawTheirOffer(Rect rect, bool isInitiator)
        {
            DrawOffer(rect, isInitiator ? trade.TargetOffer : trade.InitiatorOffer, false, isInitiator);
        }

        private void DrawOffer(Rect rect, TradeOffer offer, bool mine, bool isInitiator)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Widgets.DrawMenuSection(rect);
            Rect inner = TalentTradeUi.Inset(rect, 6f);
            Vector2 scroll = mine ? myScroll : theirScroll;
            int count = offer == null ? 0 : offer.Pawns.Count;
            var layout = TalentOfferLayout.Calculate(inner, count, mine);
            TalentTradeUi.Label(layout.Header,
                (mine ? "Phinix_legacyTalentTrade_tradeMyOffer" : "Phinix_legacyTalentTrade_tradeTheirOffer").Translate());
            scroll.y = Mathf.Clamp(scroll.y, 0f, Mathf.Max(0f, layout.ScrollContent.height - layout.Viewport.height));
            Widgets.BeginScrollView(layout.Viewport, ref scroll, layout.ScrollContent);
            try
            {
                var range = VirtualListLayout.GetFixedRange(count, PAWN_ROW_HEIGHT + SPACING, scroll.y, layout.Viewport.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    if (offer == null || i >= offer.Pawns.Count) break;
                    DrawPawnSummaryRow(new Rect(0f, i * (PAWN_ROW_HEIGHT + SPACING), layout.ScrollContent.width, PAWN_ROW_HEIGHT),
                        offer.Pawns[i], mine, i);
                }
                if (layout.ControlsScroll) DrawOfferControls(layout.Controls, offer, mine, isInitiator);
            }
            finally { Widgets.EndScrollView(); }
            if (!layout.ControlsScroll) DrawOfferControls(layout.Controls, offer, mine, isInitiator);
            if (mine) myScroll = scroll; else theirScroll = scroll;
        }

        private void DrawOfferControls(Rect rect, TradeOffer offer, bool mine, bool isInitiator)
        {
            if (mine)
            {
                if (TalentTradeUi.Button(new Rect(rect.x, rect.y, rect.width, 28f), "Phinix_legacyTalentTrade_tradeAddUnit".Translate()))
                    ShowPawnPicker(isInitiator);
                TalentTradeUi.Label(new Rect(rect.x, rect.y + 30f, rect.width, 20f),
                    "Phinix_legacyTalentTrade_tradeSilverAmount".Translate() + " (" + "Phinix_legacyTalentTrade_silver".Translate() + ")");
                silverBuffer = Widgets.TextField(new Rect(rect.x, rect.y + 50f, rect.width, 28f), silverBuffer);
                int.TryParse(silverBuffer, out silverValue);
                silverValue = Mathf.Max(0, silverValue);
            }
            else
            {
                TalentTradeUi.Label(rect, "Phinix_legacyTalentTrade_tradeSilverAmount".Translate() + ": " +
                    (offer == null ? 0 : offer.SilverAmount) + " " + "Phinix_legacyTalentTrade_silver".Translate());
            }
        }

        private void DrawPawnSummaryRow(Rect rect, PawnSummary summary, bool canRemove, int index)
        {
            if (summary == null) return;
            if (pawnStateVersion != TalentTradeManager.StateVersion || !ReferenceEquals(pawnLanguage, LanguageDatabase.activeLanguage))
            {
                pawnStateVersion = TalentTradeManager.StateVersion;
                pawnLanguage = LanguageDatabase.activeLanguage;
                pawnLabels.Clear();
            }
            string label;
            if (!pawnLabels.TryGetValue(summary, out label))
            {
                label = summary.GetDisplayLabel();
                pawnLabels.Add(summary, label);
            }
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            float buttonWidth = canRemove ? Mathf.Min(60f, rect.width) : 0f;
            float textWidth = Mathf.Max(0f, rect.width - buttonWidth - (canRemove ? SPACING : 0f));
            TalentTradeUi.Label(new Rect(rect.x, rect.y, textWidth, 24f), label);
            TalentTradeUi.Label(new Rect(rect.x, rect.y + 24f, textWidth, 22f), summary.SkillsSummary);
            if (canRemove && TalentTradeUi.Button(new Rect(rect.xMax - buttonWidth, rect.y + 10f, buttonWidth, 28f),
                "Phinix_legacyTalentTrade_tradeRemovePawn".Translate())) RemovePawnFromOffer(index);
            if (Mouse.IsOver(rect)) TooltipHandler.TipRegion(rect, label + "\n" + TalentCard.Details(summary));
        }

        private void DrawBottomButtons(Rect rect, bool isInitiator)
        {
            bool mine = isInitiator ? trade.InitiatorConfirmed : trade.TargetConfirmed;
            toolbar.Draw(rect, 3, disabledIndex: mine ? 1 : -1);
            bool theirs = isInitiator ? trade.TargetConfirmed : trade.InitiatorConfirmed;
            if (mine)
                TalentTradeUi.Label(TalentTradeUi.Below(rect, 32f),
                    (theirs ? "Phinix_legacyTalentTrade_tradeBothLocked" : "Phinix_legacyTalentTrade_tradeWaitingLock").Translate());
        }

        private void ActivateBottomAction(int action)
        {
            bool isInitiator = trade.InitiatorUuid == TalentTradeManager.GetLocalUuid();
            switch (action)
            {
                case 0: SendCurrentOffer(isInitiator); break;
                case 1:
                    if (isInitiator ? trade.InitiatorConfirmed : trade.TargetConfirmed) return;
                    SendCurrentOffer(isInitiator);
                    TradeOffer theirOffer = isInitiator ? trade.TargetOffer : trade.InitiatorOffer;
                    if (theirOffer == null || theirOffer.PawnData == null || theirOffer.PawnData.Count == 0)
                        TalentTradeManager.LockTrade(trade.Id);
                    else
                        TransferCompatibilityUi.ConfirmMany(
                            "Phinix_legacyTalentTrade_tradeLockConfirm".Translate(),
                            "Phinix_legacyTalentTrade_tradeTheirOffer".Translate(),
                            theirOffer.PawnManifestData,
                            delegate { TalentTradeManager.LockTrade(trade.Id); });
                    break;
                case 2: TalentTradeManager.CancelTrade(trade.Id); Close(); break;
            }
        }

        private void ShowPawnPicker(bool isInitiator)
        {
            List<Pawn> tradeablePawns = TradeablePawnUtility.GetTradeablePawns(Find.CurrentMap);

            if (tradeablePawns.Count == 0)
            {
                Messages.Message("Phinix_legacyTalentTrade_noTradeablePawnsAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn p in tradeablePawns)
            {
                Pawn captured = p;
                options.Add(new FloatMenuOption(TradeablePawnUtility.GetLabel(captured), delegate
                {
                    TryAddPawnToOffer(captured, isInitiator);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void TryAddPawnToOffer(Pawn pawn, bool isInitiator)
        {
            if (pawn == null) return;

            string targetUuid = isInitiator ? trade.TargetUuid : trade.InitiatorUuid;
            string pawnLabel = TradeablePawnUtility.GetLabel(pawn);
            string raceDefName = pawn.def != null ? pawn.def.defName : "Unknown";

            TalentTradeManager.RequestRaceCheck(pawn, targetUuid, delegate(TransferReport report)
            {
                if (report != null && report.HasMissing)
                {
                    string message = "Phinix_legacyTalentTrade_raceIncompatible".Translate(pawnLabel, raceDefName);
                    if (!string.IsNullOrEmpty(report.ToSummary()))
                    {
                        message += "\n\n" + report.ToSummary();
                    }
                    Find.WindowStack.Add(new Dialog_MessageBox(message));
                    return;
                }

                AddPawnToOffer(pawn, isInitiator);
            });
        }

        private void AddPawnToOffer(Pawn pawn, bool isInitiator)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;

            PawnSummary summary = PawnSummary.FromPawn(pawn);
            string b64 = PawnSerializer.Serialize(pawn);
            if (string.IsNullOrEmpty(b64))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】Failed to serialize pawn for trade offer.");
                return;
            }

            PawnSerializer.DespawnAndHold(pawn);

            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;
            if (myOffer == null)
            {
                myOffer = new TradeOffer();
                if (isInitiator) trade.InitiatorOffer = myOffer;
                else trade.TargetOffer = myOffer;
            }

            myOffer.Pawns.Add(summary);
            myOffer.PawnData.Add(b64);
            myOffer.PawnManifestData.Add(DefManifestHelper.SerializeCompressed(pawn));
            trade.HeldPawns.Add(pawn);
        }

        private void RemovePawnFromOffer(int index)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            bool isInitiator = trade.InitiatorUuid == localUuid;
            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;
            if (myOffer == null || index < 0 || index >= myOffer.Pawns.Count) return;

            // Restore pawn
            if (index < myOffer.PawnData.Count)
            {
                string b64 = myOffer.PawnData[index];
                if (!string.IsNullOrEmpty(b64))
                {
                    TalentTradeManager.EnqueueMainThread(() =>
                    {
                        Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64);
                        if (pawn != null)
                        {
                            Messages.Message("Phinix_legacyTalentTrade_pawnRestored".Translate(pawn.LabelShortCap),
                                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                        }
                    });
                }
                if (index < myOffer.PawnManifestData.Count)
                    myOffer.PawnManifestData.RemoveAt(index);
                myOffer.PawnData.RemoveAt(index);
            }

            myOffer.Pawns.RemoveAt(index);
        }

        private void SendCurrentOffer(bool isInitiator)
        {
            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;
            if (myOffer == null)
            {
                myOffer = new TradeOffer();
                if (isInitiator) trade.InitiatorOffer = myOffer;
                else trade.TargetOffer = myOffer;
            }

            myOffer.SilverAmount = silverValue;
            TalentTradeManager.SendTradeOffer(trade.Id, myOffer);
        }
    }
}
