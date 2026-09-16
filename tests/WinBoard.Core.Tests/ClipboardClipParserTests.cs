using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class ClipboardClipParserTests
{
    [Fact]
    public void ParseFile_SchemaVersion1_ReadsAuthorizationAndSortsByUpdatedAtMs()
    {
        const string json = """
            {
              "version": 1,
              "updatedAtMs": 200,
              "authorized": true,
              "clips": [
                { "id": "a", "text": "old", "type": "text", "updatedAtMs": 100 },
                { "id": "b", "text": "new", "type": "text", "updatedAtMs": 200 }
              ]
            }
            """;

        ParsedClipFile? file = ClipboardClipParser.ParseFile(json);
        Assert.NotNull(file);
        Assert.Equal(1, file.Version);
        Assert.True(file.Authorized);
        Assert.Equal(200, file.UpdatedAtMs);
        Assert.Equal(2, file.Clips.Count);
        Assert.Equal("new", file.Clips[0].Text);
        Assert.Equal("old", file.Clips[1].Text);
        Assert.Equal("text", file.Clips[0].Type);
    }

    [Fact]
    public void ParseFile_AuthorizedFalse_StillParsesClips()
    {
        const string json = """
            {
              "version": 1,
              "updatedAtMs": 1,
              "authorized": false,
              "clips": [ { "id": "x", "text": "secret", "type": "text", "updatedAtMs": 1 } ]
            }
            """;

        ParsedClipFile? file = ClipboardClipParser.ParseFile(json);
        Assert.NotNull(file);
        Assert.False(file.Authorized);
        Assert.Single(file.Clips);
        Assert.Equal("secret", file.Clips[0].Text);
    }

    [Fact]
    public void ParseFile_MissingAuthorized_IsUnauthorized()
    {
        ParsedClipFile? file = ClipboardClipParser.ParseFile("""{ "version": 1, "clips": [] }""");
        Assert.NotNull(file);
        Assert.False(file.Authorized);
        Assert.Empty(file.Clips);
    }

    [Fact]
    public void ParseFile_EmptyClips_ReturnsEmptyList()
    {
        ParsedClipFile? file = ClipboardClipParser.ParseFile(
            """{ "version": 1, "updatedAtMs": 0, "authorized": true, "clips": [] }""");
        Assert.NotNull(file);
        Assert.True(file.Authorized);
        Assert.Empty(file.Clips);
    }

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
        Assert.Null(ClipboardClipParser.ParseFile("not json"));
    }

    [Fact]
    public void Contract_PreferredPath_UsesIntegrationFolder()
    {
        string path = MyClipboardContract.GetPreferredPath();
        Assert.Contains("MyClipBoard", path);
        Assert.Contains("integration", path);
        Assert.EndsWith("clips.json", path);
        Assert.Equal("myclipboard://authorize-winboard", MyClipboardContract.ProtocolUri);
        Assert.Equal(1, MyClipboardContract.SchemaVersion);
    }
}
