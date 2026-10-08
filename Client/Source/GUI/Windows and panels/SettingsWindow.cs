using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    internal sealed class SettingsWindow : Window
    {
        private static readonly Regex ServerPortRegex = new Regex("^[0-9]{0,5}$", RegexOptions.Compiled);
        private const float Spacing = 8f;
        private const float RowHeight = 30f;
        private const float SectionDividerHeight = 1f;
        private static string serverAddress = Client.Instance.Settings.ServerAddress;
        private static string serverPortString = Client.Instance.Settings.ServerPort.ToString();
        private Vector2 scrollPosition;
        private float cachedWidth = -1f;
        private object cachedLanguage;
        private string cachedConnectedAddress;
        private string connectedText;
        private string cachedPreviewName;
        private string previewText;
        private float connectedHeight;
        private float connectionHeight;
        private float displayNameHeight;
        private readonly List<IClientQuickSettingsPanelProvider> quickSettingsPanels =
            new List<IClientQuickSettingsPanelProvider>();
        private string addressLabel;
        private string portLabel;
        private string connectLabel;
        private string connectingLabel;
        private string connectionFailedLabel;
        private string disconnectLabel;
        private string setNameLabel;
        private string displayNameLabel;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safe = UiScreenSafeArea.Current;
                return new Vector2(Mathf.Min(620f, safe.width), Mathf.Min(500f, safe.height));
            }
        }

        public SettingsWindow()
        {
            doCloseX = true;
            doCloseButton = false;
            doWindowBackground = true;
            draggable = true;
            resizeable = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            serverAddress = Client.Instance.Settings.ServerAddress;
            serverPortString = Client.Instance.Settings.ServerPort.ToString();
            RefreshQuickSettingsPanels();
            cachedWidth = -1f;
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

        public override void DoWindowContents(Rect inRect)
        {
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            RebuildLayoutIfNeeded(Mathf.Max(1f, inRect.width - 16f));
            bool connected = Client.Instance.Connected;
            bool online = Client.Instance.Online;
            bool showConnectionStatus = !connected &&
                (Client.Instance.Connecting || Client.Instance.ConnectionAttemptFailed);
            float formHeight = connectionHeight + (showConnectionStatus ? Spacing + RowHeight : 0f);
            float baseHeight = (connected ? connectedHeight : formHeight) +
                (online ? Spacing + displayNameHeight : 0f);
            float contentHeight = baseHeight + GetQuickSettingsHeight(Mathf.Max(1f, inRect.width - 16f));
            float contentWidth = contentHeight > inRect.height ? Mathf.Max(0f, inRect.width - 16f) : inRect.width;
            contentHeight = baseHeight + GetQuickSettingsHeight(Mathf.Max(1f, contentWidth));
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, contentHeight - inRect.height));
            Rect viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(contentHeight, inRect.height));
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            try
            {
                float y = 0f;
                if (connected) y += DrawConnected(new Rect(0f, y, contentWidth, connectedHeight));
                else y += DrawConnectionForm(new Rect(0f, y, contentWidth, formHeight));
                if (online)
                {
                    y += Spacing;
                    DrawDisplayName(new Rect(0f, y, contentWidth, displayNameHeight));
                    y += displayNameHeight;
                }
                DrawQuickSettings(contentWidth, ref y);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void RefreshQuickSettingsPanels()
        {
            quickSettingsPanels.Clear();
            PhinixFrameworkClient framework = Client.Instance.FrameworkClient;
            if (framework == null) return;
            IReadOnlyList<IClientQuickSettingsPanelProvider> providers =
                framework.ResolveExtensionApis<IClientQuickSettingsPanelProvider>();
            for (int i = 0; i < providers.Count; i++) quickSettingsPanels.Add(providers[i]);
            quickSettingsPanels.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : string.Compare(left.SectionId, right.SectionId, StringComparison.Ordinal);
            });
        }

        private float GetQuickSettingsHeight(float width)
        {
            IClientSettingsContext settings = Client.Instance.SettingsContext;
            float height = 0f;
            bool hasVisiblePanel = false;
            for (int i = 0; i < quickSettingsPanels.Count; i++)
            {
                IClientQuickSettingsPanelProvider panel = quickSettingsPanels[i];
                try
                {
                    if (!panel.IsVisible(settings)) continue;
                    float panelHeight = Mathf.Max(0f, panel.GetQuickSettingsHeight(width));
                    if (panelHeight <= 0f) continue;
                    if (!hasVisiblePanel)
                    {
                        height += Spacing + SectionDividerHeight + Spacing;
                        hasVisiblePanel = true;
                    }
                    else height += Spacing;
                    height += panelHeight;
                }
                catch (Exception ex)
                {
                    Log.Error($"[Phinix] Quick settings panel '{panel.SectionId}' failed to measure: {ex}");
                }
            }
            return height;
        }

        private void DrawQuickSettings(float width, ref float y)
        {
            IClientSettingsContext settings = Client.Instance.SettingsContext;
            bool drewPanel = false;
            for (int i = 0; i < quickSettingsPanels.Count; i++)
            {
                IClientQuickSettingsPanelProvider panel = quickSettingsPanels[i];
                try
                {
                    if (!panel.IsVisible(settings)) continue;
                    float panelHeight = Mathf.Max(0f, panel.GetQuickSettingsHeight(width));
                    if (panelHeight <= 0f) continue;
                    if (!drewPanel)
                    {
                        y += Spacing;
                        Widgets.DrawLineHorizontal(0f, y, width);
                        y += SectionDividerHeight + Spacing;
                        drewPanel = true;
                    }
                    else y += Spacing;
                    Rect panelRect = new Rect(0f, y, width, panelHeight);
                    y += panelHeight;
                    panel.DrawQuickSettings(panelRect, settings);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Phinix] Quick settings panel '{panel.SectionId}' failed to draw: {ex}");
                }
            }
        }

        private float DrawConnected(Rect rect)
        {
            float buttonWidth = Mathf.Min(rect.width, Mathf.Max(100f, Text.CalcSize(disconnectLabel).x + 24f));
            bool stacked = rect.width < 280f;
            Rect labelRect = stacked
                ? new Rect(rect.x, rect.y, rect.width, RowHeight)
                : new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - buttonWidth - Spacing), RowHeight);
            Rect buttonRect = stacked
                ? new Rect(rect.x, rect.y + RowHeight + Spacing, rect.width, RowHeight)
                : new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, RowHeight);
            if (!string.Equals(cachedConnectedAddress, serverAddress, StringComparison.Ordinal))
            {
                cachedConnectedAddress = serverAddress;
                connectedText = "Phinix_settings_connectedToLabel".Translate(serverAddress);
            }
            Widgets.Label(labelRect, connectedText);
            TooltipHandler.TipRegion(labelRect, connectedText);
            if (Widgets.ButtonText(buttonRect, disconnectLabel)) Client.Instance.Disconnect();
            return rect.height;
        }

        private float DrawConnectionForm(Rect rect)
        {
            float labelWidth = Mathf.Min(120f, rect.width * 0.28f);
            ResponsiveFormResult address = ResponsiveFormLayout.Calculate(rect, labelWidth, 140f, 0f, RowHeight, Spacing, 0f);
            Widgets.Label(address.LabelRect, addressLabel);
            serverAddress = Widgets.TextField(address.InputRect, serverAddress);

            Rect portBase = new Rect(rect.x, rect.y + address.Height + Spacing, rect.width,
                Mathf.Max(0f, rect.height - address.Height - Spacing));
            float connectWidth = Mathf.Min(portBase.width, Mathf.Max(100f, Text.CalcSize(connectLabel).x + 24f));
            ResponsiveFormResult port = ResponsiveFormLayout.Calculate(portBase, labelWidth, 70f, connectWidth, RowHeight, Spacing, 0f);
            Widgets.Label(port.LabelRect, portLabel);
            string candidate = Widgets.TextField(port.InputRect, serverPortString);
            if (ServerPortRegex.IsMatch(candidate)) serverPortString = candidate;
            bool valid = int.TryParse(serverPortString, out int portValue) && portValue > 0 &&
                portValue <= 65535 && !string.IsNullOrWhiteSpace(serverAddress);
            bool connecting = Client.Instance.Connecting;
            if (Widgets.ButtonText(port.ActionRect, connectLabel, active: valid && !connecting) && valid && !connecting)
            {
                string addressValue = serverAddress;
                Client.Instance.Settings.ServerAddress = addressValue;
                Client.Instance.Settings.ServerPort = portValue;
                Client.Instance.Settings.AcceptChanges();
                Client.Instance.QueueConnect(addressValue, portValue);
            }
            if (Client.Instance.Connecting || Client.Instance.ConnectionAttemptFailed)
            {
                string status = Client.Instance.Connecting ? connectingLabel : connectionFailedLabel;
                Widgets.Label(new Rect(rect.x, portBase.y + port.Height + Spacing, rect.width, RowHeight), status);
            }
            return rect.height;
        }

        private void DrawDisplayName(Rect rect)
        {
            float buttonWidth = Mathf.Min(rect.width, Mathf.Max(100f, Text.CalcSize(setNameLabel).x + 24f));
            ResponsiveFormResult form = ResponsiveFormLayout.Calculate(rect, Mathf.Min(160f, rect.width * 0.35f),
                140f, buttonWidth, RowHeight, Spacing, 0f);
            Widgets.Label(form.LabelRect, displayNameLabel);
            string value = Widgets.TextField(form.InputRect, Client.Instance.Settings.DisplayName);
            if (value != Client.Instance.Settings.DisplayName)
            {
                Client.Instance.Settings.DisplayName = value;
                Client.Instance.Settings.AcceptChanges();
            }
            if (Widgets.ButtonText(form.ActionRect, setNameLabel)) Client.Instance.UpdateDisplayName(value);
            if (!string.Equals(cachedPreviewName, value, StringComparison.Ordinal))
            {
                cachedPreviewName = value;
                previewText = "Phinix_settings_displayNamePreview".Translate(value).Resolve();
            }
            Rect previewRect = new Rect(rect.x, rect.y + form.Height + Spacing, rect.width,
                Mathf.Max(0f, rect.height - form.Height - Spacing));
            Widgets.Label(previewRect, previewText);
            TooltipHandler.TipRegion(previewRect, previewText);
        }

        private void RebuildLayoutIfNeeded(float width)
        {
            object language = LanguageDatabase.activeLanguage;
            if (Mathf.Approximately(cachedWidth, width) && ReferenceEquals(cachedLanguage, language)) return;
            cachedWidth = width;
            cachedLanguage = language;
            cachedConnectedAddress = null;
            cachedPreviewName = null;
            addressLabel = "Phinix_settings_addressLabel".Translate();
            portLabel = "Phinix_settings_portLabel".Translate();
            connectLabel = "Phinix_settings_connectButton".Translate();
            connectingLabel = "Phinix_settings_connecting".Translate();
            connectionFailedLabel = "Phinix_settings_connectionFailed".Translate();
            disconnectLabel = "Phinix_settings_disconnectButton".Translate();
            setNameLabel = "Phinix_settings_setDisplayNameButton".Translate();
            displayNameLabel = "Phinix_modSettings_displayNameTitle".Translate();
            connectedHeight = width < 280f ? RowHeight * 2f + Spacing : RowHeight;
            Rect probe = new Rect(0f, 0f, width, 500f);
            float labelWidth = Mathf.Min(120f, width * 0.28f);
            ResponsiveFormResult address = ResponsiveFormLayout.Calculate(probe, labelWidth, 140f, 0f, RowHeight, Spacing, 0f);
            probe.y = address.Height + Spacing;
            ResponsiveFormResult port = ResponsiveFormLayout.Calculate(probe, labelWidth, 70f,
                Mathf.Min(width, Mathf.Max(100f, Text.CalcSize(connectLabel).x + 24f)), RowHeight, Spacing, 0f);
            connectionHeight = address.Height + Spacing + port.Height;
            ResponsiveFormResult name = ResponsiveFormLayout.Calculate(new Rect(0f, 0f, width, 500f),
                Mathf.Min(160f, width * 0.35f), 140f,
                Mathf.Min(width, Mathf.Max(100f, Text.CalcSize(setNameLabel).x + 24f)), RowHeight, Spacing, 0f);
            displayNameHeight = name.Height + Spacing + RowHeight;
        }

        private void ClampToScreen()
        {
            windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(320f, 180f));
        }
    }
}
