using System;
using System.Collections.Generic;
using Utils.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient.Framework
{
    /// <summary>
    /// host 核心提供的扩展管理设置面板（不是插件）。Order=50，排在插件面板（100+）之前。
    /// 用户可在此勾选启用/禁用任意扩展。v1 重启生效。
    ///
    /// 设计哲学 §1.1 插件平权：全部扩展可禁用，包括官方 Chat/Trade/LegacyAdapter。
    /// 设计哲学 §1.3 host 只做通用服务：扩展生命周期管理是通用服务。
    /// 设计哲学 §2.3 减少硬编码：禁用列表来自用户设置，动态读取。
    /// 与 ExtensionManagerTab 共用 <see cref="ExtensionDisplayState"/> 静态计算，
    /// 两处 UI 对同一设置快照的展示结果始终一致，无需刷新动作。
    /// </summary>
    internal sealed class ExtensionControlSettingsPanelProvider : IClientSettingsPanelProvider
    {
        // 布局缓存（设计哲学 §8.3）：设置面板每帧重绘，排序、文本拼接、颜色标记只在
        // 扩展结果数量或设置版本变化时重建一次，Draw 路径零分配。
        private List<ExtensionDiscoveryResult> cachedSortedResults;
        private string[] cachedLabels;
        private string[] cachedHints;
        private int cachedResultsCount = -1;
        private int cachedSettingsVersion = -1;

        public string SectionId => "Phinix_modSettings_extensionsSectionTitle";

        public float Order => 50f;

        public bool IsVisible(IClientSettingsContext settings)
        {
            return Client.Instance?.FrameworkClient?.ExtensionResults?.Count > 0;
        }

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            PhinixFrameworkClient frameworkClient = Client.Instance?.FrameworkClient;
            if (frameworkClient == null)
            {
                return;
            }

            IReadOnlyList<ExtensionDiscoveryResult> results = frameworkClient.ExtensionResults;
            ExtensionDependencyGraph dependencyGraph = frameworkClient.ExtensionDependencyGraph;
            Settings hostSettings = Client.Instance?.Settings;
            int resultsCount = results?.Count ?? 0;
            int settingsVersion = hostSettings?.SettingsVersion ?? 0;
            if (cachedSortedResults == null ||
                cachedResultsCount != resultsCount ||
                cachedSettingsVersion != settingsVersion)
            {
                RebuildCache(results, dependencyGraph, hostSettings);
                cachedResultsCount = resultsCount;
                cachedSettingsVersion = settingsVersion;
            }

            if (cachedSortedResults == null)
            {
                return;
            }

            for (int i = 0; i < cachedSortedResults.Count; i++)
            {
                ExtensionDiscoveryResult result = cachedSortedResults[i];
                string extensionId = result.ExtensionId ?? result.DisplayName ?? "?";

                // 复选框语义：勾选 = 启用（不在禁用列表中）。运行时依赖禁用状态不可单独启用。
                bool canToggle = result.State != ExtensionModuleState.DependencyDisabled;
                bool isEnabled = hostSettings == null || !hostSettings.IsExtensionDisabled(extensionId);
                bool newEnabled = isEnabled;

                if (canToggle)
                {
                    listing.CheckboxLabeled(cachedLabels[i], ref newEnabled);
                    if (newEnabled != isEnabled && hostSettings != null)
                    {
                        hostSettings.SetExtensionDisabled(extensionId, !newEnabled);
                        hostSettings.AcceptChanges();
                    }
                }
                else
                {
                    // 依赖被禁用：不可单独启用，不渲染可交互复选框
                    listing.Label(cachedLabels[i]);
                }

                if (cachedHints[i] != null)
                {
                    listing.Label(cachedHints[i]);
                }
            }

            listing.Gap(4f);
            listing.Label("Phinix_modSettings_extensionsRestartRequired".Translate().Colorize(Color.gray));
        }

        /// <summary>
        /// 缓存重建：排序、标签拼接、待重启提示与依赖警告文本一次性生成。
        /// 仅当扩展结果数量或设置版本变化时调用（§8.3 布局缓存）。
        /// </summary>
        private void RebuildCache(
            IReadOnlyList<ExtensionDiscoveryResult> results,
            ExtensionDependencyGraph dependencyGraph,
            Settings hostSettings)
        {
            IReadOnlyCollection<string> disabledIds = hostSettings?.DisabledExtensions;

            cachedSortedResults = new List<ExtensionDiscoveryResult>(results);
            cachedSortedResults.Sort((a, b) => string.Compare(a.ExtensionId, b.ExtensionId, StringComparison.OrdinalIgnoreCase));

            int count = cachedSortedResults.Count;
            cachedLabels = new string[count];
            cachedHints = new string[count];

            for (int i = 0; i < count; i++)
            {
                ExtensionDiscoveryResult result = cachedSortedResults[i];
                string extensionId = result.ExtensionId ?? result.DisplayName ?? "?";
                cachedLabels[i] = string.IsNullOrEmpty(result.DisplayName)
                    ? extensionId
                    : $"{result.DisplayName} ({extensionId})";

                ExtensionDisplayState display = ExtensionDisplayState.Compute(result, disabledIds, dependencyGraph);

                if (display.PendingChange == ExtensionPendingChange.WillDisableAfterRestart)
                {
                    cachedHints[i] = ("  " + "Phinix_extensions_pendingDisable".Translate()).Colorize(Color.yellow);
                }
                else if (display.PendingChange == ExtensionPendingChange.WillEnableAfterRestart)
                {
                    cachedHints[i] = ("  " + "Phinix_extensions_pendingEnable".Translate()).Colorize(new Color(0.55f, 0.75f, 1f));
                }
                else if (display.Reason == ExtensionDisplayReason.DependencyDisabled)
                {
                    string detail = result.StateDetail;
                    if (string.IsNullOrEmpty(detail))
                    {
                        detail = "Phinix_extensions_depDisabledHint".Translate();
                    }
                    cachedHints[i] = ("  ⚠ " + detail).Colorize(Color.yellow);
                }

                // 老插件未声明依赖关系提示
                if (dependencyGraph != null && dependencyGraph.IsUndeclared(extensionId))
                {
                    string undeclaredHint = ("  " + "Phinix_extensions_undeclaredDeps".Translate()).Colorize(Color.gray);
                    cachedHints[i] = cachedHints[i] == null
                        ? undeclaredHint
                        : cachedHints[i] + "\n" + undeclaredHint;
                }
            }
        }
    }
}
