using System.Collections.Generic;
using PhinixClient.GUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    public class ServerTabButtonWorker : MainButtonWorker_ToggleTab
    {
        // 未读角标的几何参数：右上角的小型数字。不加背景块，用 RimWorld 原生
        // 白字样式，保持低调不突兀。
        // 设计哲学 §8.3：Draw 路径不分配、不引入 LINQ；绘制后还原 GUI 全局状态。
        private const float BADGE_HEIGHT = 20f;
        private const float BADGE_MIN_WIDTH = 20f;
        private const float BADGE_MARGIN = 4f;

        public override void DoButton(Rect inRect)
        {
            base.DoButton(inRect);

            // 聚合所有 IBadgeProvider，只显示第一个有内容的角标（设计哲学 §3.1）。
            // 用索引遍历，避免 Draw 路径上的 LINQ 分配（§8.3）。
            IReadOnlyList<IMainTabProvider> providers = Client.Instance.MainTabProviders;
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] is IBadgeProvider badge && !string.IsNullOrEmpty(badge.BadgeText))
                {
                    DrawBadge(inRect, badge.BadgeText);
                    break;
                }
            }
        }

        /// <summary>
        /// 在按钮右上角绘制一个未读数字角标。
        /// 不加背景块，直接用 RimWorld 原生白色 Label（自带描边/阴影观感），
        /// 在深色底栏上自然可读、不突兀。数字在框内垂直/水平居中。
        /// </summary>
        private static void DrawBadge(Rect inRect, string text)
        {
            // 宽度随位数略微加宽（如 "99+"），用启发式而非 CalcSize，避免每帧分配。
            float width = BADGE_MIN_WIDTH + Mathf.Max(0, text.Length - 2) * 9f;

            Rect badgeRect = new Rect(
                inRect.xMax - width - BADGE_MARGIN,
                inRect.yMin + BADGE_MARGIN,
                width,
                BADGE_HEIGHT);

            TextAnchor prevAnchor = Text.Anchor;
            GameFont prevFont = Text.Font;
            bool prevWordWrap = Text.WordWrap;
            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                Widgets.Label(badgeRect, text);
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                Text.WordWrap = prevWordWrap;
            }
        }
    }
}
