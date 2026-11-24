using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Teal.Library.Localization;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// English text in, selected language out - for text the view models build themselves
    /// (status lines, dialog messages, tab titles, grid captions).
    ///
    /// Looked up by the English value in Strings.resx: a key whose English is the text
    /// gives that key's text in the active language. Tried in this order:
    ///   1. the text exactly (case-insensitive as a second chance);
    ///   2. the text without its trailing punctuation ("Station:" uses "Station");
    ///   3. a "{0}" entry ("{0} of {1} keys") matched against the finished text;
    ///   4. a "Label: value" text, translating the label and keeping the value;
    ///   5. a text of several lines, line by line.
    /// Anything not in the files is shown as it came, in English. Never throws: this is on
    /// the path of every translated label on every screen.
    /// </summary>
    internal static class UiTextTranslator
    {
        private sealed class Pattern
        {
            public string Prefix;
            public Regex Match;
            public string Translation;
            public int Slots;
        }

        private sealed class Table
        {
            public bool Translated;
            public Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, string> Loose = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Pattern[] Patterns = new Pattern[0];
            public ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        }

        private static readonly char[] TrailingPunctuation = { ' ', ':', '!', '.', '?', '*', '…', '›', '：', '！', '。' };
        private static readonly Regex PlaceholderInEscaped = new Regex(@"\\\{(\d+)(?::[^}]*)?}", RegexOptions.Compiled);

        private static readonly object Sync = new object();
        private static volatile Table _table;

        /// <summary>Drops the table; the next lookup re-reads the active language.</summary>
        public static void Reset()
        {
            _table = null;
        }

        public static string Translate(string english)
        {
            if (string.IsNullOrEmpty(english))
                return english;

            try
            {
                Table table = GetTable();
                if (!table.Translated || !HasLetter(english))
                    return english;

                return table.Cache.GetOrAdd(english, text => Lines(table, text) ?? text);
            }
            catch (Exception)
            {
                return english;
            }
        }

        private static string Lines(Table table, string english)
        {
            string whole = Find(table, english, true);
            if (whole != null || english.IndexOf('\n') < 0)
                return whole;

            string[] lines = english.Replace("\r\n", "\n").Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int start = 0;
                while (start < line.Length && (char.IsWhiteSpace(line[start]) || line[start] == '•' || line[start] == '-' || line[start] == '*'))
                    start++;

                if (start >= line.Length)
                    continue;

                string found = Find(table, line.Substring(start), true);
                if (found != null)
                {
                    lines[i] = line.Substring(0, start) + found;
                    any = true;
                }
            }

            return any ? string.Join(Environment.NewLine, lines) : null;
        }

        private static Table GetTable()
        {
            Table table = _table;
            if (table != null)
                return table;

            lock (Sync)
            {
                table = _table;
                if (table == null)
                {
                    table = Build();
                    _table = table;
                }

                return table;
            }
        }

        private static Table Build()
        {
            var table = new Table();

            try
            {
                if (!LanguageManager.IsTranslated)
                    return table;

                var patterns = new List<Pattern>();

                foreach (string key in LocalizationProvider.AllKeys().Where(k => !string.IsNullOrEmpty(k)))
                {
                    if (!LocalizationProvider.IsTranslated(key))
                        continue;

                    string source = Normalize(LocalizationProvider.GetDefault(key));
                    string text = Normalize(LocalizationProvider.Get(key));

                    if (source.Length == 0 || text.Length == 0 ||
                        string.Equals(source, key, StringComparison.Ordinal) ||
                        string.Equals(source, text, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!table.Exact.ContainsKey(source))
                        table.Exact.Add(source, text);
                    if (!table.Loose.ContainsKey(source))
                        table.Loose.Add(source, text);

                    if (source.IndexOf("{0", StringComparison.Ordinal) >= 0)
                    {
                        Pattern pattern = BuildPattern(source, text);
                        if (pattern != null)
                            patterns.Add(pattern);
                    }
                }

                // Longest fixed wording first, so the most specific pattern wins.
                table.Patterns = patterns.OrderByDescending(p => p.Match.ToString().Length).ToArray();
                table.Translated = true;
            }
            catch (Exception)
            {
                // Unreadable language files: stay in English rather than half a language.
                table = new Table();
            }

            return table;
        }

        private static string Find(Table table, string english, bool allowPatterns)
        {
            string key = Normalize(english);
            if (key.Length == 0)
                return null;

            string found = Lookup(table, key);

            if (found == null)
            {
                // "Station :" - translate the words, keep the punctuation.
                string core = key.TrimEnd(TrailingPunctuation);
                if (core.Length > 0 && core.Length < key.Length)
                {
                    string coreText = Lookup(table, core);
                    if (coreText != null)
                        found = coreText + key.Substring(core.Length);
                }
            }

            if (found == null && allowPatterns)
                found = MatchPattern(table, key);

            if (found == null && allowPatterns)
            {
                // "Station: FT200" - translate the fixed lead-in, keep the value.
                int colon = key.IndexOf(':');
                if (colon > 0 && colon < key.Length - 1)
                {
                    string withColon = Lookup(table, key.Substring(0, colon + 1));
                    if (withColon != null)
                    {
                        found = withColon + key.Substring(colon + 1);
                    }
                    else
                    {
                        string head = Lookup(table, key.Substring(0, colon).TrimEnd());
                        if (head != null)
                            found = head + key.Substring(colon);
                    }
                }
            }

            if (found == null)
                return null;

            // Put back the padding the original carried.
            int lead = 0;
            while (lead < english.Length && char.IsWhiteSpace(english[lead]))
                lead++;
            int trail = english.Length;
            while (trail > lead && char.IsWhiteSpace(english[trail - 1]))
                trail--;

            if (lead == 0 && trail == english.Length)
                return found;

            return english.Substring(0, lead) + found + english.Substring(trail);
        }

        private static string Lookup(Table table, string key)
        {
            string value;
            if (table.Exact.TryGetValue(key, out value))
                return value;
            if (table.Loose.TryGetValue(key, out value))
                return value;
            return null;
        }

        /// <summary>
        /// Turns "{0} of {1} keys shown" into a matcher for the finished text. Null for a
        /// text that is nearly all placeholder, which would match everything.
        /// </summary>
        private static Pattern BuildPattern(string source, string translation)
        {
            try
            {
                int slots = 0;
                string escaped = Regex.Escape(source);
                string body = PlaceholderInEscaped.Replace(escaped, delegate (Match m)
                {
                    int index = int.Parse(m.Groups[1].Value);
                    if (index + 1 > slots)
                        slots = index + 1;
                    return "(?<p" + index + ">.+?)";
                });

                if (slots == 0 || slots > 8)
                    return null;

                int letters = 0;
                foreach (char c in PlaceholderInEscaped.Replace(escaped, string.Empty))
                {
                    if (char.IsLetter(c))
                        letters++;
                }

                if (letters < 3)
                    return null;

                int firstSlot = source.IndexOf('{');

                return new Pattern
                {
                    Prefix = firstSlot > 0 ? source.Substring(0, firstSlot) : string.Empty,
                    Match = new Regex("^" + body + "$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    Translation = translation,
                    Slots = slots
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string MatchPattern(Table table, string key)
        {
            Pattern[] patterns = table.Patterns;
            for (int i = 0; i < patterns.Length; i++)
            {
                Pattern pattern = patterns[i];
                if (pattern.Prefix.Length > 0 && !key.StartsWith(pattern.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                Match match = pattern.Match.Match(key);
                if (!match.Success)
                    continue;

                object[] values = new object[pattern.Slots];
                for (int slot = 0; slot < values.Length; slot++)
                {
                    // A slot holding a known word ("Running") is translated too.
                    string captured = match.Groups["p" + slot].Value;
                    values[slot] = Lookup(table, captured.Trim()) ?? captured;
                }

                try
                {
                    return string.Format(pattern.Translation, values);
                }
                catch (FormatException)
                {
                    // The translation lost or gained a placeholder; try the next one.
                }
            }

            return null;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (text.IndexOf('\r') >= 0)
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            return text.Trim();
        }

        private static bool HasLetter(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]))
                    return true;
            }

            return false;
        }
    }
}
