using System;
using PhinixClient;
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
        private const float MINIMUM_WINDOW_WIDTH = 360f;
        private const float MINIMUM_WINDOW_HEIGHT = 320f;
        private const float PREFERRED_WINDOW_WIDTH = 640f;
        private const float PREFERRED_WINDOW_HEIGHT = 620f;
        private const int VIRTUAL_LIST_OVERSCAN = 1;

        private readonly RedPacketDetailSnapshot snapshot;
        private Vector2 claimsScroll = Vector2.zero;
        private readonly ThingDef itemDef;
        private readonly ThingDef stuffDef;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safeRect = UiScreenSafeArea.Current;
                return new Vector2(
                    Mathf.Min(PREFERRED_WINDOW_WIDTH, safeRect.width),
                    Mathf.Min(PREFERRED_WINDOW_HEIGHT, safeRect.height));
            }
        }

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
            resizeable = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampToSafeArea();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampToSafeArea();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (snapshot == null) return;

            float y = inRect.yMin - 2f;

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            string title = "Phinix_legacyRedpacket_detailSenderTitle".Translate(snapshot.SenderName);
            float titleHeight = Mathf.Clamp(Text.CalcHeight(title, Mathf.Max(1f, inRect.width)), TITLE_HEIGHT, 84f);
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, titleHeight),
                title
            );
            y += titleHeight + SECTION_SPACING;

            float iconSize = Mathf.Min(ICON_SIZE, Mathf.Max(0f, inRect.width));
            Rect iconRect = new Rect(inRect.xMin + (inRect.width - iconSize) / 2f, y, iconSize, iconSize);
            DrawItemIcon(iconRect);
            y = iconRect.yMax + SECTION_SPACING;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            string packetAmount = "Phinix_legacyRedpacket_detailPacketAmountLine".Translate(snapshot.RemainingPackets, snapshot.TotalPackets);
            float packetAmountHeight = Mathf.Max(SMALL_LINE_HEIGHT, Text.CalcHeight(packetAmount, Mathf.Max(1f, inRect.width)));
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, packetAmountHeight),
                packetAmount
            );
            y += packetAmountHeight;
            string remainingAmount = "Phinix_legacyRedpacket_detailRemainingItemLine".Translate(snapshot.RemainingCount, snapshot.TotalCount);
            float remainingAmountHeight = Mathf.Max(SMALL_LINE_HEIGHT, Text.CalcHeight(remainingAmount, Mathf.Max(1f, inRect.width)));
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, remainingAmountHeight),
                remainingAmount
            );
            y += remainingAmountHeight + SECTION_SPACING;

            Widgets.DrawLineHorizontal(inRect.xMin, y, inRect.width);
            y += SECTION_SPACING;

            Rect claimsRect = new Rect(inRect.xMin, y, inRect.width, Mathf.Max(0f, inRect.yMax - y));
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

            VirtualListRange visibleRange = VirtualListLayout.GetFixedRange(
                snapshot.Claims.Count, CLAIM_LINE_HEIGHT, claimsScroll.y, inRect.height, VIRTUAL_LIST_OVERSCAN);

            for (int i = visibleRange.FirstIndex; i < visibleRange.EndIndexExclusive; i++)
            {
                RedPacketClaimSnapshot claim = snapshot.Claims[i];
                y = i * CLAIM_LINE_HEIGHT;
                Rect rowRect = new Rect(0f, y, viewRect.width, CLAIM_LINE_HEIGHT);
                if (i % 2 == 1)
                {
                    Widgets.DrawHighlight(rowRect);
                }

                string amountText = "Phinix_legacyRedpacket_detailClaimAmount".Translate(claim.Amount);
                Vector2 amountSize = Text.CalcSize(amountText);
                float amountWidth = Mathf.Min(rowRect.width * 0.4f, amountSize.x);
                Rect amountRect = new Rect(rowRect.xMax - amountWidth - 4f, rowRect.y, amountWidth, rowRect.height);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.LabelEllipses(amountRect, amountText);

                bool best = snapshot.IsFullyClaimed
                    && !string.IsNullOrEmpty(snapshot.LuckiestUuid)
                    && claim.Uuid == snapshot.LuckiestUuid;
                float bestWidth = 0f;
                string bestText = null;
                if (best)
                {
                    bestText = "Phinix_legacyRedpacket_detailBestTag".Translate();
                    bestWidth = Mathf.Min(Text.CalcSize(bestText).x, rowRect.width * 0.28f);
                }

                float nameRight = amountRect.xMin - 6f;
                if (bestWidth > 0f && nameRight - bestWidth - 10f >= rowRect.xMin + 80f)
                {
                    Rect bestRect = new Rect(nameRight - bestWidth, rowRect.y, bestWidth, rowRect.height);
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.84f, 0.2f, 1f);
                    Widgets.LabelEllipses(bestRect, bestText);
                    GUI.color = oldColor;
                    nameRight = bestRect.xMin - 10f;
                }

                Rect nameRect = new Rect(rowRect.xMin + 4f, rowRect.y, Mathf.Max(0f, nameRight - rowRect.xMin - 4f), rowRect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.LabelEllipses(nameRect, claim.Name);
                string tooltip = claim.Name + " — " + amountText + (best ? " — " + bestText : string.Empty);
                TooltipHandler.TipRegion(rowRect, tooltip);

            }

            Widgets.EndScrollView();
        }

        private void ClampToSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(
                windowRect,
                new Vector2(MINIMUM_WINDOW_WIDTH, MINIMUM_WINDOW_HEIGHT));
        }
    }
}
