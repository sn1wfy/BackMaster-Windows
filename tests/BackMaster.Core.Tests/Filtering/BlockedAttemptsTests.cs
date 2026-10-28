using BackMaster.Core.Filtering;
using Xunit;

namespace BackMaster.Core.Tests.Filtering;

public class BlockMatcherTests
{
    private readonly BlockMatcher _m = new(new[] { "chatgpt.com", "https://www.claude.ai/new" }, new[] { "poki.com", "chatgpt.com" });

    [Theory]
    [InlineData("chatgpt.com", "chatgpt.com", true)]
    [InlineData("WWW.ChatGPT.com.", "chatgpt.com", true)]
    [InlineData("claude.ai", "claude.ai", true)]
    [InlineData("www.claude.ai", "claude.ai", true)]
    [InlineData("poki.com", "poki.com", false)]
    public void Matches_exactly_the_names_in_the_hosts_file(string query, string domain, bool isAi)
    {
        Assert.Equal(new BlockedSite(domain, isAi), _m.Match(query));
    }

    [Theory]
    [InlineData("cdn.chatgpt.com")]   // not in the hosts file, so not blocked
    [InlineData("notchatgpt.com")]
    [InlineData("google.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_names_that_are_not_blocked(string? query)
    {
        Assert.Null(_m.Match(query));
    }

    [Fact]
    public void Empty_matches_nothing()
    {
        Assert.Equal(0, BlockMatcher.Empty.Count);
        Assert.Null(BlockMatcher.Empty.Match("chatgpt.com"));
    }
}

public class AttemptFeedTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-29T07:15:00Z");
    private static readonly BlockedSite Gpt = new("chatgpt.com", true);
    private static readonly BlockedSite Poki = new("poki.com", false);

    [Fact]
    public void Repeated_lookups_of_one_site_are_reported_once_per_quiet_period()
    {
        var feed = new AttemptFeed(TimeSpan.FromSeconds(60));
        Assert.NotNull(feed.Record(Gpt, T0));
        Assert.Null(feed.Record(Gpt, T0.AddSeconds(1)));
        Assert.Null(feed.Record(Gpt, T0.AddSeconds(59)));
        Assert.NotNull(feed.Record(Poki, T0.AddSeconds(2)));
        Assert.NotNull(feed.Record(Gpt, T0.AddSeconds(60)));
        Assert.Equal(3, feed.Cursor);
    }

    [Fact]
    public void After_returns_only_newer_attempts()
    {
        var feed = new AttemptFeed();
        feed.Record(Gpt, T0);
        feed.Record(Poki, T0);
        Assert.Equal(new[] { "poki.com" }, feed.After(1).Select(a => a.Domain));
        Assert.Empty(feed.After(2));
    }

    [Fact]
    public void A_cursor_from_before_a_service_restart_sees_the_new_attempts()
    {
        var feed = new AttemptFeed();
        feed.Record(Gpt, T0);
        Assert.Single(feed.After(500));
    }

    [Fact]
    public void Only_the_most_recent_attempts_are_kept()
    {
        var feed = new AttemptFeed(TimeSpan.Zero, capacity: 3);
        for (int i = 0; i < 10; i++) feed.Record(Gpt, T0.AddSeconds(i));
        Assert.Equal(new long[] { 8, 9, 10 }, feed.After(0).Select(a => a.Id));
    }
}
