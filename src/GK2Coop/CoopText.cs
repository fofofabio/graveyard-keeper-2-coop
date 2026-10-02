using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace GK2Coop
{
    /// <summary>
    /// The mod's own text in the game's language.
    ///
    /// Every player-facing sentence is written in English in the code and looked up here; the
    /// English text is the key, so the code stays readable and a missing translation simply shows
    /// English. The language follows the game's own setting (<c>LLBase.CurrentLang</c>), checked
    /// every couple of seconds so a change in the options applies without a restart.
    /// Placeholders are <c>{0}</c>, <c>{1}</c>… as in <see cref="string.Format(string, object[])"/>.
    /// </summary>
    internal static partial class L
    {
        private static string language = "en";
        private static float nextCheck;
        private static Type languageType;

        /// <summary>The game's language code, such as "en" or "de".</summary>
        internal static string Language
        {
            get
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now >= nextCheck)
                {
                    nextCheck = now + 2f;
                    try
                    {
                        languageType = languageType ?? Plugin.FindGameType("LLBase");
                        string current = Convert.ToString(CoopDiagnostics.GetStatic(languageType, "CurrentLang"));
                        if (!string.IsNullOrEmpty(current))
                        {
                            language = current;
                        }
                    }
                    catch (Exception)
                    {
                        // Keep the last known language.
                    }
                }
                return language;
            }
        }

        /// <summary>Tests and diagnostics: force a language ("de", "en"), or null to follow the game.</summary>
        internal static string Forced { get; set; }

        private static Dictionary<string, Dictionary<string, string>> tables;

        /// <summary>One table per language, keyed by the game's language id ("de", "pt-br", "zh_cn"…).</summary>
        private static Dictionary<string, Dictionary<string, string>> Tables
        {
            get
            {
                if (tables == null)
                {
                    var all = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    AddGerman(all);
                    AddFrench(all);
                    AddSpanish(all);
                    AddPortuguese(all);
                    AddPolish(all);
                    AddRussian(all);
                    AddTurkish(all);
                    AddJapanese(all);
                    AddChinese(all);
                    AddKorean(all);
                    tables = all;
                }
                return tables;
            }
        }

        /// <summary>The table for a language id, trying "pt-br" then "pt".</summary>
        private static Dictionary<string, string> TableFor(string lang)
        {
            if (string.IsNullOrEmpty(lang) || lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            Dictionary<string, string> table;
            if (Tables.TryGetValue(lang, out table))
            {
                return table;
            }
            int cut = lang.IndexOfAny(new[] { '-', '_' });
            return cut > 0 && Tables.TryGetValue(lang.Substring(0, cut), out table) ? table : null;
        }

        /// <summary>The text in the current language; English when there is no translation.</summary>
        internal static string T(string english)
        {
            Dictionary<string, string> table = TableFor(Forced ?? Language);
            string translated;
            return table != null && table.TryGetValue(english, out translated) ? translated : english;
        }

        /// <summary>
        /// The game's own translation of one of its keys (LLBase.L), or null when it has none —
        /// for names the game already translates, such as places.
        /// </summary>
        internal static string Game(string key)
        {
            try
            {
                Type lang = languageType ?? (languageType = Plugin.FindGameType("LLBase"));
                MethodInfo has = lang?.GetMethod("HasL", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
                MethodInfo get = lang?.GetMethod("L", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
                if (get == null || (has != null && !(bool)has.Invoke(null, new object[] { key })))
                {
                    return null;
                }
                string text = get.Invoke(null, new object[] { key }) as string;
                return string.IsNullOrEmpty(text) || text == key ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A place's name as the game shows it in the current language. The game's place names are
        /// "wz_" plus the id in lower case with underscores ("IntroScoutBuilding" is
        /// wz_intro_scout_building, "Abandoned House"); a scene without its own name takes the most
        /// specific one it ends with ("PalaceSewer" is in the Sewers). Otherwise the id, readable.
        /// </summary>
        internal static string Place(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId))
            {
                return sceneId;
            }
            string[] words = System.Text.RegularExpressions.Regex.Split(sceneId.Replace('_', ' '), @"(?<=[a-z0-9])(?=[A-Z])|\s+");
            words = Array.FindAll(words, w => w.Length > 0);
            for (int start = 0; start < words.Length; start++)
            {
                string snake = string.Join("_", words, start, words.Length - start).ToLowerInvariant();
                string name = Game("wz_" + snake) ?? Game("wz_" + snake + "s");
                if (name != null)
                {
                    return name;
                }
            }
            return words.Length == 0 ? sceneId : string.Join(" ", words);
        }

        /// <summary>Marks English text that is translated later, where it is shown (for the translation check).</summary>
        internal static string Key(string english)
        {
            return english;
        }

        /// <summary>The text in the current language, with its placeholders filled in.</summary>
        internal static string F(string english, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, T(english), values);
        }
    }
}
