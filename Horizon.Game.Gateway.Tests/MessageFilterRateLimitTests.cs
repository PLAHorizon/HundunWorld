using Horizon.Orleans.Grains;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 消息速率限制器、敏感词过滤器 单元测试
    /// </summary>
    public class MessageFilterRateLimitTests
    {
        #region MessageRateLimiter Tests

        [Fact]
        public void MessageRateLimiter_NotRateLimited_Initially()
        {
            var limiter = new MessageRateLimiter();
            Assert.False(limiter.IsRateLimited(1));
        }

        [Fact]
        public void MessageRateLimiter_RateLimited_AfterExceedingThreshold()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 3, windowSeconds: 60);
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            Assert.True(limiter.IsRateLimited(1));
        }

        [Fact]
        public void MessageRateLimiter_RemainingMessages_Count()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 5, windowSeconds: 60);
            Assert.Equal(5, limiter.GetRemainingMessages(1));
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            Assert.Equal(3, limiter.GetRemainingMessages(1));
        }

        [Fact]
        public void MessageRateLimiter_ResetPlayer()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 2, windowSeconds: 60);
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            Assert.True(limiter.IsRateLimited(1));
            limiter.Reset(1);
            Assert.False(limiter.IsRateLimited(1));
            Assert.Equal(2, limiter.GetRemainingMessages(1));
        }

        [Fact]
        public void MessageRateLimiter_ResetAll()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 2, windowSeconds: 60);
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            limiter.RecordMessage(2);
            limiter.RecordMessage(2);
            Assert.True(limiter.IsRateLimited(1));
            Assert.True(limiter.IsRateLimited(2));
            limiter.ResetAll();
            Assert.False(limiter.IsRateLimited(1));
            Assert.False(limiter.IsRateLimited(2));
        }

        [Fact]
        public void MessageRateLimiter_DifferentPlayers_TrackedIndependently()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 2, windowSeconds: 60);
            limiter.RecordMessage(1);
            limiter.RecordMessage(1);
            Assert.True(limiter.IsRateLimited(1));
            Assert.False(limiter.IsRateLimited(2));
        }

        [Fact]
        public void MessageRateLimiter_WindowExpiry_AllowsNewMessages()
        {
            // Use a very short window (1 second) to test expiry
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 1, windowSeconds: 1);
            limiter.RecordMessage(1);
            Assert.True(limiter.IsRateLimited(1));
            // Wait for window to expire
            Thread.Sleep(1100);
            Assert.False(limiter.IsRateLimited(1));
        }

        [Fact]
        public void MessageRateLimiter_CustomWindowSize()
        {
            var limiter = new MessageRateLimiter(maxMessagesPerWindow: 100, windowSeconds: 300);
            for (int i = 0; i < 100; i++)
                limiter.RecordMessage(1);
            Assert.True(limiter.IsRateLimited(1));
            Assert.Equal(0, limiter.GetRemainingMessages(1));
        }

        #endregion

        #region SensitiveWordFilter Tests

        [Fact]
        public void SensitiveWordFilter_EmptyText_ReturnsFalse()
        {
            var filter = new SensitiveWordFilter();
            Assert.False(filter.ContainsSensitiveWord(""));
            Assert.False(filter.ContainsSensitiveWord(null!));
        }

        [Fact]
        public void SensitiveWordFilter_TextWithNoSensitiveWords()
        {
            var filter = new SensitiveWordFilter(new[] { "badword" });
            Assert.False(filter.ContainsSensitiveWord("This is a clean message"));
        }

        [Fact]
        public void SensitiveWordFilter_TextContainingSensitiveWord()
        {
            var filter = new SensitiveWordFilter(new[] { "badword" });
            Assert.True(filter.ContainsSensitiveWord("This contains badword in it"));
        }

        [Fact]
        public void SensitiveWordFilter_FilterReplacesWithAsterisks()
        {
            var filter = new SensitiveWordFilter(new[] { "bad" });
            var result = filter.FilterText("This is bad text");
            Assert.Equal("This is *** text", result);
        }

        [Fact]
        public void SensitiveWordFilter_CaseInsensitive()
        {
            var filter = new SensitiveWordFilter(new[] { "spam" });
            Assert.True(filter.ContainsSensitiveWord("This is SPAM"));
            Assert.True(filter.ContainsSensitiveWord("This is Spam"));
            Assert.True(filter.ContainsSensitiveWord("This is spam"));
        }

        [Fact]
        public void SensitiveWordFilter_AddAndRemoveWords()
        {
            var filter = new SensitiveWordFilter(new[] { "word1" });
            Assert.True(filter.ContainsSensitiveWord("word1 present"));
            filter.AddWord("word2");
            Assert.True(filter.ContainsSensitiveWord("word2 present"));
            filter.RemoveWord("word1");
            Assert.False(filter.ContainsSensitiveWord("word1 present"));
        }

        [Fact]
        public void SensitiveWordFilter_GetMatchedWords()
        {
            var filter = new SensitiveWordFilter(new[] { "bad", "evil", "nasty" });
            var matches = filter.GetMatchedWords("This is bad and evil text");
            Assert.Contains("bad", matches);
            Assert.Contains("evil", matches);
            Assert.DoesNotContain("nasty", matches);
        }

        [Fact]
        public void SensitiveWordFilter_WordCount()
        {
            var filter = new SensitiveWordFilter(new[] { "a", "b", "c" });
            Assert.Equal(3, filter.WordCount);
            filter.AddWord("d");
            Assert.Equal(4, filter.WordCount);
            filter.RemoveWord("a");
            Assert.Equal(3, filter.WordCount);
        }

        [Fact]
        public void SensitiveWordFilter_CustomConstructor()
        {
            var filter = new SensitiveWordFilter(new[] { "custom1", "custom2" });
            Assert.Equal(2, filter.WordCount);
            Assert.True(filter.ContainsSensitiveWord("has custom1"));
            // Default words should not be present
            Assert.False(filter.ContainsSensitiveWord("spam"));
        }

        [Fact]
        public void SensitiveWordFilter_MultipleSensitiveWordsInText()
        {
            var filter = new SensitiveWordFilter(new[] { "bad", "evil" });
            var result = filter.FilterText("bad and evil stuff");
            Assert.Equal("*** and **** stuff", result);
        }

        [Fact]
        public void SensitiveWordFilter_FilterText_EmptyText()
        {
            var filter = new SensitiveWordFilter(new[] { "bad" });
            Assert.Equal("", filter.FilterText(""));
            Assert.Null(filter.FilterText(null!));
        }

        [Fact]
        public void SensitiveWordFilter_GetMatchedWords_EmptyText()
        {
            var filter = new SensitiveWordFilter(new[] { "bad" });
            var matches = filter.GetMatchedWords("");
            Assert.Empty(matches);
        }

        [Fact]
        public void SensitiveWordFilter_DefaultConstructor_HasWords()
        {
            var filter = new SensitiveWordFilter();
            Assert.True(filter.WordCount > 0);
            Assert.True(filter.ContainsSensitiveWord("this is spam"));
        }

        #endregion
    }
}
