using System;
using System.Collections.Generic;
using System.Text;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace PhinixClient
{
    /// <summary>
    /// 扩展管理 Tab——展示已发现扩展的状态、依赖关系与实时日志，并提供启用/禁用操作（v1 重启生效）。
    ///
    /// 设计哲学 §1.1 插件平权：全部扩展可禁用，包括官方 Chat/Trade/LegacyAdapter。
    /// 设计哲学 §8.3 UI 性能：Draw 路径零分配——排序、截断、翻译、摘要均在缓存重建时完成，
    /// 仅在结果数量 / 设置版本 / 日志版本 / 面板宽度变化时重建。
    /// 设计哲学 §3.6：日志为有界缓冲（PhinixFrameworkClient 内维护），UI 只读快照。
    ///
    /// v1 无热重载，因此不提供刷新按钮；展示状态是（运行时发现结果 + 当前 Settings）的纯函数，
    /// 由 <see cref="ExtensionDisplayState.Compute"/> 静态计算，勾选后下一帧自动反映。
    /// </summary>
    public class ExtensionManagerTab : IMainTabProvider
    {
        private const float ROW_HEIGHT = 28f;
        private const float DEPS_ROW_HEIGHT = 16f;
        private const float CARD_PADDING = 8f;
        private const float DEFAULT_SPACING = 6f;
        private const float STATUS_ICON_WIDTH = 20f;
        private const float CHECKBOX_SIZE = 18f;
        private const float BOTTOM_BAR_HEIGHT = 40f;
        private const float LOG_HEADER_HEIGHT = 24f;
        private const float TOP_HEADER_HEIGHT = 24f;
        private const float LOG_LINE_HEIGHT = 18f;
        private const float SPLIT_RATIO = 0.6f;

        private const float COL_VERSION = 56f;
        private const float COL_SOURCE = 84f;
        private const float COL_STATE = 72f;
        private const float PENDING_WIDTH = 92f;
        private const float HINT_WIDTH = 250f;

        private Vector2 listScrollPosition;
        private Vector2 logScrollPosition;

        private IUiTheme cachedTheme;
        private CachedLayout cachedLayout;
        private List<CachedLogLine> cachedLogLines;
        private int cachedResultsCount = -1;
        private int cachedSettingsVersion = -1;
        private int cachedLogVersion = -1;
        private int cachedWidthBucket = -1;
        private int cachedLogWidthBucket = -1;

        public string TabLabel => "Phinix_tabs_extensions".Translate();

        public float TabOrder => 999;

        public void Draw(Rect inRect)
        {
            PhinixFrameworkClient frameworkClient = Client.Instance?.FrameworkClient;
            Settings hostSettings = Client.Instance?.Settings;
            IReadOnlyList<ExtensionDiscoveryResult> results = frameworkClient?.ExtensionResults
                ?? (IReadOnlyList<ExtensionDiscoveryResult>)Array.Empty<ExtensionDiscoveryResult>();

            if (cachedTheme == null && frameworkClient != null)
            {
                IReadOnlyList<IUiTheme> themes = frameworkClient.ResolveExtensionApis<IUiTheme>();
                if (themes != null && themes.Count > 0)
                {
                    cachedTheme = themes[0];
                }
            }
            IUiTheme theme = cachedTheme;

            // ── 区域划分：底部摘要栏 / 上方列表区 / 下方日志区 ──
            Rect bottomBarRect = inRect.BottomPartPixels(BOTTOM_BAR_HEIGHT);
            inRect.yMax -= BOTTOM_BAR_HEIGHT + DEFAULT_SPACING;

            Rect listSectionRect = inRect.TopPartPixels(inRect.height * SPLIT_RATIO);
            Rect logSectionRect = inRect.BottomPartPixels(inRect.height - listSectionRect.height);
            logSectionRect.yMin += DEFAULT_SPACING;

            int settingsVersion = hostSettings?.SettingsVersion ?? 0;
            int logVersion = frameworkClient?.ExtensionLogVersion ?? 0;
            int widthBucket = (int)(listSectionRect.width / 32f);

            if (cachedLayout == null ||
                cachedResultsCount != results.Count ||
                cachedSettingsVersion != settingsVersion ||
                cachedWidthBucket != widthBucket)
            {
                cachedLayout = BuildLayout(results, frameworkClient, hostSettings, theme, listSectionRect.width);
                cachedResultsCount = results.Count;
                cachedSettingsVersion = settingsVersion;
                cachedWidthBucket = widthBucket;
            }
            if (cachedLogLines == null || cachedLogVersion != logVersion || cachedLogWidthBucket != widthBucket)
            {
                cachedLogLines = BuildLogLines(frameworkClient, listSectionRect.width);
                cachedLogVersion = logVersion;
                cachedLogWidthBucket = widthBucket;
            }

            // ── 列表区 ──
            Widgets.Label(
                new Rect(listSectionRect.x, listSectionRect.y, listSectionRect.width, TOP_HEADER_HEIGHT),
                cachedLayout.LoadedHeaderText);

            Rect listAreaRect = new Rect(
                listSectionRect.x, listSectionRect.y + TOP_HEADER_HEIGHT + DEFAULT_SPACING,
                listSectionRect.width, listSectionRect.height - TOP_HEADER_HEIGHT - DEFAULT_SPACING);

            float cardHeight = ROW_HEIGHT + DEPS_ROW_HEIGHT + 2f + CARD_PADDING * 2f;
            float listInnerHeight = cachedLayout.Rows.Count * (cardHeight + DEFAULT_SPACING);
            Rect listInnerRect = new Rect(0f, 0f, listAreaRect.width - 16f, Mathf.Max(listInnerHeight, listAreaRect.height));

            Widgets.BeginScrollView(listAreaRect, ref listScrollPosition, listInnerRect);

            float currentY = 0f;
            for (int i = 0; i < cachedLayout.Rows.Count; i++)
            {
                DrawExtensionCard(
                    new Rect(0f, currentY, listInnerRect.width, cardHeight),
                    cachedLayout.Rows[i],
                    theme);
                currentY += cardHeight + DEFAULT_SPACING;
            }

            Widgets.EndScrollView();

            // ── 日志区 ──
            Widgets.Label(
                new Rect(logSectionRect.x, logSectionRect.y, logSectionRect.width, LOG_HEADER_HEIGHT),
                cachedLayout.LogHeaderText);

            Rect logAreaRect = new Rect(
                logSectionRect.x, logSectionRect.y + LOG_HEADER_HEIGHT,
                logSectionRect.width, logSectionRect.height - LOG_HEADER_HEIGHT);
            Rect logInnerRect = new Rect(
                0f, 0f, logAreaRect.width - 16f,
                Mathf.Max(cachedLogLines.Count * LOG_LINE_HEIGHT, logAreaRect.height));

            Widgets.BeginScrollView(logAreaRect, ref logScrollPosition, logInnerRect);

            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float logY = 0f;
            for (int i = 0; i < cachedLogLines.Count; i++)
            {
                CachedLogLine line = cachedLogLines[i];
                Color prevColor = UnityEngine.GUI.color;
                UnityEngine.GUI.color = line.Color;
                Widgets.Label(new Rect(0f, logY, logInnerRect.width, LOG_LINE_HEIGHT), line.Text);
                UnityEngine.GUI.color = prevColor;
                if (!string.IsNullOrEmpty(line.Tooltip))
                {
                    TooltipHandler.TipRegion(
                        new Rect(0f, logY, logInnerRect.width, LOG_LINE_HEIGHT), line.Tooltip);
                }
                logY += LOG_LINE_HEIGHT;
            }
            Text.Font = prevFont;

            Widgets.EndScrollView();

            // ── 底部摘要栏 ──
            DrawBottomBar(bottomBarRect, cachedLayout, theme);
        }

        /// <summary>
        /// 缓存重建：排序、截断、翻译、摘要全部在此完成，Draw 路径不再分配。
        /// </summary>
        private static CachedLayout BuildLayout(
            IReadOnlyList<ExtensionDiscoveryResult> results,
            PhinixFrameworkClient frameworkClient,
            Settings hostSettings,
            IUiTheme theme,
            float panelWidth)
        {
            CachedLayout layout = new CachedLayout();

            ExtensionDependencyGraph dependencyGraph = frameworkClient?.ExtensionDependencyGraph;
            IReadOnlyCollection<string> disabledIds = hostSettings?.DisabledExtensions;

            // 排序——active 在前，disabled 在后，同状态按 ID 字母序
            List<ExtensionDiscoveryResult> sorted = new List<ExtensionDiscoveryResult>(results);
            sorted.Sort((a, b) =>
            {
                int sa = getStateSortOrder(a.State);
                int sb = getStateSortOrder(b.State);
                if (sa != sb)
                {
                    return sa.CompareTo(sb);
                }
                return string.Compare(a.ExtensionId, b.ExtensionId, StringComparison.OrdinalIgnoreCase);
            });

            // 自适应列宽：ID / Name 分享剩余宽度，其余固定列（与 Draw 共用同一公式）
            float cardWidth = panelWidth - 16f;
            ComputeColumnWidths(cardWidth, out float colId, out float colName);
            float depsWidth = cardWidth - CARD_PADDING * 2f
                - (CHECKBOX_SIZE + DEFAULT_SPACING + 4f + STATUS_ICON_WIDTH + DEFAULT_SPACING);

            Text.Font = GameFont.Small;
            layout.Rows = new List<CachedRow>(sorted.Count);
            int activeCount = 0;
            int disabledCount = 0;
            int depDisabledCount = 0;
            int pendingCount = 0;
            int undeclaredCount = 0;
            StringBuilder impact = new StringBuilder(96);

            foreach (ExtensionDiscoveryResult result in sorted)
            {
                string extensionId = result.ExtensionId ?? "?";
                ExtensionDisplayState display = ExtensionDisplayState.Compute(result, disabledIds, dependencyGraph);

                CachedRow row = new CachedRow
                {
                    Result = result,
                    Display = display,
                    IdText = Truncate(extensionId, colId),
                    NameText = Truncate(result.DisplayName ?? "", colName),
                    VersionText = result.Version ?? "",
                    SourceText = Truncate(
                        !string.IsNullOrEmpty(result.SourcePackageId)
                            ? result.SourcePackageId
                            : (result.AssemblyName ?? ""),
                        COL_SOURCE),
                    StateText = GetStateLabel(display.RuntimeState),
                    StateColor = GetStatusColor(display.RuntimeState, theme),
                    CanToggle = result.State != ExtensionModuleState.DependencyDisabled,
                    Checked = hostSettings == null || !hostSettings.IsExtensionDisabled(extensionId),
                    DepDisabledHint = "Phinix_extensions_depDisabledHint".Translate()
                };

                row.PendingColor = new Color(0.95f, 0.75f, 0.25f);
                if (display.PendingChange == ExtensionPendingChange.WillDisableAfterRestart)
                {
                    row.PendingText = "Phinix_extensions_pendingDisable".Translate();
                    pendingCount++;
                }
                else if (display.PendingChange == ExtensionPendingChange.WillEnableAfterRestart)
                {
                    row.PendingText = "Phinix_extensions_pendingEnable".Translate();
                    row.PendingColor = new Color(0.55f, 0.75f, 1f);
                    pendingCount++;
                }

                Text.Font = GameFont.Tiny;
                IReadOnlyList<string> deps = result.DependsOn;
                bool isUndeclared = dependencyGraph != null && dependencyGraph.IsUndeclared(extensionId);
                if (deps != null && deps.Count > 0)
                {
                    row.DepsText = Truncate(
                        "Phinix_extensions_dependencies".Translate() + ": " + string.Join(", ", deps),
                        depsWidth);
                    row.DepsColor = new Color(0.6f, 0.6f, 0.6f);
                }
                else if (isUndeclared)
                {
                    row.DepsText = Truncate("Phinix_extensions_undeclaredDeps".Translate(), depsWidth);
                    row.DepsColor = new Color(0.55f, 0.55f, 0.55f);
                }
                else
                {
                    row.DepsText = Truncate(
                        "Phinix_extensions_dependencies".Translate() + ": " + "Phinix_extensions_none".Translate(),
                        depsWidth);
                    row.DepsColor = new Color(0.6f, 0.6f, 0.6f);
                }
                Text.Font = GameFont.Small;

                // 禁用影响 tooltip：当前被用户禁用且有依赖方的扩展
                if (dependencyGraph != null && row.Checked == false)
                {
                    IReadOnlyList<string> dependents = dependencyGraph.GetDependents(extensionId);
                    if (dependents.Count > 0)
                    {
                        row.Tooltip = "Phinix_extensions_disableImpact".Translate(
                            extensionId, string.Join(", ", dependents));
                    }
                }
                if (row.Tooltip == null && !string.IsNullOrEmpty(result.StateDetail))
                {
                    row.Tooltip = result.StateDetail;
                }

                layout.Rows.Add(row);

                if (display.RuntimeState == ExtensionModuleState.Active) activeCount++;
                if (display.EffectiveState == ExtensionModuleState.Disabled) disabledCount++;
                else if (display.EffectiveState == ExtensionModuleState.DependencyDisabled) depDisabledCount++;
                if (dependencyGraph != null && dependencyGraph.IsUndeclared(extensionId)) undeclaredCount++;

                if (display.Reason == ExtensionDisplayReason.UserDisabled && dependencyGraph != null)
                {
                    IReadOnlyList<string> dependents = dependencyGraph.GetDependents(extensionId);
                    if (dependents.Count > 0)
                    {
                        if (impact.Length > 0)
                        {
                            impact.Append("  |  ");
                        }
                        impact.Append("Phinix_extensions_disableImpact".Translate(
                            extensionId, string.Join(", ", dependents)));
                    }
                }
            }

            // 底部摘要
            StringBuilder summary = new StringBuilder(96);
            summary.Append("Phinix_extensions_summaryActiveTotal".Translate(activeCount, results.Count));
            if (disabledCount > 0)
            {
                summary.Append("  |  ").Append("Phinix_extensions_summaryDisabled".Translate(disabledCount));
            }
            if (depDisabledCount > 0)
            {
                summary.Append("  |  ").Append("Phinix_extensions_summaryDepDisabled".Translate(depDisabledCount));
            }
            if (undeclaredCount > 0)
            {
                summary.Append("  |  ").Append("Phinix_extensions_summaryUndeclared".Translate(undeclaredCount));
            }
            if (pendingCount > 0)
            {
                summary.Append("  |  ").Append("Phinix_extensions_summaryPending".Translate(pendingCount));
            }
            Text.Font = GameFont.Tiny;
            layout.SummaryText = Truncate(summary.ToString(), panelWidth - HINT_WIDTH - DEFAULT_SPACING - 8f);
            layout.ImpactText = impact.Length > 0 ? Truncate(impact.ToString(), panelWidth - 16f) : null;
            layout.LoadedHeaderText = "Phinix_extensions_loadedExtensions".Translate(results.Count);
            layout.LogHeaderText = "Phinix_extensions_loadingLog".Translate();
            layout.RestartHintText = "Phinix_modSettings_extensionsRestartRequired".Translate();

            Text.Font = GameFont.Small;
            return layout;
        }

        /// <summary>
        /// 日志行缓存：时间戳 + 级别着色 + 截断（完整文本放 tooltip）。
        /// 与扩展行缓存分离，高频日志追加不会触发整表重建。
        /// </summary>
        private static List<CachedLogLine> BuildLogLines(PhinixFrameworkClient frameworkClient, float panelWidth)
        {
            List<CachedLogLine> lines = new List<CachedLogLine>();
            if (frameworkClient != null)
            {
                IReadOnlyList<FrameworkLogEntry> snapshot = frameworkClient.GetExtensionLogSnapshot();
                float logWidth = panelWidth - 16f - 16f;
                Text.Font = GameFont.Tiny;
                foreach (FrameworkLogEntry entry in snapshot)
                {
                    string time = new DateTime(entry.TimestampUtcTicks, DateTimeKind.Utc)
                        .ToLocalTime().ToString("HH:mm:ss");
                    string full = "[" + time + "] " + entry.Message;
                    lines.Add(new CachedLogLine
                    {
                        Text = Truncate(full, logWidth),
                        Color = GetLogColor(entry.Level),
                        Tooltip = full
                    });
                }
            }
            if (lines.Count == 0)
            {
                lines.Add(new CachedLogLine
                {
                    Text = "Phinix_extensions_logEmpty".Translate(),
                    Color = new Color(0.55f, 0.55f, 0.55f)
                });
            }
            return lines;
        }

        private static void DrawExtensionCard(Rect rect, CachedRow row, IUiTheme theme)
        {
            // 卡片背景
            UnityEngine.GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.5f);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.5f));
            UnityEngine.GUI.color = Color.white;

            Rect mainRow = new Rect(
                rect.x + CARD_PADDING, rect.y + CARD_PADDING,
                rect.width - CARD_PADDING * 2f, ROW_HEIGHT);

            float x = mainRow.x;
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Small;
            Color prevGuiColor = UnityEngine.GUI.color;

            // 复选框——反映用户偏好（Settings），运行时状态仅在重启后变化
            if (row.CanToggle)
            {
                bool newChecked = row.Checked;
                float checkboxY = mainRow.y + (mainRow.height - CHECKBOX_SIZE) / 2f;
                Widgets.Checkbox(new Vector2(x, checkboxY), ref newChecked, CHECKBOX_SIZE);
                if (newChecked != row.Checked && Client.Instance?.Settings != null)
                {
                    Settings hostSettings = Client.Instance.Settings;
                    hostSettings.SetExtensionDisabled(row.Result.ExtensionId, !newChecked);
                    hostSettings.AcceptChanges();
                    string message = (newChecked
                        ? "Phinix_extensions_toggleEnabled"
                        : "Phinix_extensions_toggleDisabled").Translate(row.Result.ExtensionId);
                    Messages.Message(message, MessageTypeDefOf.NeutralEvent);
                }
            }
            else
            {
                // 依赖禁用：灰色不可交互
                UnityEngine.GUI.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                Widgets.DrawBoxSolid(
                    new Rect(x, mainRow.y + (mainRow.height - CHECKBOX_SIZE) / 2f, CHECKBOX_SIZE, CHECKBOX_SIZE),
                    new Color(0.3f, 0.3f, 0.3f, 0.3f));
                UnityEngine.GUI.color = prevGuiColor;
                TooltipHandler.TipRegion(
                    new Rect(x, mainRow.y, CHECKBOX_SIZE + DEFAULT_SPACING, mainRow.height),
                    row.DepDisabledHint);
            }
            x += CHECKBOX_SIZE + DEFAULT_SPACING + 4f;

            // 状态图标
            UnityEngine.GUI.color = row.StateColor;
            Widgets.Label(new Rect(x, mainRow.y, STATUS_ICON_WIDTH, mainRow.height), GetStatusIcon(row.Result.State));
            UnityEngine.GUI.color = prevGuiColor;
            x += STATUS_ICON_WIDTH + DEFAULT_SPACING;

            // ID / Name 共享剩余宽度（文本已在缓存时截断）
            ComputeColumnWidths(rect.width, out float colId, out float colName);

            UnityEngine.GUI.color = new Color(0.8f, 0.85f, 1f);
            Widgets.Label(new Rect(x, mainRow.y, colId, mainRow.height), row.IdText);
            UnityEngine.GUI.color = prevGuiColor;
            x += colId + DEFAULT_SPACING;

            Widgets.Label(new Rect(x, mainRow.y, colName, mainRow.height), row.NameText);
            x += colName + DEFAULT_SPACING;

            UnityEngine.GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(x, mainRow.y, COL_VERSION, mainRow.height), row.VersionText);
            UnityEngine.GUI.color = prevGuiColor;
            x += COL_VERSION + DEFAULT_SPACING;

            Widgets.Label(new Rect(x, mainRow.y, COL_SOURCE, mainRow.height), row.SourceText);
            x += COL_SOURCE + DEFAULT_SPACING;

            UnityEngine.GUI.color = row.StateColor;
            Widgets.Label(new Rect(x, mainRow.y, COL_STATE, mainRow.height), row.StateText);
            UnityEngine.GUI.color = prevGuiColor;

            // 待重启变更标记（列宽计算已预留空间，始终可显示）
            if (!string.IsNullOrEmpty(row.PendingText))
            {
                float pendingX = x + COL_STATE + DEFAULT_SPACING;
                float pendingWidth = Mathf.Min(PENDING_WIDTH, rect.xMax - CARD_PADDING - pendingX);
                UnityEngine.GUI.color = row.PendingColor;
                Widgets.Label(new Rect(pendingX, mainRow.y, Mathf.Max(0f, pendingWidth), mainRow.height), row.PendingText);
                UnityEngine.GUI.color = prevGuiColor;
            }

            // 依赖信息行
            Text.Font = GameFont.Tiny;
            Rect depsRow = new Rect(
                rect.x + CARD_PADDING + CHECKBOX_SIZE + DEFAULT_SPACING + 4f + STATUS_ICON_WIDTH + DEFAULT_SPACING,
                mainRow.yMax + 2f,
                rect.width - CARD_PADDING * 2f - (CHECKBOX_SIZE + DEFAULT_SPACING + 4f + STATUS_ICON_WIDTH + DEFAULT_SPACING),
                DEPS_ROW_HEIGHT);
            UnityEngine.GUI.color = row.DepsColor;
            Widgets.Label(depsRow, row.DepsText);
            UnityEngine.GUI.color = prevGuiColor;
            Text.Font = prevFont;

            if (!string.IsNullOrEmpty(row.Tooltip))
            {
                TooltipHandler.TipRegion(rect, row.Tooltip);
            }
        }

        private static void DrawBottomBar(Rect rect, CachedLayout layout, IUiTheme theme)
        {
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Color prevColor = UnityEngine.GUI.color;

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width - HINT_WIDTH - DEFAULT_SPACING, 18f);
            Rect hintRect = new Rect(rect.x + rect.width - HINT_WIDTH, rect.y, HINT_WIDTH, 18f);
            Rect impactRect = new Rect(rect.x, rect.y + 20f, rect.width, 18f);

            bool hasWarnings = layout.Rows.Exists(r =>
                r.Result.State == ExtensionModuleState.Failed ||
                r.Result.State == ExtensionModuleState.DependencyDisabled);
            if (hasWarnings)
            {
                UnityEngine.GUI.color = theme?.GetColor("ext.logWarning") ?? new Color(1f, 0.6f, 0.3f);
            }
            Widgets.Label(summaryRect, layout.SummaryText);

            UnityEngine.GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(hintRect, layout.RestartHintText);
            Text.Anchor = TextAnchor.UpperLeft;

            if (!string.IsNullOrEmpty(layout.ImpactText))
            {
                UnityEngine.GUI.color = theme?.GetColor("ext.logWarning") ?? new Color(1f, 0.6f, 0.3f);
                Widgets.Label(impactRect, layout.ImpactText);
                TooltipHandler.TipRegion(impactRect, layout.ImpactText);
            }

            UnityEngine.GUI.color = prevColor;
            Text.Font = prevFont;
        }

        /// <summary>
        /// 主行列宽计算。构建期与绘制期必须使用同一公式，保证截断宽度与实际绘制宽度一致。
        /// </summary>
        private static void ComputeColumnWidths(float cardWidth, out float colId, out float colName)
        {
            float content = cardWidth - CARD_PADDING * 2f;
            float fixedPrefix = CHECKBOX_SIZE + DEFAULT_SPACING + 4f + STATUS_ICON_WIDTH + DEFAULT_SPACING;
            float fixedSuffix = COL_VERSION + DEFAULT_SPACING + COL_SOURCE + DEFAULT_SPACING + COL_STATE;
            // ID 与 Name 之间、Name 与 Version 之间的两个间距，加上待重启标记预留
            float interColumnGaps = DEFAULT_SPACING * 2f;
            float flex = Mathf.Max(0f, content - fixedPrefix - interColumnGaps - fixedSuffix - PENDING_WIDTH);
            colId = Mathf.Max(90f, flex * 0.55f);
            colName = Mathf.Max(60f, flex - colId);
        }

        /// <summary>
        /// 文本截断（构建期执行一次；Draw 路径不调用）。
        /// </summary>
        private static string Truncate(string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 8f)
            {
                return string.Empty;
            }

            if (Text.CalcSize(text).x <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "...";
            string candidate = text;
            while (candidate.Length > 1 && Text.CalcSize(candidate + ellipsis).x > maxWidth)
            {
                candidate = candidate.Substring(0, candidate.Length - 1);
            }
            return candidate + ellipsis;
        }

        private static Color GetLogColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.WARNING:
                    return new Color(1f, 0.75f, 0.3f);
                case LogLevel.ERROR:
                case LogLevel.FATAL:
                    return new Color(1f, 0.35f, 0.3f);
                case LogLevel.DEBUG:
                    return new Color(0.55f, 0.55f, 0.55f);
                default:
                    return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static int getStateSortOrder(ExtensionModuleState state)
        {
            switch (state)
            {
                case ExtensionModuleState.Failed: return 0;
                case ExtensionModuleState.DependencyDisabled: return 1;
                case ExtensionModuleState.Active: return 2;
                case ExtensionModuleState.Registered: return 3;
                case ExtensionModuleState.Discovered: return 4;
                case ExtensionModuleState.Disabled: return 5;
                case ExtensionModuleState.Shutdown: return 6;
                default: return 7;
            }
        }

        private static string GetStatusIcon(ExtensionModuleState state)
        {
            switch (state)
            {
                case ExtensionModuleState.Active: return "●";
                case ExtensionModuleState.Failed: return "✘";
                case ExtensionModuleState.Disabled: return "■";
                case ExtensionModuleState.DependencyDisabled: return "⚠";
                case ExtensionModuleState.Registered:
                case ExtensionModuleState.Discovered: return "◐";
                case ExtensionModuleState.Shutdown: return "□";
                default: return "?";
            }
        }

        private static Color GetStatusColor(ExtensionModuleState state, IUiTheme theme)
        {
            switch (state)
            {
                case ExtensionModuleState.Active:
                    return theme?.GetColor("ext.statusActive") ?? new Color(0.3f, 0.85f, 0.4f);
                case ExtensionModuleState.Failed:
                    return theme?.GetColor("ext.statusFailed") ?? new Color(1f, 0.35f, 0.3f);
                case ExtensionModuleState.Disabled:
                    return theme?.GetColor("ext.statusDisabled") ?? new Color(0.45f, 0.45f, 0.45f);
                case ExtensionModuleState.DependencyDisabled:
                    return theme?.GetColor("ext.statusDependencyDisabled") ?? new Color(0.95f, 0.75f, 0.25f);
                case ExtensionModuleState.Registered:
                    return theme?.GetColor("ext.statusRegistered") ?? new Color(0.8f, 0.8f, 0.35f);
                default:
                    return theme?.GetColor("ext.statusDefault") ?? new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private static string GetStateLabel(ExtensionModuleState state)
        {
            switch (state)
            {
                case ExtensionModuleState.Active:
                    return "Phinix_extensions_state_active".Translate();
                case ExtensionModuleState.Failed:
                    return "Phinix_extensions_state_failed".Translate();
                case ExtensionModuleState.Registered:
                    return "Phinix_extensions_state_registered".Translate();
                case ExtensionModuleState.Shutdown:
                    return "Phinix_extensions_state_shutdown".Translate();
                case ExtensionModuleState.Discovered:
                    return "Phinix_extensions_state_discovered".Translate();
                case ExtensionModuleState.Disabled:
                    return "Phinix_extensions_state_disabled".Translate();
                case ExtensionModuleState.DependencyDisabled:
                    return "Phinix_extensions_state_dependencyDisabled".Translate();
                default:
                    return "Phinix_extensions_state_unknown".Translate();
            }
        }

        private sealed class CachedLayout
        {
            public List<CachedRow> Rows;
            public string SummaryText;
            public string ImpactText;
            public string LoadedHeaderText;
            public string LogHeaderText;
            public string RestartHintText;
        }

        private sealed class CachedRow
        {
            public ExtensionDiscoveryResult Result;
            public ExtensionDisplayState Display;
            public string IdText;
            public string NameText;
            public string VersionText;
            public string SourceText;
            public string StateText;
            public string PendingText;
            public string DepsText;
            public string Tooltip;
            public Color StateColor;
            public Color PendingColor;
            public Color DepsColor;
            public bool CanToggle;
            public bool Checked;
            public string DepDisabledHint;
        }

        private sealed class CachedLogLine
        {
            public string Text;
            public string Tooltip;
            public Color Color;
        }
    }
}
