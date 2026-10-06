using System;
using System.Collections.Generic;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace PhinixClient.Framework
{
    internal static class ClientGameLanguage
    {
        // The game identifies languages by folder, not a standardized locale.
        private static readonly Dictionary<string,string> locales=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"English","en-US"},{"ChineseSimplified","zh-CN"},{"ChineseTraditional","zh-TW"},
            {"Japanese","ja-JP"},{"Korean","ko-KR"},{"French","fr-FR"},{"German","de-DE"},
            {"Spanish","es-ES"},{"SpanishLatin","es-419"},{"Portuguese","pt-PT"},{"PortugueseBrazilian","pt-BR"},
            {"Russian","ru-RU"},{"Polish","pl-PL"},{"Italian","it-IT"},{"Dutch","nl-NL"},
            {"Turkish","tr-TR"},{"Ukrainian","uk-UA"},{"Czech","cs-CZ"},{"Danish","da-DK"},
            {"Finnish","fi-FI"},{"Hungarian","hu-HU"},{"Norwegian","no-NO"},{"Swedish","sv-SE"},
            {"Romanian","ro-RO"},{"Slovak","sk-SK"},{"Serbian","sr-RS"},{"Greek","el-GR"},
            {"Arabic","ar"},{"Vietnamese","vi-VN"},{"Thai","th-TH"},{"Indonesian","id-ID"}
        };
        internal static string CurrentLocale()
        {
            if(!UnityData.IsInMainThread) throw new InvalidOperationException("Game language requires the main thread.");
            string folder=LanguageDatabase.activeLanguage?.folderName;
            if(folder==null) return null;
            int label=folder.IndexOf(" (",StringComparison.Ordinal); if(label>=0) folder=folder.Substring(0,label);
            string locale; if(locales.TryGetValue(folder,out locale)) return locale;
            try { return ExtensionLocale.Normalize(folder); } catch(ManagedExtensionValidationException) { return null; }
        }
    }
}
