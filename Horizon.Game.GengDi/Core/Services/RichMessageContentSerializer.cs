using System;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class RichMessageContentSerializer
    {
        public static string Serialize(RichMessageContent content)
        {
            return JsonConvert.SerializeObject(content ?? RichMessageContent.CreateText(string.Empty));
        }

        public static RichMessageContent Deserialize(Horizon.Game.GengDi.Models.IMMessage message)
        {
            if (message == null)
            {
                return RichMessageContent.CreateText(string.Empty);
            }

            return Deserialize(message.Type, message.Content);
        }

        public static RichMessageContent Deserialize(MessageType type, string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
            {
                return CreateLegacyFallback(type, string.Empty);
            }

            try
            {
                var content = JsonConvert.DeserializeObject<RichMessageContent>(rawContent);
                if (content != null && string.Equals(content.Schema, RichMessageContent.SchemaName, StringComparison.Ordinal))
                {
                    if (content.Type != type && type != MessageType.Text)
                    {
                        content.Type = type;
                    }

                    return Normalize(content);
                }
            }
            catch
            {
            }

            return CreateLegacyFallback(type, rawContent);
        }

        private static RichMessageContent Normalize(RichMessageContent content)
        {
            content.Text ??= string.Empty;
            content.MediaUrl ??= string.Empty;
            content.OriginalUrl ??= string.Empty;
            content.PreviewImageUrl ??= string.Empty;
            content.PlaybackUrl ??= string.Empty;
            content.Title ??= string.Empty;
            content.Subtitle ??= string.Empty;
            content.SiteName ??= string.Empty;
            content.Provider ??= string.Empty;
            content.Attachments ??= new List<MessageAttachment>();
            content.Schema = RichMessageContent.SchemaName;
            return content;
        }

        private static RichMessageContent CreateLegacyFallback(MessageType type, string rawContent)
        {
            switch (type)
            {
                case MessageType.Image:
                    return RichMessageContent.CreateImage(rawContent, title: "图片消息");

                case MessageType.Video:
                    return RichMessageContent.CreateVideo(
                        rawContent,
                        SocialLinkParser.BuildDirectVideoPlaybackAddress(rawContent),
                        title: "视频消息",
                        subtitle: "直链视频",
                        siteName: "视频",
                        provider: "video");

                case MessageType.LinkCard:
                    if (SocialLinkParser.TryParse(rawContent, out var parsedLink))
                    {
                        return RichMessageContent.CreateLinkCard(
                            parsedLink.OriginalUrl,
                            parsedLink.PlaybackUrl,
                            parsedLink.TitleFallback,
                            parsedLink.SubtitleFallback,
                            parsedLink.SiteName,
                            parsedLink.PreviewImageUrl,
                            provider: parsedLink.Provider,
                            supportsInlinePlayback: parsedLink.SupportsInlinePlayback);
                    }

                    return RichMessageContent.CreateLinkCard(rawContent, rawContent, rawContent, string.Empty, "链接卡片");

                case MessageType.Emoji:
                    if (EmojiRegistry.TryParseEmojiId(rawContent, out var emojiId))
                    {
                        return RichMessageContent.CreateEmoji(emojiId);
                    }

                    return RichMessageContent.CreateText(rawContent);

                default:
                    return RichMessageContent.CreateText(rawContent);
            }
        }
    }
}