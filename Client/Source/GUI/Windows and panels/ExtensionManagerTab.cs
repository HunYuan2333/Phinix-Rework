using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Utils.Framework;
using Verse;

namespace PhinixClient
{
    public class ExtensionManagerTab : IMainTabProvider
    {
        private const float ROW_HEIGHT = 24f;
        private const float SECTION_HEADER_HEIGHT = 26f;
        private const float BUTTON_WIDTH = 60f;
        private const float BUTTON_HEIGHT = 22f;
        private const float DEFAULT_SPACING = 6f;
        private const float STATUS_ICON_WIDTH = 20f;
        private const float BOTTOM_BUTTON_WIDTH = 160f;
        private const float BOTTOM_BUTTON_HEIGHT = 28f;
        private const float SPLIT_RATIO = 0.55f;

        private Vector2 listScrollPosition;
        private Vector2 logScrollPosition;

        public string TabLabel => "Phinix_tabs_extensions".Translate();
        public float TabOrder => 999;

        private IUiTheme Theme => Client.Instance?.FrameworkClient?.ResolveExtensionApis<IUiTheme>() is IReadOnlyList<IUiTheme> themes && themes.Count > 0
            ? themes[0]
            : null;

        public void Draw(Rect inRect)
        {
            IUiTheme theme = Theme;

            IReadOnlyList<ExtensionDiscoveryResult> results =
                Client.Instance.FrameworkClient?.ExtensionResults ??
                new List<ExtensionDiscoveryResult>().AsReadOnly();
            IReadOnlyList<string> diagnostics =
                Client.Instance.FrameworkClient?.ExtensionDiagnostics ??
                new List<string>().AsReadOnly();
            IReadOnlyList<string> warnings =
                Client.Instance.FrameworkClient?.ExtensionWarnings ??
                new List<string>().AsReadOnly();

            Rect listSectionRect = inRect.TopPartPixels(inRect.height * SPLIT_RATIO);
            Rect logSectionRect = inRect.BottomPartPixels(inRect.height * (1f - SPLIT_RATIO));
            logSectionRect.yMin += DEFAULT_SPACING;

            // ---- Extension List Section ----
            float listHeaderY = listSectionRect.y;
            Widgets.Label(
                new Rect(listSectionRect.x, listHeaderY, listSectionRect.width, SECTION_HEADER_HEIGHT),
                "Phinix_extensions_loadedExtensions".Translate(results.Count));

            Rect listHeaderRect = new Rect(listSectionRect.x, listHeaderY + SECTION_HEADER_HEIGHT,
                listSectionRect.width, ROW_HEIGHT);
            DrawListHeader(listHeaderRect, theme);

            // Column widths (match header layout)
            float colStatus = STATUS_ICON_WIDTH;
            float colId = 150f;
            float colDisplayName = 130f;
            float colVersion = 60f;
            float colSource = 100f;
            float colState = 70f;
            float colButton = BUTTON_WIDTH + DEFAULT_SPACING;

            float listTop = listHeaderRect.yMax + 2f;
            float listHeight = listSectionRect.yMax - listTop;
            Rect listOuterRect = new Rect(listSectionRect.x, listTop, listSectionRect.width, listHeight);

            float listInnerHeight = results.Count * ROW_HEIGHT;
            Rect listInnerRect = new Rect(0f, 0f, listSectionRect.width - 16f, listInnerHeight);

            Widgets.BeginScrollView(listOuterRect, ref listScrollPosition, listInnerRect);

            float currentY = 0f;
            // 扩展列表启动后不变，OrderBy 结果缓存到本地避免每帧分配
            List<ExtensionDiscoveryResult> sortedResults = new List<ExtensionDiscoveryResult>(results);
            sortedResults.Sort((a, b) => string.Compare(a.ExtensionId, b.ExtensionId, System.StringComparison.OrdinalIgnoreCase));
            foreach (ExtensionDiscoveryResult result in sortedResults)
            {
                Rect rowRect = new Rect(0f, currentY, listInnerRect.width, ROW_HEIGHT);

                if (currentY % (ROW_HEIGHT * 2) < ROW_HEIGHT)
                {
                    Widgets.DrawAltRect(rowRect);
                }

                DrawExtensionRow(rowRect, result, colStatus, colId, colDisplayName,
                    colVersion, colSource, colState, colButton, theme);

                currentY += ROW_HEIGHT;
            }

            Widgets.EndScrollView();

            // ---- Log Section ----
            float logHeaderY = logSectionRect.y;
            Widgets.Label(
                new Rect(logSectionRect.x, logHeaderY, logSectionRect.width, SECTION_HEADER_HEIGHT),
                "Phinix_extensions_loadingLog".Translate());

            float logContentY = logHeaderY + SECTION_HEADER_HEIGHT;
            float logContentHeight = logSectionRect.yMax - logContentY;
            Rect logOuterRect = new Rect(logSectionRect.x, logContentY, logSectionRect.width, logContentHeight);

            int totalLogEntries = diagnostics.Count + warnings.Count;
            float logInnerHeight = totalLogEntries * (ROW_HEIGHT - 4f);
            Rect logInnerRect = new Rect(0f, 0f, logSectionRect.width - 16f, Mathf.Max(logInnerHeight, logContentHeight));

            Widgets.BeginScrollView(logOuterRect, ref logScrollPosition, logInnerRect);

            float logY = 0f;
            GameFont prevFont = Text.Font;

            Color logDiagnosticColor = theme?.GetColor("ext.logDiagnostic") ?? Color.gray;
            Color logWarningColor = theme?.GetColor("ext.logWarning") ?? Color.red;

            foreach (string diagnostic in diagnostics)
            {
                Text.Font = GameFont.Tiny;
                Color prevColor = UnityEngine.GUI.color;
                UnityEngine.GUI.color = logDiagnosticColor;
                Widgets.Label(new Rect(0f, logY, logInnerRect.width, ROW_HEIGHT - 4f), diagnostic);
                UnityEngine.GUI.color = prevColor;
                logY += ROW_HEIGHT - 4f;
            }

            foreach (string warning in warnings)
            {
                Text.Font = GameFont.Tiny;
                Color prevColor = UnityEngine.GUI.color;
                UnityEngine.GUI.color = logWarningColor;
                Widgets.Label(new Rect(0f, logY, logInnerRect.width, ROW_HEIGHT - 4f), warning);
                UnityEngine.GUI.color = prevColor;
                logY += ROW_HEIGHT - 4f;
            }

            Text.Font = prevFont;
            Widgets.EndScrollView();

            // ---- Bottom Buttons ----
            Rect bottomBarRect = new Rect(inRect.x, inRect.yMax - BOTTOM_BUTTON_HEIGHT,
                inRect.width, BOTTOM_BUTTON_HEIGHT);
            DrawBottomButtons(bottomBarRect, results, diagnostics, warnings, theme);
        }

        private static void DrawListHeader(Rect rect, IUiTheme theme)
        {
            float x = rect.x;
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Color prevColor = UnityEngine.GUI.color;
            UnityEngine.GUI.color = theme?.GetColor("ext.headerText") ?? Color.gray;

            Widgets.Label(new Rect(x, rect.y, STATUS_ICON_WIDTH, rect.height), "");
            x += STATUS_ICON_WIDTH + DEFAULT_SPACING;
            Widgets.Label(new Rect(x, rect.y, 150f, rect.height), "ID");
            x += 150f + DEFAULT_SPACING;
            Widgets.Label(new Rect(x, rect.y, 130f, rect.height), "Name");
            x += 130f + DEFAULT_SPACING;
            Widgets.Label(new Rect(x, rect.y, 60f, rect.height), "Version");
            x += 60f + DEFAULT_SPACING;
            Widgets.Label(new Rect(x, rect.y, 100f, rect.height), "Source");
            x += 100f + DEFAULT_SPACING;
            Widgets.Label(new Rect(x, rect.y, 70f, rect.height), "State");

            UnityEngine.GUI.color = prevColor;
            Text.Font = prevFont;
        }

        private static void DrawExtensionRow(Rect rect, ExtensionDiscoveryResult result,
            float colStatus, float colId, float colDisplayName, float colVersion,
            float colSource, float colState, float colButton, IUiTheme theme)
        {
            float x = rect.x;
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;

            // Status icon
            string statusIcon = GetStatusIcon(result.State);
            Color statusColor = GetStatusColor(result.State, theme);
            Color prevColor = UnityEngine.GUI.color;
            UnityEngine.GUI.color = statusColor;
            Widgets.Label(new Rect(x, rect.y, colStatus, rect.height), statusIcon);
            UnityEngine.GUI.color = prevColor;
            x += colStatus + DEFAULT_SPACING;

            // Extension ID
            Widgets.Label(new Rect(x, rect.y, colId, rect.height),
                result.ExtensionId ?? "?");
            x += colId + DEFAULT_SPACING;

            // Display Name
            Widgets.Label(new Rect(x, rect.y, colDisplayName, rect.height),
                result.DisplayName ?? "");
            x += colDisplayName + DEFAULT_SPACING;

            // Version
            Widgets.Label(new Rect(x, rect.y, colVersion, rect.height),
                result.Version ?? "");
            x += colVersion + DEFAULT_SPACING;

            // Source Package
            string sourceLabel = !string.IsNullOrEmpty(result.SourcePackageId)
                ? result.SourcePackageId
                : (result.AssemblyName ?? "");
            Widgets.Label(new Rect(x, rect.y, colSource, rect.height), sourceLabel);
            x += colSource + DEFAULT_SPACING;

            // State
            string stateLabel = GetStateLabel(result.State);
            Widgets.Label(new Rect(x, rect.y, colState, rect.height), stateLabel);

            // Tooltip with StateDetail on hover
            if (!string.IsNullOrEmpty(result.StateDetail))
            {
                Rect fullRowRect = new Rect(rect.x, rect.y,
                    colStatus + colId + colDisplayName + colVersion + colSource + colState
                    + DEFAULT_SPACING * 5, rect.height);
                TooltipHandler.TipRegion(fullRowRect, result.StateDetail);
            }

            Text.Font = prevFont;
        }

        private static void DrawBottomButtons(Rect rect,
            IReadOnlyList<ExtensionDiscoveryResult> results,
            IReadOnlyList<string> diagnostics,
            IReadOnlyList<string> warnings,
            IUiTheme theme)
        {
            // Bottom bar: show summary + refresh button on the right
            string summaryText;
            int warningCount = 0;
            int activeCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].State == ExtensionModuleState.Active) activeCount++;
                else if (results[i].State == ExtensionModuleState.Failed) warningCount++;
            }

            if (warningCount > 0)
            {
                summaryText = $"{activeCount} active, {warningCount} with errors";
                UnityEngine.GUI.color = theme?.GetColor("ext.logWarning") ?? Color.red;
            }
            else
            {
                summaryText = $"{activeCount} active, {results.Count} total";
            }

            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width - BOTTOM_BUTTON_WIDTH - DEFAULT_SPACING, rect.height),
                summaryText);
            UnityEngine.GUI.color = theme?.GetColor("ext.summaryText") ?? Color.white;
            Text.Font = prevFont;

            // Refresh button
            Rect refreshButtonRect = new Rect(
                rect.xMax - BOTTOM_BUTTON_WIDTH, rect.y, BOTTOM_BUTTON_WIDTH, BOTTOM_BUTTON_HEIGHT);
            if (Widgets.ButtonText(refreshButtonRect, "Phinix_extensions_refresh".Translate()))
            {
                // Refresh is a no-op for now — extension discovery happens at startup.
                // In the future this could trigger a re-scan.
                Messages.Message("Extension refresh is not yet supported. Restart RimWorld to reload extensions.",
                    MessageTypeDefOf.RejectInput);
            }
        }

        private static string GetStatusIcon(ExtensionModuleState state)
        {
            switch (state)
            {
                case ExtensionModuleState.Active:
                    return "✔"; // ✓
                case ExtensionModuleState.Failed:
                    return "✘"; // ✘
                case ExtensionModuleState.Registered:
                case ExtensionModuleState.Discovered:
                    return "○"; // ○
                case ExtensionModuleState.Shutdown:
                    return "■"; // ■
                default:
                    return "?";
            }
        }

        /// <summary>
        /// 从主题读取状态颜色，缺省回退到旧硬编码值以保持向后兼容。
        /// </summary>
        private static Color GetStatusColor(ExtensionModuleState state, IUiTheme theme)
        {
            switch (state)
            {
                case ExtensionModuleState.Active:
                    return theme?.GetColor("ext.statusActive") ?? new Color(0.3f, 0.8f, 0.3f);
                case ExtensionModuleState.Failed:
                    return theme?.GetColor("ext.statusFailed") ?? Color.red;
                case ExtensionModuleState.Registered:
                    return theme?.GetColor("ext.statusRegistered") ?? new Color(0.8f, 0.8f, 0.3f);
                default:
                    return theme?.GetColor("ext.statusDefault") ?? Color.gray;
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
                default:
                    return "Phinix_extensions_state_unknown".Translate();
            }
        }
    }
}
