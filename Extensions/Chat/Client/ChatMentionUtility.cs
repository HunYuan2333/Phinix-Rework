using System.Text.RegularExpressions;

namespace Phinix.ChatExtension.Client
{
    internal static class ChatMentionUtility
    {
        private static readonly Regex AtPartialRegex = new Regex(@"@([^\s<>]*)$", RegexOptions.Compiled);
        // Consume tags before looking for mentions, including @ inside attributes.
        private static readonly Regex MentionOrTagRegex = new Regex(@"<[^>]*>|@[^\s<>]+", RegexOptions.Compiled);

        internal static bool TryGetAutocompletePartial(string text, string previousText, bool canOpen, out string partial)
        {
            partial = null;
            if (!canOpen || string.IsNullOrEmpty(text) || text == previousText) return false;

            Match match = AtPartialRegex.Match(text);
            if (!match.Success || match.Groups[1].Length == 0) return false;

            partial = match.Groups[1].Value;
            return true;
        }

        internal static string ReplaceAtPartial(string text, string fullName)
        {
            if (string.IsNullOrEmpty(text)) return text;
            Match match = AtPartialRegex.Match(text);
            if (!match.Success) return text;

            return text.Substring(0, match.Index + 1) + fullName + " ";
        }

        internal static string Highlight(string text, string rgbHex)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return MentionOrTagRegex.Replace(text, match => match.Value[0] == '<'
                ? match.Value
                : "<color=#" + rgbHex + ">" + match.Value + "</color>");
        }
    }
}
