using BackMaster.Core.Filtering;
using Xunit;

namespace BackMaster.Core.Tests.Filtering;

public class DomainNameTests
{
    [Theory]
    [InlineData("chatgpt.com", "chatgpt.com")]
    [InlineData("  ChatGPT.COM  ", "chatgpt.com")]
    [InlineData("https://www.chatgpt.com/c/123?x=1", "chatgpt.com")]
    [InlineData("http://user@claude.ai:443/new", "claude.ai")]
    [InlineData("gemini.google.com.", "gemini.google.com")]
    [InlineData("*.poki.com", "poki.com")]
    [InlineData("www.com", "www.com")]
    [InlineData("пример.бг", "xn--e1afmkfd.xn--90ae")]
    public void Normalizes_what_teachers_type(string input, string expected)
    {
        Assert.Equal(expected, DomainName.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("192.168.1.1")]
    [InlineData("not a domain")]
    [InlineData("-bad-.com")]
    [InlineData("a..com")]
    public void Rejects_unusable_names(string? input)
    {
        Assert.Null(DomainName.Normalize(input));
    }
}

public class HostsSectionTests
{
    private const string Stock = "# Copyright (c) Microsoft Corp.\r\n#\r\n127.0.0.1 printer.local\r\n";

    [Fact]
    public void Adds_a_marked_section_for_each_domain_and_www()
    {
        var text = HostsSection.Apply(Stock, new[] { "chatgpt.com", "www.example.org" });
        Assert.StartsWith(Stock, text);
        Assert.Contains(HostsSection.BeginMarker + "\r\n", text);
        Assert.Contains("0.0.0.0 chatgpt.com\r\n", text);
        Assert.Contains(":: chatgpt.com\r\n", text);
        Assert.Contains("0.0.0.0 www.chatgpt.com\r\n", text);
        Assert.Contains("0.0.0.0 www.example.org\r\n", text);
        Assert.DoesNotContain("www.www.", text);
        Assert.EndsWith(HostsSection.EndMarker + "\r\n", text);
    }

    [Fact]
    public void Applying_twice_changes_nothing()
    {
        var once = HostsSection.Apply(Stock, new[] { "chatgpt.com" });
        Assert.Equal(once, HostsSection.Apply(once, new[] { "chatgpt.com" }));
    }

    [Fact]
    public void Replacing_the_list_keeps_user_entries()
    {
        var a = HostsSection.Apply(Stock, new[] { "chatgpt.com" });
        var b = HostsSection.Apply(a + "10.0.0.5 nas.local\r\n", new[] { "claude.ai" });
        Assert.DoesNotContain("chatgpt.com", b);
        Assert.Contains("0.0.0.0 claude.ai", b);
        Assert.Contains("127.0.0.1 printer.local", b);
        Assert.Contains("10.0.0.5 nas.local", b);
        Assert.Single(b.Split(HostsSection.BeginMarker)[1..]);
    }

    [Fact]
    public void An_empty_list_restores_the_original_file_exactly()
    {
        var blocked = HostsSection.Apply(Stock, new[] { "chatgpt.com" });
        Assert.Equal(Stock, HostsSection.Apply(blocked, Array.Empty<string>()));
    }

    [Fact]
    public void An_empty_list_leaves_a_file_without_a_section_untouched()
    {
        const string noTrailingNewline = "127.0.0.1 a.local";
        Assert.Same(noTrailingNewline, HostsSection.Apply(noTrailingNewline, Array.Empty<string>()));
    }

    [Fact]
    public void A_file_without_a_trailing_newline_gets_one_before_the_section()
    {
        var text = HostsSection.Apply("127.0.0.1 a.local", new[] { "poki.com" });
        Assert.StartsWith("127.0.0.1 a.local\r\n" + HostsSection.BeginMarker, text);
    }

    [Fact]
    public void Unix_line_endings_are_kept()
    {
        var text = HostsSection.Apply("127.0.0.1 a.local\n", new[] { "poki.com" });
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void An_unterminated_section_is_removed_to_the_end()
    {
        var damaged = Stock + HostsSection.BeginMarker + "\r\n0.0.0.0 old.com\r\n";
        Assert.Equal(Stock, HostsSection.Apply(damaged, Array.Empty<string>()));
    }

    [Fact]
    public void An_empty_hosts_file_works()
    {
        var text = HostsSection.Apply("", new[] { "poki.com" });
        Assert.StartsWith(HostsSection.BeginMarker, text);
        Assert.Equal("", HostsSection.Apply(text, Array.Empty<string>()));
    }
}
