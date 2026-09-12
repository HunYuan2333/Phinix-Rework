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
    public sealed class ExtensionManagerTab : IMainTabProvider, IResponsiveMainTabProvider
    {
        private const float Spacing = 6f;
        private const float HeaderHeight = 24f;
        private const float LogLineHeight = 18f;
        private const float WideCardHeight = 62f;
        private const float CompactCardHeight = 94f;
        private const float CompactWidth = 720f;
        private static readonly UiLayoutHints Hints = new UiLayoutHints(
            new Vector2(320f, 260f), new Vector2(820f, 580f), true);

        private readonly List<CachedRow> rows = new List<CachedRow>();
        private readonly List<CachedLogLine> logLines = new List<CachedLogLine>();
        private Vector2 listScroll;
        private Vector2 logScroll;
        private IUiTheme theme;
        private int resultCount = -1;
        private int settingsVersion = -1;
        private int logVersion = -1;
        private int widthBucket = -1;
        private int logWidthBucket = -1;
        private object language;
        private object logLanguage;
        private string loadedHeader;
        private string logHeader;
        private string summary;
        private string impact;
        private string restartHint;
        private bool hasWarnings;

        public string TabLabel => "Phinix_tabs_extensions".Translate();
        public float TabOrder => 999f;
        public UiLayoutHints LayoutHints => Hints;

        public void Draw(Rect inRect)
        {
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);
            if (inRect.width <= 0f || inRect.height <= 0f) return;

            PhinixFrameworkClient framework = Client.Instance?.FrameworkClient;
            Settings settings = Client.Instance?.Settings;
            IReadOnlyList<ExtensionDiscoveryResult> results = framework?.ExtensionResults ??
                (IReadOnlyList<ExtensionDiscoveryResult>)Array.Empty<ExtensionDiscoveryResult>();
            ResolveTheme(framework);

            int currentWidthBucket = (int)(inRect.width / 24f);
            int currentSettingsVersion = settings?.SettingsVersion ?? 0;
            object currentLanguage = LanguageDatabase.activeLanguage;
            if (resultCount != results.Count || settingsVersion != currentSettingsVersion ||
                widthBucket != currentWidthBucket || !ReferenceEquals(language, currentLanguage))
            {
                RebuildRows(results, framework, settings, inRect.width);
                resultCount = results.Count;
                settingsVersion = currentSettingsVersion;
                widthBucket = currentWidthBucket;
                language = currentLanguage;
            }

            int currentLogVersion = framework?.ExtensionLogVersion ?? 0;
            if (logVersion != currentLogVersion || logWidthBucket != currentWidthBucket ||
                !ReferenceEquals(logLanguage, currentLanguage))
            {
                RebuildLogs(framework, inRect.width);
                logVersion = currentLogVersion;
                logWidthBucket = currentWidthBucket;
                logLanguage = currentLanguage;
            }

            float bottomHeight = inRect.width < 560f ? (string.IsNullOrEmpty(impact) ? 48f : 68f) : 42f;
            bottomHeight = Mathf.Min(bottomHeight, inRect.height);
            Rect bottom = new Rect(inRect.x, inRect.yMax - bottomHeight, inRect.width, bottomHeight);
            Rect body = new Rect(inRect.x, inRect.y, inRect.width,
                Mathf.Max(0f, inRect.height - bottomHeight - Spacing));
            float listHeight = Mathf.Max(0f, Mathf.Floor(body.height * 0.6f));
            Rect listSection = new Rect(body.x, body.y, body.width, listHeight);
            Rect logSection = new Rect(body.x, body.y + listHeight + Spacing, body.width,
                Mathf.Max(0f, body.height - listHeight - Spacing));

            DrawList(listSection);
            DrawLog(logSection);
            DrawBottom(bottom);
        }

        private void DrawList(Rect section)
        {
            if (section.height <= 0f) return;
            Widgets.Label(new Rect(section.x, section.y, section.width, Mathf.Min(HeaderHeight, section.height)), loadedHeader);
            Rect viewport = new Rect(section.x, section.y + HeaderHeight, section.width,
                Mathf.Max(0f, section.height - HeaderHeight));
            if (viewport.height <= 0f) return;
            float rowHeight = section.width < CompactWidth ? CompactCardHeight : WideCardHeight;
            float stride = rowHeight + Spacing;
            float contentHeight = rows.Count * stride;
            float contentWidth = Mathf.Max(0f, viewport.width - (contentHeight > viewport.height ? 16f : 0f));
            listScroll.y = Mathf.Clamp(listScroll.y, 0f, Mathf.Max(0f, contentHeight - viewport.height));
            Widgets.BeginScrollView(viewport, ref listScroll,
                new Rect(0f, 0f, contentWidth, Mathf.Max(contentHeight, viewport.height)));
            try
            {
                VirtualListRange range = VirtualListLayout.GetFixedRange(rows.Count, stride, listScroll.y, viewport.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                    DrawCard(new Rect(0f, i * stride, contentWidth, rowHeight), rows[i], contentWidth < CompactWidth);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawLog(Rect section)
        {
            if (section.height <= 0f) return;
            Widgets.Label(new Rect(section.x, section.y, section.width, Mathf.Min(HeaderHeight, section.height)), logHeader);
            Rect viewport = new Rect(section.x, section.y + HeaderHeight, section.width,
                Mathf.Max(0f, section.height - HeaderHeight));
            if (viewport.height <= 0f) return;
            float contentHeight = logLines.Count * LogLineHeight;
            float contentWidth = Mathf.Max(0f, viewport.width - (contentHeight > viewport.height ? 16f : 0f));
            logScroll.y = Mathf.Clamp(logScroll.y, 0f, Mathf.Max(0f, contentHeight - viewport.height));
            Widgets.BeginScrollView(viewport, ref logScroll,
                new Rect(0f, 0f, contentWidth, Mathf.Max(contentHeight, viewport.height)));
            GameFont oldFont = Text.Font;
            Color oldColor = UnityEngine.GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                VirtualListRange range = VirtualListLayout.GetFixedRange(logLines.Count, LogLineHeight, logScroll.y, viewport.height, 2);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    CachedLogLine line = logLines[i];
                    Rect rect = new Rect(0f, i * LogLineHeight, contentWidth, LogLineHeight);
                    UnityEngine.GUI.color = line.Color;
                    Widgets.Label(rect, line.Text);
                    TooltipHandler.TipRegion(rect, line.Tooltip);
                }
            }
            finally
            {
                UnityEngine.GUI.color = oldColor;
                Text.Font = oldFont;
                Widgets.EndScrollView();
            }
        }

        private void DrawCard(Rect rect, CachedRow row, bool compact)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.5f));
            Rect inner = rect.ContractedBy(6f);
            Color oldColor = UnityEngine.GUI.color;
            GameFont oldFont = Text.Font;
            try
            {
                float checkbox = Mathf.Min(18f, inner.height);
                bool enabled = row.Checked;
                if (row.CanToggle)
                {
                    Widgets.Checkbox(new Vector2(inner.x, inner.y + 4f), ref enabled, checkbox);
                    if (enabled != row.Checked && Client.Instance?.Settings != null)
                    {
                        Client.Instance.Settings.SetExtensionDisabled(row.Result.ExtensionId, !enabled);
                        Client.Instance.Settings.AcceptChanges();
                        Messages.Message((enabled ? "Phinix_extensions_toggleEnabled" :
                            "Phinix_extensions_toggleDisabled").Translate(row.Result.ExtensionId),
                            MessageTypeDefOf.NeutralEvent);
                    }
                }
                else
                {
                    Widgets.DrawBoxSolid(new Rect(inner.x, inner.y + 4f, checkbox, checkbox),
                        new Color(0.3f, 0.3f, 0.3f, 0.3f));
                }

                float textX = inner.x + checkbox + Spacing;
                float textWidth = Mathf.Max(0f, inner.xMax - textX);
                UnityEngine.GUI.color = row.StateColor;
                Widgets.Label(new Rect(textX, inner.y, 22f, 24f), row.StatusIcon);
                UnityEngine.GUI.color = oldColor;
                textX += 22f;

                if (compact)
                {
                    float stateWidth = Mathf.Min(110f, textWidth * 0.35f);
                    UnityEngine.GUI.color = new Color(0.8f, 0.85f, 1f);
                    Widgets.Label(new Rect(textX, inner.y, Mathf.Max(0f, inner.xMax - textX - stateWidth), 24f), row.ExtensionId);
                    UnityEngine.GUI.color = row.StateColor;
                    Widgets.Label(new Rect(inner.xMax - stateWidth, inner.y, stateWidth, 24f), row.StateText);
                    UnityEngine.GUI.color = oldColor;
                    Widgets.Label(new Rect(textX, inner.y + 25f, Mathf.Max(0f, inner.xMax - textX), 22f),
                        row.DisplayName + (string.IsNullOrEmpty(row.Version) ? "" : "  " + row.Version));
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(textX, inner.y + 48f, Mathf.Max(0f, inner.xMax - textX), 18f), row.Source);
                    UnityEngine.GUI.color = row.DependencyColor;
                    Widgets.Label(new Rect(textX, inner.y + 66f, Mathf.Max(0f, inner.xMax - textX), 18f), row.Dependencies);
                }
                else
                {
                    float available = Mathf.Max(0f, inner.xMax - textX);
                    float idWidth = available * 0.30f;
                    float nameWidth = available * 0.25f;
                    float versionWidth = available * 0.12f;
                    float sourceWidth = available * 0.18f;
                    float stateWidth = Mathf.Max(0f, available - idWidth - nameWidth - versionWidth - sourceWidth);
                    UnityEngine.GUI.color = new Color(0.8f, 0.85f, 1f);
                    Widgets.Label(new Rect(textX, inner.y, idWidth, 24f), row.ExtensionId);
                    UnityEngine.GUI.color = oldColor;
                    Widgets.Label(new Rect(textX + idWidth, inner.y, nameWidth, 24f), row.DisplayName);
                    Widgets.Label(new Rect(textX + idWidth + nameWidth, inner.y, versionWidth, 24f), row.Version);
                    Widgets.Label(new Rect(textX + idWidth + nameWidth + versionWidth, inner.y, sourceWidth, 24f), row.Source);
                    UnityEngine.GUI.color = row.StateColor;
                    Widgets.Label(new Rect(inner.xMax - stateWidth, inner.y, stateWidth, 24f), row.StateText);
                    Text.Font = GameFont.Tiny;
                    UnityEngine.GUI.color = row.DependencyColor;
                    Widgets.Label(new Rect(textX, inner.y + 27f, available, 18f), row.Dependencies);
                }
            }
            finally
            {
                UnityEngine.GUI.color = oldColor;
                Text.Font = oldFont;
            }
            TooltipHandler.TipRegion(rect, row.Tooltip);
        }

        private void DrawBottom(Rect rect)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = UnityEngine.GUI.color;
            bool oldWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                bool stacked = rect.width < 560f;
                Rect summaryRect = stacked
                    ? new Rect(rect.x, rect.y, rect.width, 18f)
                    : new Rect(rect.x, rect.y, rect.width * 0.65f, 18f);
                Rect hintRect = stacked
                    ? new Rect(rect.x, rect.y + 18f, rect.width, 18f)
                    : new Rect(summaryRect.xMax + Spacing, rect.y,
                        Mathf.Max(0f, rect.xMax - summaryRect.xMax - Spacing), 18f);
                if (hasWarnings) UnityEngine.GUI.color = theme?.GetColor("ext.logWarning") ?? new Color(1f, 0.6f, 0.3f);
                Widgets.Label(summaryRect, summary);
                TooltipHandler.TipRegion(summaryRect, summary);
                UnityEngine.GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(hintRect, restartHint);
                TooltipHandler.TipRegion(hintRect, restartHint);
                if (!string.IsNullOrEmpty(impact))
                {
                    UnityEngine.GUI.color = theme?.GetColor("ext.logWarning") ?? new Color(1f, 0.6f, 0.3f);
                    Rect impactRect = new Rect(rect.x, stacked ? rect.y + 38f : rect.y + 20f, rect.width, 18f);
                    Widgets.Label(impactRect, impact);
                    TooltipHandler.TipRegion(impactRect, impact);
                }
            }
            finally
            {
                UnityEngine.GUI.color = oldColor;
                Text.Font = oldFont;
                Text.WordWrap = oldWrap;
            }
        }

        private void ResolveTheme(PhinixFrameworkClient framework)
        {
            if (theme != null || framework == null) return;
            IReadOnlyList<IUiTheme> themes = framework.ResolveExtensionApis<IUiTheme>();
            if (themes != null && themes.Count > 0) theme = themes[0];
        }

        private void RebuildRows(IReadOnlyList<ExtensionDiscoveryResult> results,
            PhinixFrameworkClient framework, Settings settings, float width)
        {
            rows.Clear();
            ExtensionDependencyGraph graph = framework?.ExtensionDependencyGraph;
            IReadOnlyCollection<string> disabled = settings?.DisabledExtensions;
            List<ExtensionDiscoveryResult> sorted = new List<ExtensionDiscoveryResult>(results);
            sorted.Sort(CompareResults);
            int active = 0;
            int disabledCount = 0;
            int dependencyDisabled = 0;
            bool warning = false;
            int pending = 0;
            int undeclared = 0;
            StringBuilder impactBuilder = new StringBuilder(96);
            for (int i = 0; i < sorted.Count; i++)
            {
                ExtensionDiscoveryResult result = sorted[i];
                string id = result.ExtensionId ?? "?";
                ExtensionDisplayState display = ExtensionDisplayState.Compute(result, disabled, graph);
                string dependencies;
                IReadOnlyList<string> declared = result.DependsOn;
                bool missingDeclaration = graph != null && graph.IsUndeclared(id);
                if (declared != null && declared.Count > 0)
                    dependencies = "Phinix_extensions_dependencies".Translate() + ": " + string.Join(", ", declared);
                else if (missingDeclaration)
                    dependencies = "Phinix_extensions_undeclaredDeps".Translate();
                else
                    dependencies = "Phinix_extensions_dependencies".Translate() + ": " + "Phinix_extensions_none".Translate();

                string pendingText = "";
                if (display.PendingChange == ExtensionPendingChange.WillDisableAfterRestart)
                {
                    pendingText = "Phinix_extensions_pendingDisable".Translate();
                    pending++;
                }
                else if (display.PendingChange == ExtensionPendingChange.WillEnableAfterRestart)
                {
                    pendingText = "Phinix_extensions_pendingEnable".Translate();
                    pending++;
                }
                string tooltip = id + "\n" + (result.DisplayName ?? "") + "\n" +
                    (result.SourcePackageId ?? result.AssemblyName ?? "") + "\n" + dependencies;
                if (!string.IsNullOrEmpty(pendingText)) tooltip += "\n" + pendingText;
                if (!string.IsNullOrEmpty(result.StateDetail)) tooltip += "\n" + result.StateDetail;

                rows.Add(new CachedRow
                {
                    Result = result,
                    ExtensionId = id,
                    DisplayName = result.DisplayName ?? "",
                    Version = result.Version ?? "",
                    Source = result.SourcePackageId ?? result.AssemblyName ?? "",
                    StateText = GetStateLabel(display.RuntimeState) +
                        (string.IsNullOrEmpty(pendingText) ? "" : " · " + pendingText),
                    StatusIcon = GetStatusIcon(display.RuntimeState),
                    Dependencies = dependencies,
                    StateColor = GetStatusColor(display.RuntimeState, theme),
                    DependencyColor = missingDeclaration ? new Color(0.7f, 0.7f, 0.45f) : new Color(0.6f, 0.6f, 0.6f),
                    CanToggle = result.State != ExtensionModuleState.DependencyDisabled,
                    Checked = settings == null || !settings.IsExtensionDisabled(id),
                    Tooltip = tooltip
                });
                if (display.RuntimeState == ExtensionModuleState.Active) active++;
                if (display.EffectiveState == ExtensionModuleState.Disabled) disabledCount++;
                else if (display.EffectiveState == ExtensionModuleState.DependencyDisabled)
                {
                    dependencyDisabled++;
                    warning = true;
                }
                if (display.RuntimeState == ExtensionModuleState.Failed) warning = true;
                if (missingDeclaration) undeclared++;
                if (display.Reason == ExtensionDisplayReason.UserDisabled && graph != null)
                {
                    IReadOnlyList<string> dependents = graph.GetDependents(id);
                    if (dependents.Count > 0)
                    {
                        if (impactBuilder.Length > 0) impactBuilder.Append(" | ");
                        impactBuilder.Append("Phinix_extensions_disableImpact".Translate(id, string.Join(", ", dependents)));
                    }
                }
            }
            StringBuilder summaryBuilder = new StringBuilder(96);
            summaryBuilder.Append("Phinix_extensions_summaryActiveTotal".Translate(active, results.Count));
            if (disabledCount > 0) summaryBuilder.Append(" | ").Append("Phinix_extensions_summaryDisabled".Translate(disabledCount));
            if (dependencyDisabled > 0) summaryBuilder.Append(" | ").Append("Phinix_extensions_summaryDepDisabled".Translate(dependencyDisabled));
            if (undeclared > 0) summaryBuilder.Append(" | ").Append("Phinix_extensions_summaryUndeclared".Translate(undeclared));
            if (pending > 0) summaryBuilder.Append(" | ").Append("Phinix_extensions_summaryPending".Translate(pending));
            summary = summaryBuilder.ToString();
            impact = impactBuilder.Length == 0 ? null : impactBuilder.ToString();
            loadedHeader = "Phinix_extensions_loadedExtensions".Translate(results.Count);
            logHeader = "Phinix_extensions_loadingLog".Translate();
            restartHint = "Phinix_modSettings_extensionsRestartRequired".Translate();
            hasWarnings = warning;
        }

        private void RebuildLogs(PhinixFrameworkClient framework, float width)
        {
            logLines.Clear();
            if (framework != null)
            {
                IReadOnlyList<FrameworkLogEntry> snapshot = framework.GetExtensionLogSnapshot();
                GameFont oldFont = Text.Font;
                Text.Font = GameFont.Tiny;
                try
                {
                    float available = Mathf.Max(1f, width - 16f);
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        FrameworkLogEntry entry = snapshot[i];
                        string time = new DateTime(entry.TimestampUtcTicks, DateTimeKind.Utc)
                            .ToLocalTime().ToString("HH:mm:ss");
                        string full = "[" + time + "] " + entry.Message;
                        logLines.Add(new CachedLogLine
                        {
                            Text = Truncate(full, available),
                            Tooltip = full,
                            Color = GetLogColor(entry.Level)
                        });
                    }
                }
                finally { Text.Font = oldFont; }
            }
            if (logLines.Count == 0)
            {
                string empty = "Phinix_extensions_logEmpty".Translate();
                logLines.Add(new CachedLogLine { Text = empty, Tooltip = empty, Color = new Color(0.55f, 0.55f, 0.55f) });
            }
        }

        private static int CompareResults(ExtensionDiscoveryResult left, ExtensionDiscoveryResult right)
        {
            int state = GetStateOrder(left.State).CompareTo(GetStateOrder(right.State));
            return state != 0 ? state : string.Compare(left.ExtensionId, right.ExtensionId, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetStateOrder(ExtensionModuleState state)
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

        private static string Truncate(string value, float width)
        {
            if (string.IsNullOrEmpty(value) || width <= 8f) return "";
            if (Text.CalcSize(value).x <= width) return value;
            const string ellipsis = "...";
            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int middle = low + ((high - low + 1) >> 1);
                if (Text.CalcSize(value.Substring(0, middle) + ellipsis).x <= width) low = middle;
                else high = middle - 1;
            }
            return value.Substring(0, low) + ellipsis;
        }

        private static Color GetLogColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.WARNING: return new Color(1f, 0.75f, 0.3f);
                case LogLevel.ERROR:
                case LogLevel.FATAL: return new Color(1f, 0.35f, 0.3f);
                case LogLevel.DEBUG: return new Color(0.55f, 0.55f, 0.55f);
                default: return new Color(0.7f, 0.7f, 0.7f);
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
                case ExtensionModuleState.Active: return theme?.GetColor("ext.statusActive") ?? new Color(0.3f, 0.85f, 0.4f);
                case ExtensionModuleState.Failed: return theme?.GetColor("ext.statusFailed") ?? new Color(1f, 0.35f, 0.3f);
                case ExtensionModuleState.Disabled: return theme?.GetColor("ext.statusDisabled") ?? new Color(0.45f, 0.45f, 0.45f);
                case ExtensionModuleState.DependencyDisabled: return theme?.GetColor("ext.statusDependencyDisabled") ?? new Color(0.95f, 0.75f, 0.25f);
                case ExtensionModuleState.Registered: return theme?.GetColor("ext.statusRegistered") ?? new Color(0.8f, 0.8f, 0.35f);
                default: return theme?.GetColor("ext.statusDefault") ?? new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private static string GetStateLabel(ExtensionModuleState state)
        {
            switch (state)
            {
                case ExtensionModuleState.Active: return "Phinix_extensions_state_active".Translate();
                case ExtensionModuleState.Failed: return "Phinix_extensions_state_failed".Translate();
                case ExtensionModuleState.Registered: return "Phinix_extensions_state_registered".Translate();
                case ExtensionModuleState.Shutdown: return "Phinix_extensions_state_shutdown".Translate();
                case ExtensionModuleState.Discovered: return "Phinix_extensions_state_discovered".Translate();
                case ExtensionModuleState.Disabled: return "Phinix_extensions_state_disabled".Translate();
                case ExtensionModuleState.DependencyDisabled: return "Phinix_extensions_state_dependencyDisabled".Translate();
                default: return "Phinix_extensions_state_unknown".Translate();
            }
        }

        private sealed class CachedRow
        {
            public ExtensionDiscoveryResult Result;
            public string ExtensionId;
            public string DisplayName;
            public string Version;
            public string Source;
            public string StateText;
            public string StatusIcon;
            public string Dependencies;
            public string Tooltip;
            public Color StateColor;
            public Color DependencyColor;
            public bool CanToggle;
            public bool Checked;
        }

        private sealed class CachedLogLine
        {
            public string Text;
            public string Tooltip;
            public Color Color;
        }
    }
}
