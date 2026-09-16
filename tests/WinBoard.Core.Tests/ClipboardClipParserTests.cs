using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class ClipboardClipParserTests
{
    [Fact]
    public void Parse_StandardClipsArray_ReadsTextAndSortsNewestFirst()
    {
        const string json = """
            {
              "clips": [
                { "id": "a", "text": "old", "timestamp": "2026-01-01T00:00:00Z" },
                { "id": "b", "text": "new", "timestamp": "2026-09-16T12:00:00Z" }
              ]
            }
            """;

        IReadOnlyList<ParsedClip> clips = ClipboardClipParser.Parse(json, 12);
        Assert.Equal(2, clips.Count);
        Assert.Equal("new", clips[0].Text);
        Assert.Equal("old", clips[1].Text);
    }

    [Fact]
    public void Parse_RootArrayAndContentAlias_Works()
    {
        const string json = """[{ "Content": "hello" }, "plain"]""";
        IReadOnlyList<ParsedClip> clips = ClipboardClipParser.Parse(json, 12);
        Assert.Equal(2, clips.Count);
        Assert.Contains(clips, c => c.Text == "hello");
        Assert.Contains(clips, c => c.Text == "plain");
    }

    [Fact]
    public void Parse_ItemsAlias_Works()
    {
        const string json = """{ "items": [ { "content": "clip" } ] }""";
        IReadOnlyList<ParsedClip> clips = ClipboardClipParser.Parse(json, 4);
        Assert.Single(clips);
        Assert.Equal("clip", clips[0].Text);
    }

    [Fact]
    public void LooksLikeJson_DetectsObjectAndArray()
    {
        Assert.True(ClipboardClipParser.LooksLikeJson("  {\"clips\":[]}"));
        Assert.True(ClipboardClipParser.LooksLikeJson("[]"));
        Assert.False(ClipboardClipParser.LooksLikeJson("not json"));
    }

    [Fact]
    public void Parse_EmptyClipsArray_ReturnsEmpty()
    {
        IReadOnlyList<ParsedClip> clips = ClipboardClipParser.Parse("""{ "clips": [] }""", 12);
        Assert.Empty(clips);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        Assert.Empty(ClipboardClipParser.Parse("   ", 12));
        Assert.Empty(ClipboardClipParser.Parse("", 12));
    }
}
