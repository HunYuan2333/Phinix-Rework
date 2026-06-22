using System.Text.RegularExpressions;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal static class ChatTheme
    {
        public static Color MentionText;
        public static Color MentionSelfBg;
        public static Color SelfName;
        public static Color SelfMessageBg;
        public static Color RowHoverBg;
        public static Color GroupIndentLine;
        public static Color ReplyQuoteBorder;
        public static Color ReplyQuoteBg;
        public static Color ReplyQuoteText;
        public static Color NoticeAccent;
        public static Color NoticeBg;
        public static Color NoticeBannerBg;
        public static Color NoticeProgress;
        public static Color InputReplyBorder;
        public static Color InputReplyBg;
        public static Color BlockedBg;
        public static Color BlockedName;
        public static Color PendingMessage;
        public static Color DeniedMessage;

        // 用户名配色 HSV 参数——由主题 XML <param> 控制，颜色计算统一走这里
        private static float nameColorSaturation = 0.55f;
        private static float nameColorValue = 0.85f;

        internal static void Refresh(IUiTheme theme)
        {
            MentionText = theme.GetColor("chat.mentionText");
            MentionSelfBg = theme.GetColor("chat.mentionSelfBg");
            SelfName = theme.GetColor("chat.selfName");
            SelfMessageBg = theme.GetColor("chat.selfMessageBg");
            RowHoverBg = theme.GetColor("chat.rowHoverBg");
            GroupIndentLine = theme.GetColor("chat.groupIndentLine");
            ReplyQuoteBorder = theme.GetColor("chat.replyQuoteBorder");
            ReplyQuoteBg = theme.GetColor("chat.replyQuoteBg");
            ReplyQuoteText = theme.GetColor("chat.replyQuoteText");
            NoticeAccent = theme.GetColor("chat.noticeAccent");
            NoticeBg = theme.GetColor("chat.noticeBg");
            NoticeBannerBg = theme.GetColor("chat.noticeBannerBg");
            NoticeProgress = theme.GetColor("chat.noticeProgress");
            InputReplyBorder = theme.GetColor("chat.inputReplyBorder");
            InputReplyBg = theme.GetColor("chat.inputReplyBg");
            BlockedBg = theme.GetColor("chat.blockedBg");
            BlockedName = theme.GetColor("chat.blockedName");
            PendingMessage = theme.GetColor("chat.pendingMessage");
            DeniedMessage = theme.GetColor("chat.deniedMessage");

            // 读取主题化的 HSV 参数
            nameColorSaturation = theme.GetFloat("nameColor.saturation", 0.55f);
            nameColorValue = theme.GetFloat("nameColor.value", 0.85f);
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
