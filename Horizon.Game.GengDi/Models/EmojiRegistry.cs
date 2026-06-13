using System.Collections.Generic;

namespace Horizon.Game.GengDi.Models
{
    public static class EmojiRegistry
    {
        public const string ContentPrefix = "emoji_";

        private static readonly string[] Emojis =
        {
            "😀","😊","😂","🤣","😍","🥰","😎","😭","😱","🤔",
            "👍","👎","❤️","🎉","🎊","🔥","💯","😴","🤗","😄",
            "😆","😅","😇","🙂","🙃","😉","😋","😘","😛","😜",
            "😝","🤑","🤩","🥳","😤","😠","😡","🤬","😞","😟",
            "😣","😖","😫","😩","🥺","😢","👋","🙌","🤝","👏"
        };

        private static readonly Dictionary<string, int> EmojiToIdMap;
        private static readonly Dictionary<int, string> IdToEmojiMap;

        static EmojiRegistry()
        {
            EmojiToIdMap = new Dictionary<string, int>(Emojis.Length);
            IdToEmojiMap = new Dictionary<int, string>(Emojis.Length);

            for (var i = 0; i < Emojis.Length; i++)
            {
                EmojiToIdMap[Emojis[i]] = i;
                IdToEmojiMap[i] = Emojis[i];
            }
        }

        public static string BuildContentId(int emojiId)
        {
            return $"{ContentPrefix}{emojiId}";
        }

        public static bool TryParseEmojiId(string contentId, out int emojiId)
        {
            if (!string.IsNullOrWhiteSpace(contentId) && contentId.StartsWith(ContentPrefix))
            {
                var idStr = contentId.Substring(ContentPrefix.Length);
                if (int.TryParse(idStr, out emojiId) && IdToEmojiMap.ContainsKey(emojiId))
                {
                    return true;
                }
            }

            emojiId = -1;
            return false;
        }

        public static int GetEmojiId(string emoji)
        {
            return EmojiToIdMap.TryGetValue(emoji, out var id) ? id : -1;
        }

        public static string GetEmoji(int emojiId)
        {
            return IdToEmojiMap.TryGetValue(emojiId, out var emoji) ? emoji : string.Empty;
        }

        public static IReadOnlyList<string> AllEmojis => Emojis;
    }
}
