using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    public sealed class RedPacketDetailWindow : Window
    {
        private const float TITLE_HEIGHT = 38f;
        private const float ICON_SIZE = 84f;
        private const float SMALL_LINE_HEIGHT = 20f;
        private const float CLAIM_LINE_HEIGHT = 30f;
        private const float SECTION_SPACING = 8f;

        private readonly RedPacketDetailSnapshot snapshot;
        private Vector2 claimsScroll = Vector2.zero;
        private readonly ThingDef itemDef;
        private readonly ThingDef stuffDef;

        public override Vector2 InitialSize => new Vector2(640f, 620f);

        public RedPacketDetailWindow(RedPacketDetailSnapshot snapshot)
        {
            this.snapshot = snapshot;
            if (snapshot != null)
            {
                itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.ItemDefName);
                if (!string.IsNullOrEmpty(snapshot.StuffDefName))
                {
                    stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.StuffDefName);
                }
            }
            draggable = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (snapshot == null) return;

            float y = inRect.yMin - 2f;

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, TITLE_HEIGHT),
                "Phinix_legacyRedpacket_detailSenderTitle".Translate(snapshot.SenderName)
            );
            y += TITLE_HEIGHT + SECTION_SPACING;

            Rect iconRect = new Rect(inRect.xMin + (inRect.width - ICON_SIZE) / 2f, y, ICON_SIZE, ICON_SIZE);
            DrawItemIcon(iconRect);
            y = iconRect.yMax + SECTION_SPACING;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, SMALL_LINE_HEIGHT),
                "Phinix_legacyRedpacket_detailPacketAmountLine".Translate(snapshot.RemainingPackets, snapshot.TotalPackets)
            );
            y += SMALL_LINE_HEIGHT;
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, SMALL_LINE_HEIGHT),
                "Phinix_legacyRedpacket_detailRemainingItemLine".Translate(snapshot.RemainingCount, snapshot.TotalCount)
            );
            y += SMALL_LINE_HEIGHT + SECTION_SPACING;

            Widgets.DrawLineHorizontal(inRect.xMin, y, inRect.width);
            y += SECTION_SPACING;

            Rect claimsRect = new Rect(inRect.xMin, y, inRect.width, inRect.yMax - y);
            DrawClaims(claimsRect);

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private void DrawItemIcon(Rect iconRect)
        {
            ThingDef iconDef = itemDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("UnknownItem");
            if (iconDef != null)
            {
                Widgets.ThingIcon(iconRect, iconDef, stuffDef, null, 1f);
            }
            else
            {
                GUI.DrawTexture(iconRect, BaseContent.BadTex);
            }
            Widgets.DrawBox(iconRect, 1);

            if (Mouse.IsOver(iconRect))
            {
                Widgets.DrawHighlight(iconRect);
            }

            if (iconDef != null && Widgets.ButtonInvisible(iconRect))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(iconDef));
            }
        }

        private void DrawClaims(Rect inRect)
        {
            float contentHeight = Mathf.Max(inRect.height, (snapshot.Claims.Count > 0 ? snapshot.Claims.Count : 1) * CLAIM_LINE_HEIGHT);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(inRect, ref claimsScroll, viewRect);

            if (snapshot.Claims.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, viewRect.width, CLAIM_LINE_HEIGHT), "Phinix_legacyRedpacket_detailNoClaims".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.EndScrollView();
                return;
            }

            float y = 0f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int i = 0; i < snapshot.Claims.Count; i++)
            {
                RedPacketClaimSnapshot claim = snapshot.Claims[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, CLAIM_LINE_HEIGHT);
                if (i % 2 == 1)
                {
                    Widgets.DrawHighlight(rowRect);
                }

                Rect nameRect = new Rect(rowRect.xMin + 4f, rowRect.y, rowRect.width * 0.58f, rowRect.height);
                Rect rightRect = new Rect(nameRect.xMax + 6f, rowRect.y, rowRect.xMax - nameRect.xMax - 6f, rowRect.height);
                Widgets.Label(nameRect, claim.Name);

                string amountText = "Phinix_legacyRedpacket_detailClaimAmount".Translate(claim.Amount);
                Vector2 amountSize = Text.CalcSize(amountText);
                Rect amountRect = new Rect(rightRect.xMax - amountSize.x, rowRect.y, amountSize.x, rowRect.height);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(amountRect, amountText);

                bool best = snapshot.IsFullyClaimed
                    && !string.IsNullOrEmpty(snapshot.LuckiestUuid)
                    && claim.Uuid == snapshot.LuckiestUuid;
                if (best)
                {
                    string bestText = "Phinix_legacyRedpacket_detailBestTag".Translate();
                    Vector2 bestSize = Text.CalcSize(bestText);
                    Rect bestRect = new Rect(amountRect.x - 10f - bestSize.x, rowRect.y, bestSize.x, rowRect.height);
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.84f, 0.2f, 1f);
                    Widgets.Label(bestRect, bestText);
                    GUI.color = oldColor;
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                y += CLAIM_LINE_HEIGHT;
            }

            Widgets.EndScrollView();
        }
    }
}
