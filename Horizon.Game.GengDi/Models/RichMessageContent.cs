using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Models
{
    public sealed class MessageAttachment
    {
        public string MediaUrl { get; set; } = string.Empty;
        public string PreviewImageUrl { get; set; } = string.Empty;
        public string AttachmentType { get; set; } = "image";
    }

    public sealed class RichMessageContent
    {
        public const string SchemaName = "horizon-rich-message-v1";

        public string Schema { get; set; } = SchemaName;

        public MessageType Type { get; set; } = MessageType.Text;

        public string Text { get; set; } = string.Empty;

        public string MediaUrl { get; set; } = string.Empty;

        public string PreviewImageUrl { get; set; } = string.Empty;

        public string PlaybackUrl { get; set; } = string.Empty;

        public string OriginalUrl { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public string SiteName { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;

        public bool SupportsInlinePlayback { get; set; }

        public int EmojiId { get; set; } = -1;

        public string FileName { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public List<MessageAttachment> Attachments { get; set; } = new();

        public static RichMessageContent CreateText(string text)
        {
            return new RichMessageContent
            {
                Type = MessageType.Text,
                Text = text ?? string.Empty
            };
        }

        public static RichMessageContent CreateImage(
            string mediaUrl,
            string text = "",
            string title = "",
            string subtitle = "",
            string previewImageUrl = "")
        {
            return new RichMessageContent
            {
                Type = MessageType.Image,
                Text = text ?? string.Empty,
                MediaUrl = mediaUrl ?? string.Empty,
                OriginalUrl = mediaUrl ?? string.Empty,
                PreviewImageUrl = string.IsNullOrWhiteSpace(previewImageUrl) ? mediaUrl ?? string.Empty : previewImageUrl,
                Title = title ?? string.Empty,
                Subtitle = subtitle ?? string.Empty,
                SiteName = "图片",
                Provider = "image",
                SupportsInlinePlayback = false
            };
        }

        public static RichMessageContent CreateVideo(
            string mediaUrl,
            string playbackUrl,
            string text = "",
            string title = "",
            string subtitle = "",
            string previewImageUrl = "",
            string siteName = "视频",
            string provider = "video")
        {
            return new RichMessageContent
            {
                Type = MessageType.Video,
                Text = text ?? string.Empty,
                MediaUrl = mediaUrl ?? string.Empty,
                OriginalUrl = mediaUrl ?? string.Empty,
                PreviewImageUrl = previewImageUrl ?? string.Empty,
                PlaybackUrl = playbackUrl ?? string.Empty,
                Title = title ?? string.Empty,
                Subtitle = subtitle ?? string.Empty,
                SiteName = siteName ?? string.Empty,
                Provider = provider ?? string.Empty,
                SupportsInlinePlayback = !string.IsNullOrWhiteSpace(playbackUrl)
            };
        }

        public static RichMessageContent CreateLinkCard(
            string originalUrl,
            string playbackUrl,
            string title,
            string subtitle,
            string siteName,
            string previewImageUrl = "",
            string text = "",
            string provider = "link",
            bool supportsInlinePlayback = false)
        {
            return new RichMessageContent
            {
                Type = MessageType.LinkCard,
                Text = text ?? string.Empty,
                OriginalUrl = originalUrl ?? string.Empty,
                MediaUrl = originalUrl ?? string.Empty,
                PreviewImageUrl = previewImageUrl ?? string.Empty,
                PlaybackUrl = playbackUrl ?? string.Empty,
                Title = title ?? string.Empty,
                Subtitle = subtitle ?? string.Empty,
                SiteName = siteName ?? string.Empty,
                Provider = provider ?? string.Empty,
                SupportsInlinePlayback = supportsInlinePlayback && !string.IsNullOrWhiteSpace(playbackUrl)
            };
        }

        public static RichMessageContent CreateFile(string filePath, string text = "")
        {
            var fileName = System.IO.Path.GetFileName(filePath) ?? "未知文件";
            long fileSize = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    fileSize = new FileInfo(filePath).Length;
                }
            }
            catch { }

            return new RichMessageContent
            {
                Type = MessageType.File,
                Text = string.IsNullOrWhiteSpace(text) ? fileName : text,
                MediaUrl = filePath ?? string.Empty,
                FileName = fileName,
                FileSize = fileSize,
                Provider = "file"
            };
        }

        public static RichMessageContent CreateEmoji(int emojiId)
        {
            var emoji = EmojiRegistry.GetEmoji(emojiId);
            return new RichMessageContent
            {
                Type = MessageType.Emoji,
                Text = emoji,
                EmojiId = emojiId,
                Provider = "emoji"
            };
        }
    }
}