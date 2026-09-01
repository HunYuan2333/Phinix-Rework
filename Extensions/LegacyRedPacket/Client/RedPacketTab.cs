using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Phinix.TradeExtension.Client;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.Trade;
using RimWorld;
using UnityEngine;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包 Tab（Rework UI 新流程：IMainTabProvider）。
    /// 布局/交互逻辑与原 submod RedPacketTab 保持一致；依赖全部改为框架服务与插件实例。
    /// </summary>
    internal sealed class RedPacketTab : IMainTabProvider
    {
        private const float DEFAULT_SPACING = 10f;
        private const float SCROLLBAR_WIDTH = 16f;
        private const float LEFT_COLUMN_WIDTH = 420f;
        private const float TITLE_HEIGHT = 24f;
        private const float CONTROL_HEIGHT = 30f;
        private const float BUTTON_HEIGHT = 30f;
        private const float SEARCH_LABEL_WIDTH = 60f;
        private const float REFRESH_BUTTON_WIDTH = 80f;
        private const float ROW_HEIGHT_MIN = 30f;
        private const float PACKET_BUTTON_WIDTH = 90f;
        private const float PACKET_BUTTON_LEFT_SHIFT = 8f;
        private const float PACKET_ROW_HEIGHT = 70f;
        private const float PACKET_ICON_SIZE = 30f;
        private const float PACKET_LINE_SPACING = 2f;
        private const float ITEM_ICON_WIDTH = 30f;
        private const float COUNT_FIELD_WIDTH = 70f;
        private const float AVAILABLE_COUNT_WIDTH = 70f;
        private const float RIGHT_PADDING = 5f;
        private const int SEND_COOLDOWN_SECONDS = 60;

        private static readonly Regex ItemCountInputRegex = new Regex("\\d*");

        private readonly IClientSessionContext session;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;
        private readonly RedPacketSettings settings;
        private readonly IFrameworkClientTransport transport;
        private readonly RedPacketStateMachine stateMachine;
        private readonly Action<string, LogLevel> log;

        private Vector2 availableItemsScroll = Vector2.zero;
        private Vector2 packetListScroll = Vector2.zero;

        private List<StackedThings> availableItems = new List<StackedThings>();
        private List<StackedThings> filteredItems = new List<StackedThings>();
        private RedPacket[] cachedDisplayedPackets = Array.Empty<RedPacket>();
        private int cachedBadgeVersion = -1;

        private string searchText = string.Empty;
        private string packetCountText = "1";
        private StackedThings selectedStack;
        private RedPacketType selectedType = RedPacketType.Normal;
        private DateTime nextSendAllowedUtc = DateTime.MinValue;

        public RedPacketTab(
            IClientSessionContext session,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            RedPacketSettings settings,
            IFrameworkClientTransport transport,
            RedPacketStateMachine stateMachine,
            Action<string, LogLevel> log)
        {
            this.session = session;
            this.userDirectory = userDirectory;
            this.settingsContext = settingsContext;
            this.settings = settings;
            this.transport = transport;
            this.stateMachine = stateMachine;
            this.log = log;
        }

        public string TabLabel => "Phinix_legacyRedpacket_tab".Translate();

        public float TabOrder => 1100f;

        private bool IsOnline => session != null && session.Authenticated && session.LoggedIn;

        private string LocalUuid => session?.Uuid ?? string.Empty;

        public void Draw(Rect inRect)
        {
            if (!IsOnline)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_legacyRedpacket_pleaseLogInPlaceholder".Translate());
                return;
            }

            EnsureItemsFresh();

            float leftWidth = Mathf.Min(LEFT_COLUMN_WIDTH, inRect.width * 0.48f);
            float rightWidth = inRect.width - leftWidth - DEFAULT_SPACING;
            if (rightWidth < 200f)
            {
                leftWidth = inRect.width * 0.45f;
                rightWidth = inRect.width - leftWidth - DEFAULT_SPACING;
            }

            Rect leftRect = new Rect(inRect.xMin, inRect.yMin, leftWidth, inRect.height);
            Rect rightRect = new Rect(leftRect.xMax + DEFAULT_SPACING, inRect.yMin, rightWidth, inRect.height);

            DrawSendPanel(leftRect);
            DrawPacketList(rightRect);
        }

        private void EnsureItemsFresh()
        {
            if (!availableItems.Any())
            {
                RefreshAvailableItems();
            }
        }

        private void RefreshAvailableItems()
        {
            List<Map> homeMaps = Find.Maps.Where(map => map != null && map.IsPlayerHome).ToList();
            bool allItemsTradable = settingsContext != null && settingsContext.Get("trade.allItemsTradable", false);
            availableItems = StackedThings.GroupThings(
                StoredThingCollector.Collect(homeMaps, allItemsTradable, log).Where(thing =>
                    thing.def.category == ThingCategory.Item
                    && !thing.def.IsCorpse
                    && !(thing is MinifiedThing)),
                log
            );

            selectedStack = null;
            UpdateFilteredItems();
        }

        private void UpdateFilteredItems()
        {
            if (string.IsNullOrEmpty(searchText))
            {
                filteredItems = availableItems.Where(stack => stack.Count > 0).ToList();
                return;
            }

            filteredItems = availableItems
                .Where(stack => stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1)
                .ToList();
        }

        private void DrawSendPanel(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            Rect contentRect = inRect.ContractedBy(DEFAULT_SPACING);

            float y = contentRect.y;

            Rect titleRect = new Rect(contentRect.x, y, contentRect.width, TITLE_HEIGHT);
            y += TITLE_HEIGHT + DEFAULT_SPACING;

            Rect searchLabelRect = new Rect(contentRect.x, y, SEARCH_LABEL_WIDTH, CONTROL_HEIGHT);
            Rect refreshRect = new Rect(contentRect.xMax - REFRESH_BUTTON_WIDTH, y, REFRESH_BUTTON_WIDTH, CONTROL_HEIGHT);
            Rect searchFieldRect = new Rect(
                searchLabelRect.xMax + DEFAULT_SPACING,
                y,
                refreshRect.xMin - (searchLabelRect.xMax + DEFAULT_SPACING) - DEFAULT_SPACING,
                CONTROL_HEIGHT
            );
            y += CONTROL_HEIGHT + DEFAULT_SPACING;

            float controlsHeight = CONTROL_HEIGHT * 2 + BUTTON_HEIGHT + DEFAULT_SPACING * 2;
            Rect controlsRect = new Rect(contentRect.x, contentRect.yMax - controlsHeight, contentRect.width, controlsHeight);
            Rect listRect = new Rect(contentRect.x, y, contentRect.width, Math.Max(0f, controlsRect.yMin - y - DEFAULT_SPACING));

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(titleRect, "Phinix_legacyRedpacket_sendTitle".Translate());
            Text.Font = previousFont;

            Widgets.Label(searchLabelRect, "Phinix_legacyRedpacket_searchLabel".Translate());
            string oldSearch = searchText;
            searchText = Widgets.TextField(searchFieldRect, searchText);
            if (!searchText.Equals(oldSearch, StringComparison.Ordinal))
            {
                UpdateFilteredItems();
            }

            if (Widgets.ButtonText(refreshRect, "Phinix_legacyRedpacket_refreshButton".Translate()))
            {
                RefreshAvailableItems();
            }

            DrawAvailableItems(listRect);

            Rect packetRowRect = new Rect(controlsRect.x, controlsRect.y, controlsRect.width, CONTROL_HEIGHT);
            Rect typeRowRect = new Rect(controlsRect.x, packetRowRect.yMax + DEFAULT_SPACING, controlsRect.width, CONTROL_HEIGHT);
            Rect sendButtonRect = new Rect(controlsRect.x, typeRowRect.yMax + DEFAULT_SPACING, controlsRect.width, BUTTON_HEIGHT);

            Rect packetLabelRect = new Rect(packetRowRect.x, packetRowRect.y, SEARCH_LABEL_WIDTH, CONTROL_HEIGHT);
            Rect packetFieldRect = new Rect(packetLabelRect.xMax + DEFAULT_SPACING, packetRowRect.y, packetRowRect.width - packetLabelRect.width - DEFAULT_SPACING, CONTROL_HEIGHT);
            Widgets.Label(packetLabelRect, "Phinix_legacyRedpacket_packetCountLabel".Translate());
            packetCountText = Widgets.TextField(packetFieldRect, packetCountText);

            Rect typeLabelRect = new Rect(typeRowRect.x, typeRowRect.y, SEARCH_LABEL_WIDTH, CONTROL_HEIGHT);
            float typeButtonWidth = (typeRowRect.width - typeLabelRect.width - DEFAULT_SPACING * 3) / 2f;
            Rect normalRect = new Rect(typeLabelRect.xMax + DEFAULT_SPACING, typeRowRect.y, typeButtonWidth, CONTROL_HEIGHT);
            Rect luckyRect = new Rect(normalRect.xMax + DEFAULT_SPACING, typeRowRect.y, typeButtonWidth, CONTROL_HEIGHT);

            Widgets.Label(typeLabelRect, "Phinix_legacyRedpacket_typeLabel".Translate());
            DrawTypeButton(normalRect, "Phinix_legacyRedpacket_typeNormal".Translate(), selectedType == RedPacketType.Normal);
            DrawTypeButton(luckyRect, "Phinix_legacyRedpacket_typeLucky".Translate(), selectedType == RedPacketType.Lucky);

            if (Widgets.ButtonInvisible(normalRect))
            {
                selectedType = RedPacketType.Normal;
            }

            if (Widgets.ButtonInvisible(luckyRect))
            {
                selectedType = RedPacketType.Lucky;
            }

            DateTime now = DateTime.UtcNow;
            bool onCooldown = now < nextSendAllowedUtc;
            int cooldownSeconds = onCooldown ? (int) Math.Ceiling((nextSendAllowedUtc - now).TotalSeconds) : 0;
            string sendLabel = onCooldown
                ? "Phinix_legacyRedpacket_sendCooldownButton".Translate(cooldownSeconds)
                : "Phinix_legacyRedpacket_sendButton".Translate();

            bool previousEnabled = GUI.enabled;
            GUI.enabled = !onCooldown;
            if (Widgets.ButtonText(sendButtonRect, sendLabel))
            {
                TrySendPacket();
            }
            GUI.enabled = previousEnabled;
        }

        private void DrawAvailableItems(Rect inRect)
        {
            // 直接遍历过滤后的列表，避免每帧 ToList 分配
            int drawCount = 0;
            for (int i = 0; i < filteredItems.Count; i++)
            {
                if (filteredItems[i].Count > 0) drawCount++;
            }

            if (drawCount == 0)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_legacyRedpacket_noItemsPlaceholder".Translate());
                return;
            }

            float rowHeight = Math.Max(ROW_HEIGHT_MIN, ITEM_ICON_WIDTH);
            bool scrollbarsPresent = rowHeight * drawCount > inRect.height;
            Rect contentRect = new Rect(
                inRect.xMin,
                inRect.yMin,
                scrollbarsPresent ? inRect.width - SCROLLBAR_WIDTH : inRect.width,
                rowHeight * drawCount
            );
            bool scrollRequired = contentRect.height > inRect.height;
            if (scrollRequired) Widgets.BeginScrollView(inRect, ref availableItemsScroll, contentRect);

            bool alternateBackground = false;
            float currentY = contentRect.yMin;
            for (int i = 0; i < filteredItems.Count; i++)
            {
                StackedThings stack = filteredItems[i];
                if (stack.Things.Count == 0) continue;

                Rect rowRect = new Rect(contentRect.xMin, currentY, contentRect.width, rowHeight);
                if (alternateBackground || selectedStack == stack) Widgets.DrawHighlight(rowRect);

                Rect iconRect = rowRect.LeftPartPixels(ITEM_ICON_WIDTH);
                Widgets.ThingIcon(iconRect, stack.ThingDef, stack.StuffDef, stack.StyleDef, 0.9f);

                float inputAreaWidth = COUNT_FIELD_WIDTH + AVAILABLE_COUNT_WIDTH + DEFAULT_SPACING;
                Rect inputAreaRect = new Rect(rowRect.xMax - (RIGHT_PADDING + inputAreaWidth), rowRect.yMin, inputAreaWidth, rowRect.height);
                Rect quantityFieldRect = new Rect(inputAreaRect.xMin, inputAreaRect.yMin, COUNT_FIELD_WIDTH, inputAreaRect.height);
                Rect availableCountRect = new Rect(quantityFieldRect.xMax + DEFAULT_SPACING, inputAreaRect.yMin, AVAILABLE_COUNT_WIDTH, inputAreaRect.height);

                Rect itemNameRect = new Rect(iconRect.xMax + DEFAULT_SPACING, rowRect.yMin, inputAreaRect.xMin - iconRect.xMax - (DEFAULT_SPACING * 2), rowRect.height);
                if (itemNameRect.width < 0f) itemNameRect.width = 0f;

                int oldSelected = stack.Selected;

                string buf = stack.Selected == 0 ? string.Empty : stack.Selected.ToString();
                buf = Widgets.TextField(quantityFieldRect, buf, 100, ItemCountInputRegex);
                stack.Selected = string.IsNullOrEmpty(buf) ? 0 : Mathf.Clamp(int.Parse(buf), 0, stack.Count);

                TextAnchor previousAnchor = Text.Anchor;
                GameFont previousFont = Text.Font;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Widgets.Label(availableCountRect, "/ " + stack.Count);
                Widgets.LabelFit(itemNameRect, stack.Label);
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;

                if (stack.Selected != oldSelected)
                {
                    OnSelectedChanged(stack, stack.Selected);
                }

                alternateBackground = !alternateBackground;
                currentY += rowHeight;
            }

            if (scrollRequired) Widgets.EndScrollView();
        }

        private void DrawPacketList(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            Rect contentRect = inRect.ContractedBy(DEFAULT_SPACING);

            float toggleWidth = Mathf.Min(180f, contentRect.width * 0.45f);
            Rect toggleRect = new Rect(contentRect.xMax - toggleWidth, contentRect.y, toggleWidth, TITLE_HEIGHT);
            float titleWidth = contentRect.width - toggleWidth - DEFAULT_SPACING;
            if (titleWidth < 0f) titleWidth = contentRect.width;
            Rect titleRect = new Rect(contentRect.x, contentRect.y, titleWidth, TITLE_HEIGHT);
            Rect listRect = new Rect(contentRect.x, titleRect.yMax + DEFAULT_SPACING, contentRect.width, contentRect.height - TITLE_HEIGHT - DEFAULT_SPACING);

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.LabelFit(titleRect, "Phinix_legacyRedpacket_listTitle".Translate());
            Text.Font = GameFont.Small;
            bool notificationsEnabled = settings != null && settings.EnableNotifications;
            bool newValue = notificationsEnabled;
            Widgets.CheckboxLabeled(toggleRect, "Phinix_legacyRedpacket_notificationToggle".Translate(), ref newValue);
            if (newValue != notificationsEnabled)
            {
                settings?.Save(settingsContext, RedPacketSettings.KeyNotifications, newValue);
            }
            Text.Font = previousFont;

            DateTime nowUtc = DateTime.UtcNow;
            string localUuid = LocalUuid;
            RedPacket[] packets = GetDisplayedPackets(nowUtc, localUuid);
            if (packets.Length == 0)
            {
                Widgets.DrawMenuSection(listRect);
                if (stateMachine.TestPingReceived)
                {
                    Rect testRect = listRect.TopPartPixels(24f);
                    TextAnchor previousAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(testRect, "Phinix_legacyRedpacket_testSuccess".Translate());
                    Text.Anchor = previousAnchor;
                }
                else
                {
                    Widgets.NoneLabelCenteredVertically(listRect, "Phinix_legacyRedpacket_noPacketsPlaceholder".Translate());
                }
                return;
            }

            Rect listBodyRect = listRect;
            if (stateMachine.TestPingReceived)
            {
                Rect testRect = listRect.TopPartPixels(24f);
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(testRect, "Phinix_legacyRedpacket_testSuccess".Translate());
                Text.Anchor = previousAnchor;
                listBodyRect = new Rect(listRect.x, testRect.yMax + DEFAULT_SPACING, listRect.width, listRect.height - (testRect.height + DEFAULT_SPACING));
            }

            float viewWidth = listBodyRect.width - SCROLLBAR_WIDTH;
            GameFont previousListFont = Text.Font;
            Text.Font = GameFont.Small;
            float lineHeight = Text.LineHeight;
            float rowHeight = Mathf.Max(PACKET_ROW_HEIGHT, (lineHeight * 4f) - (PACKET_LINE_SPACING * 3f) + 4f);
            Text.Font = previousListFont;

            Rect viewRect = new Rect(0f, 0f, viewWidth, rowHeight * packets.Length);
            float currentY = 0f;

            Widgets.BeginScrollView(listBodyRect, ref packetListScroll, viewRect);
            for (int i = 0; i < packets.Length; i++)
            {
                RedPacket packet = packets[i];

                Rect rowRect = new Rect(viewRect.xMin, currentY, viewRect.width, rowHeight);
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);
                Widgets.DrawBoxSolidWithOutline(rowRect, Color.clear, new Color(1f, 1f, 1f, 0.15f), 1);

                Rect textRect = new Rect(rowRect.xMin, rowRect.yMin, rowRect.width - (PACKET_BUTTON_WIDTH + DEFAULT_SPACING), rowRect.height);
                Rect buttonRect = new Rect(
                    textRect.xMax + DEFAULT_SPACING - PACKET_BUTTON_LEFT_SHIFT,
                    rowRect.yMin + (rowRect.height - BUTTON_HEIGHT) / 2f,
                    PACKET_BUTTON_WIDTH,
                    BUTTON_HEIGHT
                );

                DrawPacketRowText(textRect, packet);
                DrawPacketRowAction(buttonRect, packet);

                currentY += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private RedPacket[] GetDisplayedPackets(DateTime nowUtc, string localUuid)
        {
            int version = stateMachine.BadgeVersion;
            if (cachedBadgeVersion == version)
            {
                return cachedDisplayedPackets;
            }

            cachedBadgeVersion = version;
            cachedDisplayedPackets = stateMachine.GetPacketsSnapshot()
                .Where(packet => stateMachine.ShouldDisplayInList(packet, nowUtc, localUuid))
                .ToArray();
            return cachedDisplayedPackets;
        }

        private void DrawPacketRowText(Rect inRect, RedPacket packet)
        {
            string itemLabel = packet.Template != null ? packet.Template.DefName : "???";
            ThingDef def = packet.Template != null ? DefDatabase<ThingDef>.GetNamedSilentFail(packet.Template.DefName) : null;
            if (def != null) itemLabel = def.LabelCap;

            ThingDef iconDef = def ?? DefDatabase<ThingDef>.GetNamedSilentFail("UnknownItem");
            ThingDef stuffDef = null;
            if (packet.Template != null && !string.IsNullOrEmpty(packet.Template.StuffDefName))
            {
                stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(packet.Template.StuffDefName);
            }

            string typeLabel = packet.Type == RedPacketType.Lucky
                ? "Phinix_legacyRedpacket_typeLucky".Translate()
                : "Phinix_legacyRedpacket_typeNormal".Translate();

            string senderName = packet.SenderDisplayName;
            if (string.IsNullOrEmpty(senderName) && TryGetDisplayName(packet.SenderUuid, out string displayName))
            {
                senderName = TextHelper.StripRichText(displayName);
            }
            if (string.IsNullOrEmpty(senderName)) senderName = "???";

            TimeSpan timeLeft = packet.ExpiresAtUtc - DateTime.UtcNow;
            if (timeLeft < TimeSpan.Zero) timeLeft = TimeSpan.Zero;
            string timeValue = string.Format("{0:D2}:{1:D2}", (int) timeLeft.TotalMinutes, timeLeft.Seconds);
            bool isEmpty = packet.RemainingPackets <= 0 || packet.RemainingCount <= 0;

            string line1 = "Phinix_legacyRedpacket_itemLineFormat".Translate(itemLabel, packet.TotalCount);
            string line2 = "Phinix_legacyRedpacket_remainingTypeLine".Translate(packet.RemainingPackets, packet.TotalPackets, typeLabel);
            string line3 = "Phinix_legacyRedpacket_senderFormat".Translate(senderName);
            string line4 = isEmpty
                ? "Phinix_legacyRedpacket_rowClaimedOut".Translate()
                : packet.Expired
                    ? "Phinix_legacyRedpacket_expiredLabel".Translate()
                    : "Phinix_legacyRedpacket_timeLeftFormat".Translate(timeValue);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;

            float lineHeight = Text.LineHeight;
            float totalHeight = (lineHeight * 4f) - (PACKET_LINE_SPACING * 3f);
            float startY = inRect.yMin + Mathf.Max(0f, (inRect.height - totalHeight) / 2f);
            float textOffset = iconDef != null ? PACKET_ICON_SIZE + DEFAULT_SPACING : 0f;

            if (iconDef != null)
            {
                Rect iconRect = new Rect(inRect.xMin, inRect.yMin, PACKET_ICON_SIZE, PACKET_ICON_SIZE);
                Widgets.ThingIcon(iconRect, iconDef, stuffDef, null, 0.9f);
            }

            Rect line1Rect = new Rect(inRect.xMin + textOffset, startY, inRect.width - textOffset, lineHeight);
            Rect line2Rect = new Rect(inRect.xMin + textOffset, line1Rect.yMax - PACKET_LINE_SPACING, inRect.width - textOffset, lineHeight);
            Rect line3Rect = new Rect(inRect.xMin + textOffset, line2Rect.yMax - PACKET_LINE_SPACING, inRect.width - textOffset, lineHeight);
            Rect line4Rect = new Rect(inRect.xMin + textOffset, line3Rect.yMax - PACKET_LINE_SPACING, inRect.width - textOffset, lineHeight);

            Widgets.Label(line1Rect, line1);
            Widgets.Label(line2Rect, line2);
            Widgets.LabelEllipses(line3Rect, line3);
            Widgets.Label(line4Rect, line4);

            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            Text.WordWrap = previousWordWrap;
        }

        private void DrawPacketRowAction(Rect inRect, RedPacket packet)
        {
            string localUuid = LocalUuid;

            bool isEmpty = packet.RemainingPackets <= 0 || packet.RemainingCount <= 0;
            bool claimed = packet.HasClaimed(localUuid);
            bool pending = stateMachine.IsPendingClaim(packet.Id);
            bool isSender = !string.IsNullOrEmpty(localUuid) && packet.IsSender(localUuid);

            if (!packet.Expired && !isEmpty && !claimed && !pending && !isSender)
            {
                if (Widgets.ButtonText(inRect, "Phinix_legacyRedpacket_claimButton".Translate()))
                {
                    stateMachine.RequestClaim(packet);
                }
                return;
            }

            if (pending)
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "Phinix_legacyRedpacket_pendingLabel".Translate());
                Text.Anchor = previousAnchor;
                return;
            }

            if (isSender)
            {
                if (Widgets.ButtonText(inRect, "Phinix_legacyRedpacket_detailButton".Translate()))
                {
                    OpenPacketDetail(packet);
                }
                return;
            }

            string statusLabel = packet.Expired
                ? "Phinix_legacyRedpacket_expiredLabel".Translate()
                : isEmpty
                    ? "Phinix_legacyRedpacket_emptyLabel".Translate()
                    : "Phinix_legacyRedpacket_claimedLabel".Translate();
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(inRect, statusLabel);
            Text.Anchor = oldAnchor;
        }

        private void OpenPacketDetail(RedPacket packet)
        {
            if (packet == null) return;
            if (!stateMachine.TryGetPacketDetailSnapshot(packet.Id, out RedPacketDetailSnapshot snapshot))
            {
                Messages.Message("Phinix_legacyRedpacket_detailUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new RedPacketDetailWindow(snapshot));
        }

        private void OnSelectedChanged(StackedThings stack, int selected)
        {
            if (selected <= 0)
            {
                if (selectedStack == stack) selectedStack = null;
                return;
            }

            if (selectedStack != stack)
            {
                if (selectedStack != null)
                {
                    selectedStack.Selected = 0;
                }

                selectedStack = stack;
                packetCountText = "1";
            }

            if (selectedStack != null && int.TryParse(packetCountText, out int packetCount))
            {
                if (packetCount > selectedStack.Selected)
                {
                    packetCountText = selectedStack.Selected.ToString();
                }
            }
        }

        private void TrySendPacket()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextSendAllowedUtc)
            {
                int secondsLeft = (int) Math.Ceiling((nextSendAllowedUtc - now).TotalSeconds);
                Messages.Message("Phinix_legacyRedpacket_sendCooldownMessage".Translate(secondsLeft), MessageTypeDefOf.RejectInput);
                return;
            }

            if (selectedStack == null || selectedStack.Selected <= 0)
            {
                Messages.Message("Phinix_legacyRedpacket_errorSelectItem".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (selectedStack.Things.Any(thing => thing is MinifiedThing))
            {
                Messages.Message("Phinix_legacyRedpacket_minifiedSendReject".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (!int.TryParse(packetCountText, out int packetCount) || packetCount <= 0)
            {
                Messages.Message("Phinix_legacyRedpacket_errorPacketCount".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            int totalCount = selectedStack.Selected;
            if (packetCount > totalCount)
            {
                Messages.Message("Phinix_legacyRedpacket_errorPacketCountTooLarge".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            string itemLabel = selectedStack.Label;
            List<PoppedThing> poppedThings = null;
            try
            {
                poppedThings = selectedStack.PopSelectedWithOrigins().ToList();
                if (!poppedThings.Any())
                {
                    Messages.Message("Phinix_legacyRedpacket_errorSelectItem".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                foreach (PoppedThing poppedThing in poppedThings)
                {
                    poppedThing.DeSpawn();
                }

                List<Thing> selectedThings = poppedThings.Select(poppedThing => poppedThing.Thing).ToList();
                TradeItemSnapshot sourceSnapshot = TradeItemConverter.ConvertThingFromVerse(selectedThings[0]);
                TradeItemSnapshot template = new TradeItemSnapshot(
                    sourceSnapshot.DefName,
                    totalCount,
                    sourceSnapshot.HitPoints,
                    sourceSnapshot.Quality,
                    sourceSnapshot.StuffDefName,
                    sourceSnapshot.InnerItem);

                bool special = stateMachine.IsSpecialPacketItem(template);
                RedPacket packet = new RedPacket
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SenderUuid = LocalUuid,
                    SenderDisplayName = GetLocalDisplayName(),
                    Template = template,
                    TotalCount = totalCount,
                    RemainingCount = totalCount,
                    TotalPackets = packetCount,
                    RemainingPackets = packetCount,
                    Type = selectedType,
                    LuckyAlgorithmVersion = stateMachine.GetLuckyAlgorithmVersionForType(selectedType),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(special ? 1 : 10),
                    Expired = false
                };

                packet.StoredThings.AddRange(selectedThings);
                stateMachine.AddLocalPacket(packet);
            }
            catch (Exception exception)
            {
                RestorePoppedThings(poppedThings);
                selectedStack = null;
                RefreshAvailableItems();
                log?.Invoke(
                    $"[RedPacketTab] Failed to create a red packet; selected things were restored.{Environment.NewLine}{exception}",
                    LogLevel.ERROR);
                Messages.Message("Phinix_legacyRedpacket_errorSelectItem".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                TryBroadcastChatAnnouncement();
            }
            catch (Exception exception)
            {
                log?.Invoke(
                    $"[RedPacketTab] Red packet was created, but its chat announcement failed.{Environment.NewLine}{exception}",
                    LogLevel.WARNING);
            }

            Messages.Message("Phinix_legacyRedpacket_sentMessage".Translate(itemLabel, totalCount), MessageTypeDefOf.PositiveEvent);
            nextSendAllowedUtc = now.AddSeconds(SEND_COOLDOWN_SECONDS);

            selectedStack = null;
            packetCountText = "1";
            RefreshAvailableItems();
        }

        private void RestorePoppedThings(IEnumerable<PoppedThing> poppedThings)
        {
            List<Thing> unrestoredThings = PoppedThing.RestoreAll(poppedThings, log, "RedPacketTab.CreatePacket");
            if (unrestoredThings.Count == 0)
            {
                return;
            }

            bool dropCurrentMap = settingsContext != null && settingsContext.Get("trade.dropCurrentMap", false);
            try
            {
                RedPacketDropHelper.DropPodsWithLimit(unrestoredThings, dropCurrentMap);
                log?.Invoke(
                    $"[RedPacketTab] Returned {unrestoredThings.Count} thing(s) by drop pod after direct restoration failed.",
                    LogLevel.WARNING);
            }
            catch (Exception exception)
            {
                log?.Invoke(
                    $"[RedPacketTab] Failed to return {unrestoredThings.Count} thing(s) by drop pod.{Environment.NewLine}{exception}",
                    LogLevel.ERROR);
            }
        }

        private string GetLocalDisplayName()
        {
            if (TryGetDisplayName(LocalUuid, out string displayName))
            {
                return TextHelper.StripRichText(displayName);
            }

            return "???";
        }

        private bool TryGetDisplayName(string uuid, out string displayName)
        {
            displayName = null;
            if (string.IsNullOrEmpty(uuid) || userDirectory == null) return false;
            if (userDirectory.TryGetUser(uuid, out UserManagement.ImmutableUser user))
            {
                displayName = user.DisplayName;
                return true;
            }
            return false;
        }

        private void TryBroadcastChatAnnouncement()
        {
            if (!IsOnline) return;
            if (settings != null && !settings.EnableChatAnnouncement) return;
            if (LanguageDatabase.activeLanguage == null) return;

            string text = "Phinix_legacyRedpacket_chatAnnounceMessage".Translate();
            transport?.TryHandleOutgoingMessage(text);
        }

        private void DrawTypeButton(Rect inRect, string label, bool selected)
        {
            Widgets.DrawOptionBackground(inRect, selected);

            if (selected)
            {
                float checkSize = 16f;
                Rect checkRect = new Rect(
                    inRect.xMax - (checkSize + 6f),
                    inRect.y + (inRect.height - checkSize) / 2f,
                    checkSize,
                    checkSize
                );
                // 1.6 无 WidgetsWork.WorkBoxCheckTex，改用禁用态复选框绘制勾选标记
                bool checkOn = true;
                Widgets.Checkbox(new Vector2(checkRect.x, checkRect.y), ref checkOn, checkSize, disabled: true);
            }

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(inRect, label);
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }
    }
}
