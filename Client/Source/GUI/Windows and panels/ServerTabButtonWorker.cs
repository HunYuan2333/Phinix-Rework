using System.Collections.Generic;
using System.Linq;
using PhinixClient.GUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    public class ServerTabButtonWorker : MainButtonWorker_ToggleTab
    {
        private const float PADDING = 5f;

        public override void DoButton(Rect inRect)
        {
            base.DoButton(inRect);

            var badgeProviders = Client.Instance.MainTabProviders
                .OfType<IBadgeProvider>();

            Rect iconRect = inRect.RightPartPixels(inRect.height + PADDING).LeftPartPixels(inRect.height);
            foreach (var badge in badgeProviders)
            {
                if (!string.IsNullOrEmpty(badge.BadgeText))
                {
                    new TextWidget(badge.BadgeText, anchor: TextAnchor.MiddleCenter).Draw(iconRect);
                    break; // show first badge only
                }
            }
        }
    }
}
