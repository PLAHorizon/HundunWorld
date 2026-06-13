using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class LrcParser
    {
        private static readonly Regex TimeTagRegex = new Regex(
            @"\[(?<min>\d{2,3}):(?<sec>\d{2})(?:\.(?<ms>\d{1,3}))?\]",
            RegexOptions.Compiled);

        public static Lyrics Parse(string lrcText, string translationLrcText = null)
        {
            var lyrics = new Lyrics();

            if (string.IsNullOrWhiteSpace(lrcText))
            {
                return lyrics;
            }

            var translationMap = new Dictionary<TimeSpan, string>();
            if (!string.IsNullOrWhiteSpace(translationLrcText))
            {
                foreach (var line in translationLrcText.Split('\n'))
                {
                    var trimmed = line.Trim();
                    var match = TimeTagRegex.Match(trimmed);
                    if (match.Success)
                    {
                        var ts = ParseTimeSpan(match);
                        var text = TimeTagRegex.Replace(trimmed, "").Trim();
                        if (!string.IsNullOrWhiteSpace(text) && !translationMap.ContainsKey(ts))
                        {
                            translationMap[ts] = text;
                        }
                    }
                }
            }

            var rawLines = new List<(TimeSpan Timestamp, string Text)>();

            foreach (var line in lrcText.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var matches = TimeTagRegex.Matches(trimmed);
                if (matches.Count == 0) continue;

                var text = TimeTagRegex.Replace(trimmed, "").Trim();
                if (string.IsNullOrEmpty(text)) continue;

                foreach (Match m in matches)
                {
                    var ts = ParseTimeSpan(m);
                    rawLines.Add((ts, text));
                }
            }

            rawLines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            foreach (var raw in rawLines)
            {
                var lyricLine = new LyricLine
                {
                    Timestamp = raw.Timestamp,
                    Text = raw.Text
                };

                if (translationMap.TryGetValue(raw.Timestamp, out var translation))
                {
                    lyricLine.Text += $"\n{translation}";
                }

                lyrics.Lines.Add(lyricLine);
            }

            return lyrics;
        }

        public static string ToJson(string lrcText, string translationLrcText = null)
        {
            var lyrics = Parse(lrcText, translationLrcText);
            return Newtonsoft.Json.JsonConvert.SerializeObject(lyrics);
        }

        private static TimeSpan ParseTimeSpan(Match match)
        {
            var min = int.Parse(match.Groups["min"].Value);
            var sec = int.Parse(match.Groups["sec"].Value);
            var msStr = match.Groups["ms"].Value;
            var ms = 0;
            if (!string.IsNullOrEmpty(msStr))
            {
                ms = msStr.Length <= 3 ? int.Parse(msStr.PadRight(3, '0')) : int.Parse(msStr.Substring(0, 3));
            }
            return TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(ms);
        }
    }
}
