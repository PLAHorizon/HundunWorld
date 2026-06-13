using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;
using Newtonsoft.Json.Linq;

namespace Horizon.Game.GengDi.Core.Services
{
    public class MusicStoryService
    {
        private static MusicStoryService _instance;
        private static readonly object _lock = new object();

        public static MusicStoryService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicStoryService();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, MusicStory> _cache = new Dictionary<string, MusicStory>();

        private MusicStoryService()
        {
            _httpClient = new HttpClient(SslConfiguration.CreateTestEnvironmentHandler())
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HorizonGameGengDi/1.0");
        }

        public async Task<MusicStory> GetStoryAsync(Song song)
        {
            if (song == null) return null;

            var cacheKey = song.Id;
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if ((DateTime.UtcNow - cached.FetchedAt).TotalHours < 24)
                {
                    return cached;
                }
            }

            MusicStory story = null;

            try
            {
                story = await FetchFromApiAsync(song);
            }
            catch { }

            if (story == null)
            {
                story = BuildLocalStory(song);
            }

            if (story != null)
            {
                _cache[cacheKey] = story;
            }

            return story;
        }

        private async Task<MusicStory> FetchFromApiAsync(Song song)
        {
            var neteaseId = song.Id;
            if (neteaseId.StartsWith("netease_")) neteaseId = neteaseId.Substring(8);

            try
            {
                var detailUrl = $"/song/detail?ids={neteaseId}";
                var detailJson = await NeteaseMusicApiService.Instance.FetchJsonPublicAsync(detailUrl);
                if (string.IsNullOrEmpty(detailJson)) return null;
                var detailObj = JObject.Parse(detailJson);
                var songs = detailObj["songs"] as JArray;
                if (songs == null || songs.Count == 0) return null;

                var songDetail = songs[0];
                var artistName = string.Join("/", songDetail["ar"]?.Select(a => a["name"]?.ToString()) ?? Array.Empty<string>());
                if (string.IsNullOrWhiteSpace(artistName)) artistName = song.ArtistName;
                var albumName = songDetail["al"]?["name"]?.ToString() ?? song.AlbumName;
                var albumPicUrl = songDetail["al"]?["picUrl"]?.ToString() ?? "";
                var publishTime = songDetail["publishTime"]?.Value<long>() ?? 0;
                var publishDate = publishTime > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(publishTime).ToString("yyyy年MM月dd日")
                    : "未知";

                var artistId = songDetail["ar"]?.FirstOrDefault()?["id"]?.ToString() ?? "";

                string artistDesc = "";
                if (!string.IsNullOrEmpty(artistId))
                {
                    try
                    {
                        var descUrl = $"/artist/desc?id={artistId}";
                        var descJson = await NeteaseMusicApiService.Instance.FetchJsonPublicAsync(descUrl);
                        if (!string.IsNullOrEmpty(descJson))
                        {
                            var descObj = JObject.Parse(descJson);
                            var briefDesc = descObj["briefDesc"]?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(briefDesc))
                            {
                                artistDesc = briefDesc;
                            }
                        }
                    }
                    catch { }
                }

                var story = new MusicStory
                {
                    SongId = song.Id,
                    SongTitle = songDetail["name"]?.ToString() ?? song.Title,
                    ArtistName = artistName,
                    Summary = $"《{songDetail["name"]?.ToString() ?? song.Title}》由{artistName}演唱，收录于专辑《{albumName}》，发行于{publishDate}。",
                    Era = publishDate,
                    Genre = ""
                };

                story.Sections.Add(new MusicStorySection
                {
                    Type = "header",
                    Content = $"《{story.SongTitle}》",
                    ImageUrl = albumPicUrl,
                    ImageCaption = $"专辑：{albumName}"
                });

                story.Sections.Add(new MusicStorySection
                {
                    Type = "info",
                    Content = $"🎤 演唱：{artistName}\n💿 专辑：{albumName}\n📅 发行：{publishDate}\n⏱ 时长：{song.Duration:mm\\:ss}"
                });

                if (!string.IsNullOrWhiteSpace(artistDesc))
                {
                    story.Sections.Add(new MusicStorySection
                    {
                        Type = "text",
                        Content = artistDesc.Length > 500 ? artistDesc.Substring(0, 500) + "..." : artistDesc
                    });
                }

                return story;
            }
            catch
            {
                return null;
            }
        }

        private MusicStory BuildLocalStory(Song song)
        {
            var story = new MusicStory
            {
                SongId = song.Id,
                SongTitle = song.Title,
                ArtistName = song.ArtistName,
                Summary = $"《{song.Title}》由{song.DisplayArtist}演唱。",
                Era = "",
                Genre = ""
            };

            story.Sections.Add(new MusicStorySection
            {
                Type = "header",
                Content = $"《{song.Title}》"
            });

            story.Sections.Add(new MusicStorySection
            {
                Type = "info",
                Content = $"🎤 演唱：{song.DisplayArtist}\n💿 专辑：{song.DisplayAlbum}\n⏱ 时长：{song.Duration:mm\\:ss}"
            });

            var knownStories = GetKnownStory(song.Title, song.ArtistName);
            if (knownStories != null && knownStories.Count > 0)
            {
                story.Summary = knownStories[0].Content;
                foreach (var section in knownStories)
                {
                    story.Sections.Add(section);
                }
            }
            else
            {
                story.Sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = $"这首歌曲由{song.DisplayArtist}倾情演绎，旋律优美动人，令人回味无穷。"
                });
            }

            return story;
        }

        private List<MusicStorySection> GetKnownStory(string title, string artist)
        {
            var sections = new List<MusicStorySection>();

            if (title.Contains("晴天") && artist.Contains("周杰伦"))
            {
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "《晴天》是周杰伦2003年发行的经典歌曲，收录于专辑《叶惠美》中。这首歌以清新的校园风格和淡淡的忧伤打动了无数听众，成为了华语乐坛的标志性作品之一。"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "quote",
                    Content = "故事的小黄花，从出生那年就飘着",
                    QuoteAuthor = "周杰伦《晴天》"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "歌曲以简单的吉他伴奏开篇，描绘了青春期懵懂的爱情与遗憾。周杰伦曾表示，这首歌的灵感来源于学生时代的回忆，那种纯真的感情和无法挽回的时光。"
                });
            }
            else if (title.Contains("稻香") && artist.Contains("周杰伦"))
            {
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "《稻香》收录于2008年专辑《魔杰座》，是周杰伦为汶川地震创作的励志歌曲。歌曲以温暖的田园风光为意象，鼓励人们在困境中保持希望。"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "quote",
                    Content = "还记得你说家是唯一的城堡，随着稻香河流继续奔跑",
                    QuoteAuthor = "周杰伦《稻香》"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "2008年5月12日汶川大地震后，周杰伦深受触动，创作了这首歌。他希望通过音乐传递温暖和力量，告诉人们即使面对灾难，也要珍惜生活中的美好。"
                });
            }
            else if (title.Contains("光年之外") && artist.Contains("邓紫棋"))
            {
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "《光年之外》是邓紫棋为电影《太空旅客》演唱的中文主题曲，发行于2016年。歌曲以宇宙和星际为背景，表达了跨越时空的深情。"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "quote",
                    Content = "缘分让我们相遇乱世以外，命运却要我们危难中相爱",
                    QuoteAuthor = "邓紫棋《光年之外》"
                });
            }
            else if (title.Contains("起风了"))
            {
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "《起风了》原曲为日本歌手高橋優的《ヤキモチ》，中文版由买辣椒也用券填词演唱，后经多位歌手翻唱而广为人知。歌曲以风为意象，表达了对青春和爱情的追忆。"
                });
                sections.Add(new MusicStorySection
                {
                    Type = "quote",
                    Content = "我曾难自拔于世界之大，也沉溺于其中梦话",
                    QuoteAuthor = "《起风了》"
                });
            }
            else if (title.Contains("星辰大海"))
            {
                sections.Add(new MusicStorySection
                {
                    Type = "text",
                    Content = "《星辰大海》是黄霄雲的代表作，以磅礴的旋律和深情的歌词，描绘了对梦想和远方的执着追求，成为近年来华语乐坛的热门歌曲。"
                });
            }
            else
            {
                return null;
            }

            return sections;
        }
    }
}
