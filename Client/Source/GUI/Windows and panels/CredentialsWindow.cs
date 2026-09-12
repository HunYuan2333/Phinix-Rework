using Authentication;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    public class CredentialsWindow : Window
    {
        private const float Spacing = 10f;
        private const float RowHeight = 30f;
        private const float PreferredDescriptionHeight = 120f;
        private Vector2 scrollPosition;
        private Vector2 descriptionScrollPosition;
        private string usernameText = "";
        private string passwordText = "";
        private float cachedWidth = -1f;
        private object cachedLanguage;
        private string cachedDescription;
        private string cachedServerName;
        private float contentHeight;
        private float titleHeight;
        private float serverNameHeight;
        private float descriptionHeight;
        private float descriptionContentHeight;
        private string titleText;
        private string submitText;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safe = UiScreenSafeArea.Current;
                return new Vector2(Mathf.Min(480f, safe.width), Mathf.Min(520f, safe.height));
            }
        }

        public string SessionId;
        public string ServerName;
        public string ServerDescription;
        public AuthTypes AuthType;
        public ClientAuthenticator.ReturnCredentialsDelegate CredentialsCallback;

        public CredentialsWindow()
        {
            doCloseX = true;
            doCloseButton = false;
            doWindowBackground = true;
            draggable = true;
            resizeable = true;
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
            float width = Mathf.Max(1f, inRect.width - 16f);
            RebuildLayout(width);
            float viewWidth = contentHeight > inRect.height ? width : inRect.width;
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, contentHeight - inRect.height));
            Widgets.BeginScrollView(inRect, ref scrollPosition,
                new Rect(0f, 0f, viewWidth, Mathf.Max(contentHeight, inRect.height)));
            try
            {
                float y = 0f;
                Widgets.Label(new Rect(0f, y, viewWidth, titleHeight), titleText);
                y += titleHeight + Spacing;
                Widgets.Label(new Rect(0f, y, viewWidth, serverNameHeight), ServerName ?? "");
                TooltipHandler.TipRegion(new Rect(0f, y, viewWidth, serverNameHeight), ServerName ?? "");
                y += serverNameHeight + Spacing;
                Rect descriptionRect = new Rect(0f, y, viewWidth, descriptionHeight);
                Widgets.DrawMenuSection(descriptionRect);
                Rect descriptionInner = descriptionRect.ContractedBy(6f);
                bool descriptionOverflows = descriptionContentHeight > descriptionInner.height;
                float descriptionViewWidth = Mathf.Max(1f,
                    descriptionInner.width - (descriptionOverflows ? 16f : 0f));
                descriptionScrollPosition.y = Mathf.Clamp(descriptionScrollPosition.y, 0f,
                    Mathf.Max(0f, descriptionContentHeight - descriptionInner.height));
                Widgets.BeginScrollView(descriptionInner, ref descriptionScrollPosition,
                    new Rect(0f, 0f, descriptionViewWidth,
                        Mathf.Max(descriptionContentHeight, descriptionInner.height)));
                try
                {
                    Widgets.Label(new Rect(0f, 0f, descriptionViewWidth, descriptionContentHeight),
                        ServerDescription ?? "");
                }
                finally { Widgets.EndScrollView(); }
                TooltipHandler.TipRegion(descriptionRect, ServerDescription ?? "");
                y += descriptionHeight + Spacing;
                usernameText = Widgets.TextField(new Rect(0f, y, viewWidth, RowHeight), usernameText);
                y += RowHeight + Spacing;
                passwordText = Widgets.TextField(new Rect(0f, y, viewWidth, RowHeight), passwordText);
                y += RowHeight + Spacing;
                if (Widgets.ButtonText(new Rect(0f, y, viewWidth, RowHeight), submitText))
                {
                    CredentialsCallback?.Invoke(true, SessionId, AuthType, usernameText, passwordText);
                    CredentialsCallback = null;
                    Close();
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        public override void PostClose()
        {
            base.PostClose();
            CredentialsCallback?.Invoke(false, null, 0, null, null);
        }

        private void RebuildLayout(float width)
        {
            object language = LanguageDatabase.activeLanguage;
            string description = ServerDescription ?? "";
            if (Mathf.Approximately(cachedWidth, width) && ReferenceEquals(cachedLanguage, language) &&
                cachedDescription == description && cachedServerName == (ServerName ?? "")) return;
            cachedWidth = width;
            cachedLanguage = language;
            cachedDescription = description;
            cachedServerName = ServerName ?? "";
            titleText = "Phinix_login_logInLabel".Translate();
            submitText = "Phinix_login_submitButton".Translate();
            titleHeight = Mathf.Max(RowHeight, Text.CalcHeight(titleText, width));
            serverNameHeight = Mathf.Max(RowHeight, Text.CalcHeight(ServerName ?? "", width));
            descriptionContentHeight = Mathf.Max(1f,
                Text.CalcHeight(description, Mathf.Max(1f, width - 28f)));
            descriptionHeight = Mathf.Max(60f,
                Mathf.Min(PreferredDescriptionHeight, descriptionContentHeight + 12f));
            contentHeight = titleHeight + serverNameHeight + descriptionHeight + RowHeight * 3f + Spacing * 5f;
        }

        private void ClampToScreen()
        {
            windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(320f, 300f));
        }
    }
}
