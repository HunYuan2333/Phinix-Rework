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
        private const float TEXT_RENDERING_PADDING = 8f;
        private const float BEST_TAG_RENDERING_PADDING = 4f;
        private const int VIRTUAL_LIST_OVERSCAN = 1;

        private readonly RedPacketDetailSnapshot snapshot;
        private Vector2 claimsScroll = Vector2.zero;
        private readonly ThingDef itemDef;
        private readonly ThingDef stuffDef;
        private object cachedLanguage;
        private float cachedHeaderWidth = -1f;
        private string cachedTitle;
        private string cachedPacketAmount;
        private string cachedRemainingAmount;
        private float cachedTitleHeight;
        private float cachedPacketAmountHeight;
        private float cachedRemainingAmountHeight;
        private string cachedBestText;
        private float cachedBestWidth;
        private string[] cachedClaimAmountTexts;
        private float[] cachedClaimAmountWidths;
        private string[] cachedClaimTooltips;

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
            EnsureHeaderCache(inRect.width);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect titleRect = new Rect(inRect.xMin, y, inRect.width, cachedTitleHeight);
            Widgets.Label(titleRect, cachedTitle);
            TooltipHandler.TipRegion(titleRect, cachedTitle);
            y += cachedTitleHeight + SECTION_SPACING;

            float iconSize = Mathf.Min(ICON_SIZE, Mathf.Max(0f, inRect.width));
            Rect iconRect = new Rect(inRect.xMin + (inRect.width - iconSize) / 2f, y, iconSize, iconSize);
            DrawItemIcon(iconRect);
            y = iconRect.yMax + SECTION_SPACING;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, cachedPacketAmountHeight),
                cachedPacketAmount
            );
            y += cachedPacketAmountHeight;
            Widgets.Label(
                new Rect(inRect.xMin, y, inRect.width, cachedRemainingAmountHeight),
                cachedRemainingAmount
            );
            y += cachedRemainingAmountHeight + SECTION_SPACING;

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
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, inRect.width - 16f), contentHeight);
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
            EnsureClaimTextCache();

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

                bool best = snapshot.IsFullyClaimed
                    && !string.IsNullOrEmpty(snapshot.LuckiestUuid)
                    && claim.Uuid == snapshot.LuckiestUuid;
                RedPacketClaimRowLayout layout = RedPacketClaimRowLayout.Calculate(
                    rowRect,
                    cachedClaimAmountWidths[i],
                    cachedBestWidth,
                    best);

                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(layout.AmountRect, cachedClaimAmountTexts[i]);

                if (layout.ShowBest)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.84f, 0.2f, 1f);
                    Widgets.Label(layout.BestRect, cachedBestText);
                    GUI.color = oldColor;
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.LabelEllipses(layout.NameRect, claim.Name);
                TooltipHandler.TipRegion(rowRect, cachedClaimTooltips[i]);

            }

            Widgets.EndScrollView();
        }

        private void EnsureHeaderCache(float width)
        {
            object activeLanguage = LanguageDatabase.activeLanguage;
            if (ReferenceEquals(cachedLanguage, activeLanguage) && Mathf.Approximately(cachedHeaderWidth, width)) return;

            cachedLanguage = activeLanguage;
            cachedHeaderWidth = width;
            float measureWidth = Mathf.Max(1f, width);

            Text.Font = GameFont.Medium;
            cachedTitle = "Phinix_legacyRedpacket_detailSenderTitle".Translate(snapshot.SenderName);
            cachedTitleHeight = Mathf.Clamp(Text.CalcHeight(cachedTitle, measureWidth), TITLE_HEIGHT, 84f);

            Text.Font = GameFont.Tiny;
            cachedPacketAmount = "Phinix_legacyRedpacket_detailPacketAmountLine".Translate(snapshot.RemainingPackets, snapshot.TotalPackets);
            cachedPacketAmountHeight = Mathf.Max(SMALL_LINE_HEIGHT, Text.CalcHeight(cachedPacketAmount, measureWidth));
            cachedRemainingAmount = "Phinix_legacyRedpacket_detailRemainingItemLine".Translate(snapshot.RemainingCount, snapshot.TotalCount);
            cachedRemainingAmountHeight = Mathf.Max(SMALL_LINE_HEIGHT, Text.CalcHeight(cachedRemainingAmount, measureWidth));

            InvalidateClaimTextCache();
        }

        private void EnsureClaimTextCache()
        {
            int count = snapshot.Claims.Count;
            if (cachedClaimAmountTexts != null
                && cachedClaimAmountTexts.Length == count
                && ReferenceEquals(cachedLanguage, LanguageDatabase.activeLanguage)) return;

            cachedLanguage = LanguageDatabase.activeLanguage;
            cachedBestText = "Phinix_legacyRedpacket_detailBestTag".Translate();
            cachedBestWidth = Mathf.Ceil(Text.CalcSize(cachedBestText).x) + BEST_TAG_RENDERING_PADDING;
            cachedClaimAmountTexts = new string[count];
            cachedClaimAmountWidths = new float[count];
            cachedClaimTooltips = new string[count];

            for (int i = 0; i < count; i++)
            {
                RedPacketClaimSnapshot claim = snapshot.Claims[i];
                string amountText = "Phinix_legacyRedpacket_detailClaimAmount".Translate(claim.Amount);
                bool best = snapshot.IsFullyClaimed
                    && !string.IsNullOrEmpty(snapshot.LuckiestUuid)
                    && claim.Uuid == snapshot.LuckiestUuid;
                cachedClaimAmountTexts[i] = amountText;
                cachedClaimAmountWidths[i] = Mathf.Ceil(Text.CalcSize(amountText).x) + TEXT_RENDERING_PADDING;
                cachedClaimTooltips[i] = claim.Name + " — " + amountText + (best ? " — " + cachedBestText : string.Empty);
            }
        }

        private void InvalidateClaimTextCache()
        {
            cachedClaimAmountTexts = null;
            cachedClaimAmountWidths = null;
            cachedClaimTooltips = null;
        }

        private void ClampToSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(
                windowRect,
                new Vector2(MINIMUM_WINDOW_WIDTH, MINIMUM_WINDOW_HEIGHT));
        }
    }
}
