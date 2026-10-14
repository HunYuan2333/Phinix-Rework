using System.Text.RegularExpressions;
using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal static class ChatTheme
    {
        internal static long Version => Current?.Version ?? 0;
        public static Color MessageText => Current?.GetColor("chat.messageText").ToUnityColor() ?? default(Color);
        public static Color InputText => Current?.GetColor("chat.inputText").ToUnityColor() ?? default(Color);
        public static Color MentionText => Current?.GetColor("chat.mentionText").ToUnityColor() ?? default(Color);
        public static Color MentionSelfBg => Current?.GetColor("chat.mentionSelfBg").ToUnityColor() ?? default(Color);
        public static Color SelfName => Current?.GetColor("chat.selfName").ToUnityColor() ?? default(Color);
        public static Color SelfMessageBg => Current?.GetColor("chat.selfMessageBg").ToUnityColor() ?? default(Color);
        public static Color RowHoverBg => Current?.GetColor("chat.rowHoverBg").ToUnityColor() ?? default(Color);
        public static Color GroupIndentLine => Current?.GetColor("chat.groupIndentLine").ToUnityColor() ?? default(Color);
        public static Color ReplyQuoteBorder => Current?.GetColor("chat.replyQuoteBorder").ToUnityColor() ?? default(Color);
        public static Color ReplyQuoteBg => Current?.GetColor("chat.replyQuoteBg").ToUnityColor() ?? default(Color);
        public static Color ReplyQuoteText => Current?.GetColor("chat.replyQuoteText").ToUnityColor() ?? default(Color);
        public static Color NoticeAccent => Current?.GetColor("chat.noticeAccent").ToUnityColor() ?? default(Color);
        public static Color NoticeBg => Current?.GetColor("chat.noticeBg").ToUnityColor() ?? default(Color);
        public static Color NoticeBannerBg => Current?.GetColor("chat.noticeBannerBg").ToUnityColor() ?? default(Color);
        public static Color NoticeProgress => Current?.GetColor("chat.noticeProgress").ToUnityColor() ?? default(Color);
        public static Color InputReplyBorder => Current?.GetColor("chat.inputReplyBorder").ToUnityColor() ?? default(Color);
        public static Color InputReplyBg => Current?.GetColor("chat.inputReplyBg").ToUnityColor() ?? default(Color);
        public static Color BlockedBg => Current?.GetColor("chat.blockedBg").ToUnityColor() ?? default(Color);
        public static Color BlockedName => Current?.GetColor("chat.blockedName").ToUnityColor() ?? default(Color);
        public static Color PendingMessage => Current?.GetColor("chat.pendingMessage").ToUnityColor() ?? default(Color);
        public static Color DeniedMessage => Current?.GetColor("chat.deniedMessage").ToUnityColor() ?? default(Color);
        public static Color ImagePlaceholderBg => Current?.GetColor("chat.imagePlaceholderBg").ToUnityColor() ?? default(Color);
        public static Color ImageFailedText => Current?.GetColor("chat.imageFailedText").ToUnityColor() ?? default(Color);

        // 用户名配色 HSV 参数——由主题 XML <param> 控制，颜色计算统一走这里
        private static float nameColorSaturation => Current?.GetFloat("chat.nameColor.saturation", .55f) ?? .55f;
        private static float nameColorValue => Current?.GetFloat("chat.nameColor.value", .85f) ?? .85f;

        private static IClientTheme source;
        private static IClientThemeReader latest, drawing;
        internal static IClientThemeReader Current => drawing ?? latest;
        internal static void Refresh(IClientTheme theme)
        { source = theme; latest = theme.Capture(); }
        internal static void Release() { source = null; latest = null; }
        internal static DrawScope BeginDraw() => new DrawScope(true);
        internal struct DrawScope : IDisposable
        {
            private readonly IClientThemeReader previous;
            internal DrawScope(bool capture)
            { previous = drawing; drawing = previous ?? source?.Capture() ?? latest; }
            public void Dispose() { drawing = previous; }
        }

        private static readonly Regex OpenTagRegex = new Regex(@"<(?!/)[^>]+>", RegexOptions.Compiled);
        private static readonly Regex CloseTagRegex = new Regex(@"</[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// 格式化显示名：含合法富文本（标签平衡）时保留用户自定义样式，
        /// 纯文本或标签不平衡（恶意/未闭合）时剥离并用 hash 色着色。
        /// </summary>
        internal static string FormatDisplayName(string rawName, string uuid, Color fallbackColor)
        {
            if (string.IsNullOrEmpty(rawName))
                return "";

            if (rawName.IndexOf('<') >= 0 && HasBalancedTags(rawName))
            {
                return rawName;
            }

            string stripped = Utils.TextHelper.StripRichText(rawName);
            return stripped.Colorize(fallbackColor);
        }

        private static bool HasBalancedTags(string text)
        {
            int openCount = OpenTagRegex.Matches(text).Count;
            int closeCount = CloseTagRegex.Matches(text).Count;
            return openCount > 0 && openCount == closeCount;
        }

        /// <summary>
        /// 统一入口：根据 UUID 的哈希值生成稳定颜色，饱和度/明度由主题配置。
        /// ChatMessageList 和 UserList 均通过此方法，消除重复的 GetNameColor 实现。
        /// </summary>
        internal static Color GetNameColor(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return SelfName;
            int hash = uuid.GetHashCode();
            float h = (Mathf.Abs(hash) % 360) / 360f;
            Color rgb = Color.HSVToRGB(h, nameColorSaturation, nameColorValue);
            rgb.a = 1f;
            return rgb;
        }
    }
}
