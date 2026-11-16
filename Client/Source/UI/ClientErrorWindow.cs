using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    // Emergency Host UI deliberately uses native controls, not optional plugin contributions or presentation.
    internal sealed class ClientErrorWindow : Window
    {
        private readonly ClientErrorService errors;
        private readonly ClientErrorNotificationController controller;
        private IReadOnlyList<ClientErrorSummary> records = new ClientErrorSummary[0];
        private long revision = -1;
        private string selectedId;
        private int index;
        private string copyStatus;
        private Vector2 scrollPosition;

        internal ClientErrorWindow(ClientErrorService errors, ClientErrorNotificationController controller)
        {
            this.errors = errors;
            this.controller = controller;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(620f, UiScreenSafeArea.Current.width), Mathf.Min(420f, UiScreenSafeArea.Current.height));
        private static string L(string key) => ("Phinix_errorReport_" + key).Translate().ToString();

        public override void DoWindowContents(Rect inRect)
        {
            try { Draw(inRect); }
            catch (Exception error)
            {
                errors.ReportHost(error, "ErrorWindowDrawFailed", "DrawErrorWindow", false);
                Close();
            }
        }
        private void Draw(Rect rect)
        {
            if (revision != errors.Revision)
            {
                // Read revision before capture so a report arriving between the two is refreshed next frame.
                long capturedRevision = errors.Revision;
                records = errors.Capture();
                revision = capturedRevision;
                index = 0;
                for (int i = 0; i < records.Count; i++) if (records[i].Id == selectedId) { index = i; break; }
                selectedId = records.Count == 0 ? null : records[index].Id;
            }
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = true;
                UnityEngine.GUI.color = Color.white;
                UnityEngine.GUI.backgroundColor = Color.white;
                UnityEngine.GUI.contentColor = Color.white;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                float buttonsY = rect.yMax - 38;
                float navigationY = buttonsY - 48;
                var viewport = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0, navigationY - rect.y - 46));
                float contentWidth = Mathf.Max(1, viewport.width - 20);
                string title = L("title");
                float titleHeight = Text.CalcHeight(title, contentWidth);
                Text.Font = GameFont.Small;
                string body = L(records.Count == 0 ? "empty" : "message");
                float bodyHeight = Text.CalcHeight(body, contentWidth);
                string summary = "";
                if (records.Count != 0)
                {
                    var record = records[index];
                    summary = L("source") + " " + record.Owner + "\n" +
                        L("number") + " " + record.Id + "\n" +
                        L("occurrences") + " " + record.Count;
                }
                float summaryHeight = Text.CalcHeight(summary, contentWidth);
                var content = new Rect(0, 0, contentWidth, Mathf.Max(viewport.height, titleHeight + bodyHeight + summaryHeight + 32));
                Widgets.BeginScrollView(viewport, ref scrollPosition, content);
                try
                {
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(0, 0, contentWidth, titleHeight), title);
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(0, titleHeight + 12, contentWidth, bodyHeight), body);
                    Widgets.Label(new Rect(0, titleHeight + bodyHeight + 24, contentWidth, summaryHeight), summary);
                }
                finally { Widgets.EndScrollView(); }
                // Details are copied from the retained report, never from clipped on-screen labels.
                if (records.Count > 1)
                {
                    if (Widgets.ButtonText(new Rect(rect.x, navigationY, 90, 30), L("previous"))) Select((index + records.Count - 1) % records.Count);
                    Widgets.Label(new Rect(rect.x + 104, navigationY + 4, rect.width - 208, 30), (index + 1) + " / " + records.Count);
                    if (Widgets.ButtonText(new Rect(rect.xMax - 90, navigationY, 90, 30), L("next"))) Select((index + 1) % records.Count);
                }
                if (copyStatus != null)
                    Widgets.Label(new Rect(rect.x, navigationY - 38, rect.width, 34), L(copyStatus));
                float width = (rect.width - 12) / 2;
                if (records.Count != 0 && Widgets.ButtonText(new Rect(rect.x, buttonsY, width, 36), L("copy")))
                    copyStatus = controller.Copy(selectedId, value => GUIUtility.systemCopyBuffer = value) ? "copied" : "copyFailed";
                if (Widgets.ButtonText(new Rect(rect.x + width + 12, buttonsY, width, 36), L("close"))) Close();
            }
        }
        private void Select(int value) { index = value; selectedId = records[index].Id; copyStatus = null; }
    }
}
