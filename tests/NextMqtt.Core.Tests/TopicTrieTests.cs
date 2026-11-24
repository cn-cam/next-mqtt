using NextMqtt.Core.Topics;

namespace NextMqtt.Core.Tests;

public class TopicTrieTests
{
    [Fact]
    public void Match_returns_all_matching_filters()
    {
        var trie = new TopicTrie<string>();
        trie.Add("sport/tennis/#", "wildcard");
        trie.Add("sport/+/player1", "single");
        trie.Add("sport/tennis/player1", "exact");
        trie.Add("other/#", "other");
        trie.Add("#", "all");

        var matches = trie.Match("sport/tennis/player1");

        Assert.Equal(4, matches.Count);
        Assert.Contains("wildcard", matches);
        Assert.Contains("single", matches);
        Assert.Contains("exact", matches);
        Assert.Contains("all", matches);
    }

    [Fact]
    public void Match_does_not_apply_wildcards_to_dollar_topics()
    {
        var trie = new TopicTrie<string>();
        trie.Add("#", "all");
        trie.Add("+/load", "single");
        trie.Add("$SYS/#", "system");

        var matches = trie.Match("$SYS/broker/load");

        Assert.Equal(new[] { "system" }, matches);
    }

    [Fact]
    public void Search_resolves_retained_topics()
    {
        var trie = new TopicTrie<string>();
        trie.Add("a/b/c", "abc");
        trie.Add("a/b/d", "abd");
        trie.Add("a/c", "ac");

        Assert.Equal(new[] { "abc", "abd" }, trie.Search("a/b/#").OrderBy(x => x));
        Assert.Equal(new[] { "abc", "abd" }, trie.Search("a/+/+").OrderBy(x => x));
        Assert.Equal(new[] { "ac" }, trie.Search("a/+").OrderBy(x => x));
        Assert.Equal(new[] { "abc", "abd", "ac" }, trie.Search("a/#").OrderBy(x => x));
    }

    [Fact]
    public void Search_with_hash_matches_parent_topic()
    {
        var trie = new TopicTrie<string>();
        trie.Add("sport", "parent");
        trie.Add("sport/tennis", "child");

        Assert.Equal(new[] { "child", "parent" }, trie.Search("sport/#").OrderBy(x => x));
    }

    [Fact]
    public void Search_does_not_match_dollar_topics_with_wildcards()
    {
        var trie = new TopicTrie<string>();
        trie.Add("$SYS/load", "system");
        trie.Add("app/load", "app");

        Assert.Equal(new[] { "app" }, trie.Search("+/load"));
        Assert.Equal(new[] { "app" }, trie.Search("#"));
        Assert.Equal(new[] { "system" }, trie.Search("$SYS/#"));
    }

    [Fact]
    public void Remove_prunes_empty_branches()
    {
        var trie = new TopicTrie<string>();
        trie.Add("a/b/c", "value");

        Assert.Equal(1, trie.Count);
        Assert.True(trie.Remove("a/b/c", "value"));
        Assert.Equal(0, trie.Count);
        Assert.Empty(trie.Match("a/b/c"));
        Assert.False(trie.Remove("a/b/c", "value"));
    }

    [Fact]
    public void Remove_keeps_other_values_on_the_same_filter()
    {
        var trie = new TopicTrie<int>();
        trie.Add("a", 1);
        trie.Add("a", 2);

        Assert.True(trie.Remove("a", 1));
        Assert.Equal(new[] { 2 }, trie.Match("a"));
        Assert.Equal(1, trie.Count);
    }

    [Fact]
    public void Duplicate_values_are_all_delivered()
    {
        var trie = new TopicTrie<string>();
        trie.Add("a/+", "same");
        trie.Add("a/+", "same");

        Assert.Equal(new[] { "same", "same" }, trie.Match("a/b"));
    }

    [Fact]
    public void Clear_resets_everything()
    {
        var trie = new TopicTrie<string>();
        trie.Add("a/b", "x");
        trie.Add("c/#", "y");

        trie.Clear();

        Assert.Equal(0, trie.Count);
        Assert.Empty(trie.Match("a/b"));
        Assert.Empty(trie.ToList());
    }
}
