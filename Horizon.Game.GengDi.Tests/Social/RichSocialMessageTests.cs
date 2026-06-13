using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class RichSocialMessageTests
{
    [Fact]
    public void RichMessageContentSerializer_FallsBack_ToLegacyTextMessage()
    {
        var message = new Horizon.Game.GengDi.Models.IMMessage
        {
            Id = "msg-1",
            SenderId = "sender",
            ReceiverId = "receiver",
            Content = "hello horizon",
            Type = MessageType.Text
        };

        var content = RichMessageContentSerializer.Deserialize(message);

        Assert.Equal(MessageType.Text, content.Type);
        Assert.Equal("hello horizon", content.Text);
        Assert.False(content.SupportsInlinePlayback);
    }

    [Fact]
    public void RichMessageContentSerializer_LegacyBilibiliUrl_BecomesPlayableLinkCard()
    {
        var message = new IMMessage
        {
            Id = "msg-2",
            SenderId = "sender",
            ReceiverId = "receiver",
            Content = "https://www.bilibili.com/video/BV1xx411c7mD",
            Type = MessageType.LinkCard
        };

        var content = RichMessageContentSerializer.Deserialize(message);

        Assert.Equal(MessageType.LinkCard, content.Type);
        Assert.True(content.SupportsInlinePlayback);
        Assert.Contains("player.bilibili.com", content.PlaybackUrl);
        Assert.Equal("https://www.bilibili.com/video/BV1xx411c7mD", content.OriginalUrl);
    }

    [Fact]
    public void SocialLinkParser_ExtractsUrlAndCaption_FromMixedText()
    {
        var success = SocialLinkParser.TryExtractFirstUrl(
            "看看这个 https://www.youtube.com/watch?v=dQw4w9WgXcQ 真不错",
            out var url,
            out var caption);

        Assert.True(success);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", url);
        Assert.DoesNotContain("http", caption);
        Assert.Contains("看看这个", caption);
    }

    [Fact]
    public void SocialLinkParser_DetectsDirectVideo_AndBuildsInlinePlaybackAddress()
    {
        var success = SocialLinkParser.TryParse("https://cdn.example.com/media/demo.mp4", out var parsed);

        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(MessageType.Video, parsed.SuggestedMessageType);
        Assert.True(parsed.SupportsInlinePlayback);
        Assert.StartsWith("file:///", parsed.PlaybackUrl);
        Assert.Contains("inline-video-player.html?src=https%3A%2F%2Fcdn.example.com%2Fmedia%2Fdemo.mp4", parsed.PlaybackUrl);
    }

    [Fact]
    public void SocialLinkParser_BuildDirectVideoPlaybackAddress_UsesFileUriForLocalVideo()
    {
        var localVideoPath = Path.Combine(Path.GetTempPath(), "hundun-inline-preview.mp4");

        var playbackUrl = SocialLinkParser.BuildDirectVideoPlaybackAddress(localVideoPath);

        Assert.StartsWith("file:///", playbackUrl);
        Assert.Contains("inline-video-player.html?src=file%3A%2F%2F%2F", playbackUrl);
        Assert.Contains("hundun-inline-preview.mp4", Uri.UnescapeDataString(playbackUrl));
    }

    [Fact]
    public void ChatMessageItemViewModel_UpdatingPreviewImage_RaisesHasPreviewImageNotification()
    {
        var message = new IMMessage
        {
            Id = "msg-image-1",
            SenderId = "sender",
            ReceiverId = "receiver",
            Content = "https://cdn.example.com/media/demo.png",
            Type = MessageType.Image
        };

        var viewModel = new ChatMessageItemViewModel(message, "receiver", "发送者");
        var hasPreviewImageChanged = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatMessageItemViewModel.HasPreviewImage))
            {
                hasPreviewImageChanged = true;
            }
        };

        var bitmap = (Bitmap)RuntimeHelpers.GetUninitializedObject(typeof(Bitmap));
        var setter = typeof(ChatMessageItemViewModel)
            .GetProperty(nameof(ChatMessageItemViewModel.PreviewImage), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetSetMethod(true);

        Assert.NotNull(setter);
        setter.Invoke(viewModel, new object[] { bitmap });

        Assert.True(viewModel.HasPreviewImage);
        Assert.True(hasPreviewImageChanged);
    }

        [Fact]
        public void SocialLinkPreviewService_ParseOEmbedMetadata_ExtractsTitleAndThumbnail()
        {
                const string json = """
                        {
                            "title": "Launch Trailer",
                            "author_name": "HundunWorld Channel",
                            "provider_name": "YouTube",
                            "thumbnail_url": "https://i.ytimg.com/vi/demo/hqdefault.jpg"
                        }
                        """;

                var metadata = SocialLinkPreviewService.ParseOEmbedMetadata(json, "YouTube");

                Assert.NotNull(metadata);
                Assert.Equal("Launch Trailer", metadata.Title);
                Assert.Equal("HundunWorld Channel", metadata.Description);
                Assert.Equal("YouTube", metadata.SiteName);
                Assert.Equal("https://i.ytimg.com/vi/demo/hqdefault.jpg", metadata.PreviewImageUrl);
        }

        [Fact]
        public void SocialLinkPreviewService_ParseBilibiliMetadata_ExtractsCoverAndDescription()
        {
                const string json = """
                        {
                            "code": 0,
                            "data": {
                                "title": "远征演示",
                                "desc": "这是一次视频卡片预览测试",
                                "pic": "https://i0.hdslb.com/bfs/archive/demo-cover.jpg",
                                "owner": {
                                    "name": "HundunWorld"
                                }
                            }
                        }
                        """;

                var metadata = SocialLinkPreviewService.ParseBilibiliVideoMetadata(json);

                Assert.NotNull(metadata);
                Assert.Equal("远征演示", metadata.Title);
                Assert.Equal("这是一次视频卡片预览测试", metadata.Description);
                Assert.Equal("哔哩哔哩", metadata.SiteName);
                Assert.Equal("https://i0.hdslb.com/bfs/archive/demo-cover.jpg", metadata.PreviewImageUrl);
        }

        [Fact]
        public void SocialLinkPreviewService_ParseJsonLdMetadata_ExtractsVideoSchemaFields()
        {
                const string html = """
                        <html>
                            <head>
                                <script type="application/ld+json">
                                {
                                    "@context": "https://schema.org",
                                    "@type": "VideoObject",
                                    "name": "前线战报",
                                    "description": "这是一次 JSON-LD 预览测试",
                                    "thumbnailUrl": [
                                        "https://cdn.example.com/video-cover.jpg"
                                    ],
                                    "publisher": {
                                        "@type": "Organization",
                                        "name": "优酷"
                                    }
                                }
                                </script>
                            </head>
                        </html>
                        """;

                var metadata = SocialLinkPreviewService.ParseJsonLdMetadata(html, "优酷");

                Assert.NotNull(metadata);
                Assert.Equal("前线战报", metadata.Title);
                Assert.Equal("这是一次 JSON-LD 预览测试", metadata.Description);
                Assert.Equal("优酷", metadata.SiteName);
                Assert.Equal("https://cdn.example.com/video-cover.jpg", metadata.PreviewImageUrl);
        }

        [Fact]
        public void SocialLinkPreviewService_ParseDouyinMetadata_ExtractsCoverAndDescription()
        {
                const string json = """
                        {
                            "item_list": [
                                {
                                    "desc": "抖音热点视频",
                                    "share_info": {
                                        "share_title": "抖音热点视频，快来围观"
                                    },
                                    "author": {
                                        "nickname": "HundunWorld"
                                    },
                                    "video": {
                                        "origin_cover": {
                                            "url_list": [
                                                "https://p3.douyinpic.com/video-cover.jpeg"
                                            ]
                                        }
                                    }
                                }
                            ]
                        }
                        """;

                var metadata = SocialLinkPreviewService.ParseDouyinMetadata(json);

                Assert.NotNull(metadata);
                Assert.Equal("抖音热点视频，快来围观", metadata.Title);
                Assert.Equal("抖音热点视频", metadata.Description);
                Assert.Equal("抖音", metadata.SiteName);
                Assert.Equal("https://p3.douyinpic.com/video-cover.jpeg", metadata.PreviewImageUrl);
        }

        [Fact]
        public void SocialLinkPreviewService_BuildBilibiliMetadataEndpoint_FromBvVideoLink()
        {
                var endpoint = SocialLinkPreviewService.BuildBilibiliMetadataEndpoint("https://www.bilibili.com/video/BV1xx411c7mD");

                Assert.Equal("https://api.bilibili.com/x/web-interface/view?bvid=BV1xx411c7mD", endpoint);
        }

        [Fact]
        public void SocialLinkPreviewService_BuildDouyinMetadataEndpoint_FromVideoLink()
        {
                var endpoint = SocialLinkPreviewService.BuildDouyinMetadataEndpoint("https://www.douyin.com/video/7480000000000000000");

                Assert.Equal("https://www.iesdouyin.com/web/api/v2/aweme/iteminfo/?item_ids=7480000000000000000", endpoint);
        }
}