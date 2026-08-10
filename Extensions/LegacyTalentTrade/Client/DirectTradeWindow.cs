using System;
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

        private DirectTrade trade;
        private string silverBuffer = "0";
        private int silverValue;

        public override Vector2 InitialSize
        {
            get { return new Vector2(800f, 550f); }
        }

        public DirectTradeWindow(DirectTrade trade)
        {
            this.trade = trade;
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
            // Refresh trade state
            DirectTrade current = TalentTradeManager.GetTrade(trade.Id);
            if (current == null)
            {
                Close();
                return;
            }
            trade = current;

            if (trade.State == DirectTradeState.Completed || trade.State == DirectTradeState.Cancelled)
            {
                Close();
                return;
            }

            string localUuid = TalentTradeManager.GetLocalUuid();
            bool isInitiator = trade.InitiatorUuid == localUuid;

            // Title
            Text.Font = GameFont.Medium;
            string otherName = isInitiator ? trade.TargetName : trade.InitiatorName;
            if (string.IsNullOrEmpty(otherName)) otherName = isInitiator ? trade.TargetUuid : trade.InitiatorUuid;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "Phinix_legacyTalentTrade_tradeWith".Translate() + " " + otherName);
            Text.Font = GameFont.Small;

            float topY = inRect.y + 36f;
            float contentHeight = inRect.height - 36f - BUTTON_HEIGHT - SPACING * 2;
            float halfWidth = (inRect.width - SPACING * 2) / 2f;

            // Left: my offer
            Rect leftRect = new Rect(inRect.x, topY, halfWidth, contentHeight);
            DrawMyOffer(leftRect, isInitiator);

            // Divider
            Widgets.DrawLineVertical(inRect.x + halfWidth + SPACING / 2f, topY, contentHeight);

            // Right: their offer
            Rect rightRect = new Rect(inRect.x + halfWidth + SPACING * 2, topY, halfWidth, contentHeight);
            DrawTheirOffer(rightRect, isInitiator);

            // Bottom buttons
            float btnY = topY + contentHeight + SPACING;
            DrawBottomButtons(new Rect(inRect.x, btnY, inRect.width, BUTTON_HEIGHT), isInitiator);
        }

        private void DrawMyOffer(Rect rect, bool isInitiator)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Phinix_legacyTalentTrade_tradeMyOffer".Translate());
            y += 26f;

            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;
            if (myOffer == null) myOffer = new TradeOffer();

            // Pawns list
            for (int i = 0; i < myOffer.Pawns.Count; i++)
            {
                Rect pawnRow = new Rect(inner.x, y, inner.width, PAWN_ROW_HEIGHT);
                DrawPawnSummaryRow(pawnRow, myOffer.Pawns[i], true, i);
                y += PAWN_ROW_HEIGHT + SPACING;
            }

            // Add pawn button
            Rect addBtn = new Rect(inner.x, y, 160f, ROW_HEIGHT);
            if (Widgets.ButtonText(addBtn, "Phinix_legacyTalentTrade_tradeAddUnit".Translate()))
            {
                ShowPawnPicker(isInitiator);
            }
            y += ROW_HEIGHT + SPACING;

            // Silver
            Widgets.Label(new Rect(inner.x, y, 100f, ROW_HEIGHT), "Phinix_legacyTalentTrade_tradeSilverAmount".Translate());
            Rect silverField = new Rect(inner.x + 110f, y, 100f, ROW_HEIGHT);
            silverBuffer = Widgets.TextField(silverField, silverBuffer);
            int.TryParse(silverBuffer, out silverValue);
            if (silverValue < 0) silverValue = 0;
            Widgets.Label(new Rect(inner.x + 220f, y, 40f, ROW_HEIGHT), "Phinix_legacyTalentTrade_silver".Translate());
        }

        private void DrawTheirOffer(Rect rect, bool isInitiator)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Phinix_legacyTalentTrade_tradeTheirOffer".Translate());
            y += 26f;

            TradeOffer theirOffer = isInitiator ? trade.TargetOffer : trade.InitiatorOffer;
            if (theirOffer == null) return;

            // Pawns
            for (int i = 0; i < theirOffer.Pawns.Count; i++)
            {
                Rect pawnRow = new Rect(inner.x, y, inner.width, PAWN_ROW_HEIGHT);
                DrawPawnSummaryRow(pawnRow, theirOffer.Pawns[i], false, i);
                y += PAWN_ROW_HEIGHT + SPACING;
            }

            // Silver
            if (theirOffer.SilverAmount > 0)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, ROW_HEIGHT),
                    "Phinix_legacyTalentTrade_tradeSilverAmount".Translate() + ": " + theirOffer.SilverAmount + " " + "Phinix_legacyTalentTrade_silver".Translate());
            }
        }

        private void DrawPawnSummaryRow(Rect rect, PawnSummary summary, bool canRemove, int index)
        {
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 60f, 22f), summary.GetDisplayLabel());

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.y + 22f, rect.width - 60f, 18f), summary.SkillsSummary);

            Text.Font = GameFont.Small;

            if (canRemove)
            {
                Rect removeBtn = new Rect(rect.xMax - 56f, rect.y + 10f, 50f, 24f);
                if (Widgets.ButtonText(removeBtn, "Phinix_legacyTalentTrade_tradeRemovePawn".Translate()))
                {
                    RemovePawnFromOffer(index);
                }
            }

            // Tooltip
            if (Mouse.IsOver(rect))
            {
                string tip = summary.SkillsSummary;
                if (!string.IsNullOrEmpty(summary.TraitsSummary))
                    tip += "\n" + summary.TraitsSummary;
                if (!string.IsNullOrEmpty(summary.HealthSummary))
                    tip += "\n" + summary.HealthSummary;
                TooltipHandler.TipRegion(rect, tip);
            }
        }

        private void DrawBottomButtons(Rect rect, bool isInitiator)
        {
            float x = rect.x;

            bool myConfirmed = isInitiator ? trade.InitiatorConfirmed : trade.TargetConfirmed;
            bool theirConfirmed = isInitiator ? trade.TargetConfirmed : trade.InitiatorConfirmed;

            // Send offer update
            Rect sendBtn = new Rect(x, rect.y, 120f, rect.height);
            if (Widgets.ButtonText(sendBtn, "Phinix_legacyTalentTrade_tradeConfirmSend".Translate()))
            {
                SendCurrentOffer(isInitiator);
            }
            x += 120f + SPACING;

            // Lock / Unlock
            if (!myConfirmed)
            {
                Rect lockBtn = new Rect(x, rect.y, 120f, rect.height);
                if (Widgets.ButtonText(lockBtn, "Phinix_legacyTalentTrade_tradeLock".Translate()))
                {
                    SendCurrentOffer(isInitiator);
                    TradeOffer theirOffer = isInitiator ? trade.TargetOffer : trade.InitiatorOffer;
                    if (theirOffer == null || theirOffer.PawnData == null || theirOffer.PawnData.Count == 0)
                    {
                        TalentTradeManager.LockTrade(trade.Id);
                    }
                    else
                    {
                        TransferCompatibilityUi.ConfirmMany(
                            "Phinix_legacyTalentTrade_tradeLockConfirm".Translate(),
                            "Phinix_legacyTalentTrade_tradeTheirOffer".Translate(),
                            theirOffer.PawnManifestData,
                            delegate { TalentTradeManager.LockTrade(trade.Id); });
                    }
                }
            }
            else
            {
                Rect waitLabel = new Rect(x, rect.y, 200f, rect.height);
                if (theirConfirmed)
                {
                    Widgets.Label(waitLabel, "Phinix_legacyTalentTrade_tradeBothLocked".Translate());
                }
                else
                {
                    Widgets.Label(waitLabel, "Phinix_legacyTalentTrade_tradeWaitingLock".Translate());
                }
            }

            // Cancel (right side)
            Rect cancelBtn = new Rect(rect.xMax - 100f, rect.y, 100f, rect.height);
            if (Widgets.ButtonText(cancelBtn, "Phinix_legacyTalentTrade_cancel".Translate()))
            {
                TalentTradeManager.CancelTrade(trade.Id);
                Close();
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
