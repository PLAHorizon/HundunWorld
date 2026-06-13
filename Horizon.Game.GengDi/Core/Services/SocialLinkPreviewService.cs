using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json.Linq;

namespace Horizon.Game.GengDi.Core.Services
{
    internal sealed class LinkPreviewMetadata
    {
        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string SiteName { get; init; } = string.Empty;

        public string PreviewImageUrl { get; init; } = string.Empty;
    }

    public sealed class SocialLinkPreviewService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Regex BilibiliRegex = new(@"/video/(?<id>BV[0-9A-Za-z]+|av\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DouyinRegex = new(@"/(video|share/video)/(?<id>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex JsonLdScriptRegex = new(@"<script[^>]+type=[""']application/ld\+json[""'][^>]*>(?<content>.*?)</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public async Task<RichMessageContent> CreateFromUrlAsync(string url, string caption = "")
        {
            if (!SocialLinkParser.TryParse(url, out var parsedLink))
            {
                return RichMessageContent.CreateText(caption);
            }

            if (parsedLink.SuggestedMessageType == Enums.MessageType.Image)
            {
                return RichMessageContent.CreateImage(
                    parsedLink.OriginalUrl,
                    text: caption,
                    title: parsedLink.TitleFallback,
                    subtitle: parsedLink.SubtitleFallback,
                    previewImageUrl: parsedLink.OriginalUrl);
            }

            if (parsedLink.SuggestedMessageType == Enums.MessageType.Video)
            {
                return RichMessageContent.CreateVideo(
                    parsedLink.OriginalUrl,
                    parsedLink.PlaybackUrl,
                    text: caption,
                    title: parsedLink.TitleFallback,
                    subtitle: parsedLink.SubtitleFallback,
                    siteName: parsedLink.SiteName,
                    provider: parsedLink.Provider);
            }

            return await BuildLinkCardAsync(parsedLink, caption).ConfigureAwait(false);
        }

        public RichMessageContent CreateImageFromLocalPath(string path, string caption = "", string previewImagePath = "")
        {
            var fileName = System.IO.Path.GetFileName(path);
            return RichMessageContent.CreateImage(
                path,
                caption,
                fileName,
                "本地图片",
                string.IsNullOrWhiteSpace(previewImagePath) ? path : previewImagePath);
        }

        public RichMessageContent CreateVideoFromLocalPath(string path, string caption = "", string previewImagePath = "")
        {
            var fileName = System.IO.Path.GetFileName(path);
            return RichMessageContent.CreateVideo(
                path,
                SocialLinkParser.BuildDirectVideoPlaybackAddress(path),
                caption,
                fileName,
                "本地视频",
                previewImageUrl: previewImagePath,
                siteName: "视频",
                provider: "local-video");
        }

        private async Task<RichMessageContent> BuildLinkCardAsync(SocialLinkParseResult initialLink, string caption)
        {
            var resolvedLink = initialLink;
            var html = string.Empty;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, initialLink.SourceUri);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                var finalUri = response.RequestMessage?.RequestUri ?? initialLink.SourceUri;
                if (!string.Equals(finalUri.ToString(), initialLink.OriginalUrl, StringComparison.OrdinalIgnoreCase) &&
                    SocialLinkParser.TryParse(finalUri.ToString(), out var reparsedLink))
                {
                    resolvedLink = reparsedLink;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            var metaTitle = ExtractMetaContent(html, "og:title")
                ?? ExtractMetaContent(html, "twitter:title")
                ?? ExtractHtmlTitle(html);

            var metaDescription = ExtractMetaContent(html, "og:description")
                ?? ExtractMetaContent(html, "description");

            var metaSiteName = ExtractMetaContent(html, "og:site_name");

            var metaPreviewImageUrl = NormalizeUrl(resolvedLink.SourceUri, ExtractMetaContent(html, "og:image"))
                ?? NormalizeUrl(resolvedLink.SourceUri, ExtractMetaContent(html, "twitter:image"));

            var jsonLdMetadata = ParseJsonLdMetadata(html, resolvedLink.SiteName);

            LinkPreviewMetadata providerMetadata = null;
            if (string.IsNullOrWhiteSpace(metaTitle) ||
                string.IsNullOrWhiteSpace(metaSiteName) ||
                string.IsNullOrWhiteSpace(metaPreviewImageUrl))
            {
                providerMetadata = await TryResolveProviderMetadataAsync(resolvedLink).ConfigureAwait(false);
            }

            var title = providerMetadata?.Title
                ?? metaTitle
                ?? jsonLdMetadata?.Title
                ?? resolvedLink.TitleFallback;

            var description = metaDescription
                ?? jsonLdMetadata?.Description
                ?? providerMetadata?.Description
                ?? resolvedLink.SubtitleFallback;

            var siteName = providerMetadata?.SiteName
                ?? metaSiteName
                ?? jsonLdMetadata?.SiteName
                ?? resolvedLink.SiteName;

            var previewImageUrl = NormalizeUrl(resolvedLink.SourceUri, providerMetadata?.PreviewImageUrl)
                ?? metaPreviewImageUrl
                ?? NormalizeUrl(resolvedLink.SourceUri, jsonLdMetadata?.PreviewImageUrl)
                ?? resolvedLink.PreviewImageUrl;

            return RichMessageContent.CreateLinkCard(
                resolvedLink.OriginalUrl,
                resolvedLink.PlaybackUrl,
                title,
                description,
                siteName,
                previewImageUrl,
                caption,
                resolvedLink.Provider,
                resolvedLink.SupportsInlinePlayback);
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient(SslConfiguration.CreateStandardHandler())
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
        }

        internal static LinkPreviewMetadata ParseOEmbedMetadata(string json, string defaultSiteName)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var payload = JObject.Parse(json);
                var title = payload.Value<string>("title") ?? string.Empty;
                var authorName = payload.Value<string>("author_name") ?? string.Empty;
                var providerName = payload.Value<string>("provider_name") ?? string.Empty;
                var thumbnailUrl = payload.Value<string>("thumbnail_url") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(authorName) &&
                    string.IsNullOrWhiteSpace(thumbnailUrl))
                {
                    return null;
                }

                return new LinkPreviewMetadata
                {
                    Title = title,
                    Description = authorName,
                    SiteName = string.IsNullOrWhiteSpace(providerName) ? defaultSiteName ?? string.Empty : providerName,
                    PreviewImageUrl = thumbnailUrl
                };
            }
            catch
            {
                return null;
            }
        }

        internal static LinkPreviewMetadata ParseJsonLdMetadata(string html, string defaultSiteName)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var matches = JsonLdScriptRegex.Matches(html);
            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                var rawJson = WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    continue;
                }

                try
                {
                    var token = JToken.Parse(rawJson);
                    var metadata = ExtractJsonLdMetadata(token, defaultSiteName);
                    if (metadata != null)
                    {
                        return metadata;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        internal static LinkPreviewMetadata ParseBilibiliVideoMetadata(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var payload = JObject.Parse(json);
                var data = payload["data"];
                if (data == null || data.Type == JTokenType.Null)
                {
                    return null;
                }

                var ownerName = data["owner"]?["name"]?.Value<string>() ?? string.Empty;
                var title = data.Value<string>("title") ?? string.Empty;
                var description = data.Value<string>("desc") ?? ownerName;
                var previewImageUrl = data.Value<string>("pic") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(description) &&
                    string.IsNullOrWhiteSpace(previewImageUrl))
                {
                    return null;
                }

                return new LinkPreviewMetadata
                {
                    Title = title,
                    Description = description,
                    SiteName = "哔哩哔哩",
                    PreviewImageUrl = previewImageUrl
                };
            }
            catch
            {
                return null;
            }
        }

        internal static LinkPreviewMetadata ParseDouyinMetadata(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var payload = JObject.Parse(json);
                var item = payload["item_list"]?[0];
                if (item == null || item.Type == JTokenType.Null)
                {
                    return null;
                }

                var title = item["share_info"]?["share_title"]?.Value<string>()
                    ?? item.Value<string>("desc")
                    ?? string.Empty;

                var description = item.Value<string>("desc")
                    ?? item["author"]?["nickname"]?.Value<string>()
                    ?? string.Empty;

                var previewImageUrl = item["video"]?["origin_cover"]?["url_list"]?[0]?.Value<string>()
                    ?? item["video"]?["cover"]?["url_list"]?[0]?.Value<string>()
                    ?? item["images"]?[0]?["url_list"]?[0]?.Value<string>()
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(description) &&
                    string.IsNullOrWhiteSpace(previewImageUrl))
                {
                    return null;
                }

                return new LinkPreviewMetadata
                {
                    Title = title,
                    Description = description,
                    SiteName = "抖音",
                    PreviewImageUrl = previewImageUrl
                };
            }
            catch
            {
                return null;
            }
        }

        internal static string BuildBilibiliMetadataEndpoint(string originalUrl)
        {
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var match = BilibiliRegex.Match(uri.AbsolutePath);
            if (!match.Success)
            {
                return null;
            }

            var id = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return id.StartsWith("BV", StringComparison.OrdinalIgnoreCase)
                ? $"https://api.bilibili.com/x/web-interface/view?bvid={Uri.EscapeDataString(id)}"
                : $"https://api.bilibili.com/x/web-interface/view?aid={Uri.EscapeDataString(id.Replace("av", string.Empty, StringComparison.OrdinalIgnoreCase))}";
        }

        internal static string BuildDouyinMetadataEndpoint(string originalUrl)
        {
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var match = DouyinRegex.Match(uri.AbsolutePath);
            if (!match.Success)
            {
                return null;
            }

            var id = match.Groups["id"].Value;
            return string.IsNullOrWhiteSpace(id)
                ? null
                : $"https://www.iesdouyin.com/web/api/v2/aweme/iteminfo/?item_ids={Uri.EscapeDataString(id)}";
        }

        private async Task<LinkPreviewMetadata> TryResolveProviderMetadataAsync(SocialLinkParseResult link)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.Provider))
            {
                return null;
            }

            switch (link.Provider.ToLowerInvariant())
            {
                case "youtube":
                    return await TryResolveOEmbedMetadataAsync(link.OriginalUrl, "https://www.youtube.com/oembed?url={0}&format=json", "YouTube").ConfigureAwait(false);

                case "vimeo":
                    return await TryResolveOEmbedMetadataAsync(link.OriginalUrl, "https://vimeo.com/api/oembed.json?url={0}", "Vimeo").ConfigureAwait(false);

                case "bilibili":
                    return await TryResolveJsonMetadataAsync(BuildBilibiliMetadataEndpoint(link.OriginalUrl), ParseBilibiliVideoMetadata).ConfigureAwait(false);

                case "douyin":
                    return await TryResolveJsonMetadataAsync(BuildDouyinMetadataEndpoint(link.OriginalUrl), ParseDouyinMetadata).ConfigureAwait(false);

                default:
                    return null;
            }
        }

        private async Task<LinkPreviewMetadata> TryResolveOEmbedMetadataAsync(string originalUrl, string endpointFormat, string defaultSiteName)
        {
            if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(endpointFormat))
            {
                return null;
            }

            var endpoint = string.Format(endpointFormat, Uri.EscapeDataString(originalUrl));
            return await TryResolveJsonMetadataAsync(endpoint, json => ParseOEmbedMetadata(json, defaultSiteName)).ConfigureAwait(false);
        }

        private async Task<LinkPreviewMetadata> TryResolveJsonMetadataAsync(string endpoint, Func<string, LinkPreviewMetadata> parser)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || parser == null)
            {
                return null;
            }

            try
            {
                using var response = await HttpClient.GetAsync(endpoint).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return parser(json);
            }
            catch
            {
                return null;
            }
        }

        private static LinkPreviewMetadata ExtractJsonLdMetadata(JToken token, string defaultSiteName)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    var childMetadata = ExtractJsonLdMetadata(child, defaultSiteName);
                    if (childMetadata != null)
                    {
                        return childMetadata;
                    }
                }

                return null;
            }

            if (token is not JObject obj)
            {
                return null;
            }

            var graphMetadata = ExtractJsonLdMetadata(obj["@graph"], defaultSiteName);
            if (graphMetadata != null)
            {
                return graphMetadata;
            }

            var title = obj.Value<string>("name")
                ?? obj.Value<string>("headline")
                ?? obj.Value<string>("caption")
                ?? string.Empty;

            var description = obj.Value<string>("description") ?? string.Empty;

            var previewImageUrl = ExtractJsonLdString(obj["thumbnailUrl"])
                ?? ExtractJsonLdImageUrl(obj["image"])
                ?? ExtractJsonLdImageUrl(obj["thumbnail"])
                ?? string.Empty;

            var siteName = obj["publisher"]?["name"]?.Value<string>()
                ?? obj["provider"]?["name"]?.Value<string>()
                ?? defaultSiteName
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(title) ||
                !string.IsNullOrWhiteSpace(description) ||
                !string.IsNullOrWhiteSpace(previewImageUrl))
            {
                return new LinkPreviewMetadata
                {
                    Title = title,
                    Description = description,
                    SiteName = siteName,
                    PreviewImageUrl = previewImageUrl
                };
            }

            foreach (var property in obj.Properties())
            {
                var childMetadata = ExtractJsonLdMetadata(property.Value, defaultSiteName);
                if (childMetadata != null)
                {
                    return childMetadata;
                }
            }

            return null;
        }

        private static string ExtractJsonLdImageUrl(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    var imageUrl = ExtractJsonLdImageUrl(child);
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        return imageUrl;
                    }
                }

                return null;
            }

            return token["url"]?.Value<string>()
                ?? token["contentUrl"]?.Value<string>()
                ?? token["thumbnailUrl"]?.Value<string>();
        }

        private static string ExtractJsonLdString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    var value = ExtractJsonLdString(child);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string ExtractMetaContent(string html, string metaKey)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(metaKey))
            {
                return null;
            }

            var escapedMetaKey = Regex.Escape(metaKey);
            var contentFirstPattern = $"<meta[^>]+content=[\"'](?<content>[^\"']+)[\"'][^>]+(?:property|name)=[\"']{escapedMetaKey}[\"'][^>]*>";
            var propertyFirstPattern = $"<meta[^>]+(?:property|name)=[\"']{escapedMetaKey}[\"'][^>]+content=[\"'](?<content>[^\"']+)[\"'][^>]*>";

            var content = ExtractRegexGroup(html, propertyFirstPattern)
                ?? ExtractRegexGroup(html, contentFirstPattern);

            return string.IsNullOrWhiteSpace(content)
                ? null
                : WebUtility.HtmlDecode(content).Trim();
        }

        private static string ExtractHtmlTitle(string html)
        {
            var title = ExtractRegexGroup(html, @"<title[^>]*>(?<content>.*?)</title>");
            return string.IsNullOrWhiteSpace(title)
                ? null
                : WebUtility.HtmlDecode(Regex.Replace(title, "\\s{2,}", " ")).Trim();
        }

        private static string ExtractRegexGroup(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["content"].Value : null;
        }

        private static string NormalizeUrl(Uri baseUri, string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return null;
            }

            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            return new Uri(baseUri, rawUrl).ToString();
        }
    }
}