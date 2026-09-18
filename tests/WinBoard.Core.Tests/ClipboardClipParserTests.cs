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
    public void ParseFile_AuthorizedFalse_ReturnsEmptyClips()
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
        Assert.Empty(file.Clips);
    }

    [Fact]
    public void ParseFile_RejectsAlternatePropertyNames()
    {
        Assert.Null(ClipboardClipParser.ParseFile(
            """{ "Version": 1, "updatedAtMs": 0, "authorized": true, "clips": [] }"""));
        Assert.Null(ClipboardClipParser.ParseFile(
            """{ "version": 1, "updatedAtMs": 0, "Authorized": true, "clips": [] }"""));
        Assert.Null(ClipboardClipParser.ParseFile(
            """{ "version": 1, "updatedAtMs": 0, "authorized": true, "items": [] }"""));
        Assert.Empty(ClipboardClipParser.Parse(
            """{ "version": 1, "updatedAtMs": 0, "authorized": true, "clips": [ { "Id": "a", "text": "x", "type": "text", "updatedAtMs": 1 } ] }""",
            12));
        Assert.Empty(ClipboardClipParser.Parse(
            """{ "version": 1, "updatedAtMs": 0, "authorized": true, "clips": [ { "id": "a", "content": "x", "type": "text", "updatedAtMs": 1 } ] }""",
            12));
    }

    [Fact]
    public void ParseFile_RejectsRootArrayAndMissingRequiredFields()
    {
        Assert.Null(ClipboardClipParser.ParseFile("""[{ "id": "a", "text": "x", "type": "text", "updatedAtMs": 1 }]"""));
        Assert.Null(ClipboardClipParser.ParseFile("""{ "clips": [] }"""));
        Assert.Null(ClipboardClipParser.ParseFile("not json"));
        Assert.Null(ClipboardClipParser.ParseFile("   "));
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
    public void LooksLikeJson_DetectsObjectAndArray()
    {
        Assert.True(ClipboardClipParser.LooksLikeJson("  {\"clips\":[]}"));
        Assert.True(ClipboardClipParser.LooksLikeJson("[]"));
        Assert.False(ClipboardClipParser.LooksLikeJson("not json"));
    }

    [Fact]
    public void Contract_PreferredPath_IsExactMyClipBoardCasing()
    {
        string path = MyClipboardContract.GetPreferredPath();
        string[] parts = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("MyClipBoard", parts);
        Assert.DoesNotContain("MyClipboard", parts);
        Assert.Contains("integration", parts);
        Assert.Equal("clips.json", parts[^1]);
        Assert.Equal("myclipboard://authorize-winboard", MyClipboardContract.ProtocolUri);
        Assert.Equal(1, MyClipboardContract.SchemaVersion);
    }

    [Fact]
    public void ExistsWithExactCasing_RejectsWrongFolderSpelling()
    {
        string root = Path.Combine(Path.GetTempPath(), "winboard-mcb-" + Guid.NewGuid().ToString("n"));
        try
        {
            string exactDir = Path.Combine(root, "MyClipBoard", "integration");
            Directory.CreateDirectory(exactDir);
            string exactFile = Path.Combine(exactDir, "clips.json");
            File.WriteAllText(exactFile, """{ "version": 1, "updatedAtMs": 0, "authorized": true, "clips": [] }""");

            Assert.True(MyClipboardContract.ExistsWithExactCasing(exactFile));
            Assert.NotNull(MyClipboardContract.TryWalkContractSuffix(root));

            string wrong = Path.Combine(root, "MyClipboard", "integration", "clips.json");
            Assert.False(MyClipboardContract.HasExactContractSuffix(wrong));
            Assert.False(MyClipboardContract.ExistsWithExactCasing(wrong));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void HasExactContractSuffix_IgnoresLocalAppDataPrefixCasing()
    {
        // Windows: GetFolderPath(LocalApplicationData) often disagrees with on-disk
        // Users/Frank/AppData/Local casing. Detection must still succeed when the
        // MyClipBoard\integration\clips.json leaf segments match ordinally.
        Assert.True(MyClipboardContract.HasExactContractSuffix(
            @"C:\users\frank\appdata\local\MyClipBoard\integration\clips.json"));
        Assert.True(MyClipboardContract.HasExactContractSuffix(
            @"C:\Users\Frank\AppData\Local\MyClipBoard\integration\clips.json"));
        Assert.True(MyClipboardContract.HasExactContractSuffix(
            @"C:\USERS\FRANK\APPDATA\LOCAL\MyClipBoard\integration\clips.json"));
        Assert.False(MyClipboardContract.HasExactContractSuffix(
            @"C:\Users\Frank\AppData\Local\MyClipboard\integration\clips.json"));
        Assert.False(MyClipboardContract.HasExactContractSuffix(
            @"C:\Users\Frank\AppData\Local\MyClipBoard\Integration\clips.json"));
        Assert.False(MyClipboardContract.HasExactContractSuffix(
            @"C:\Users\Frank\AppData\Local\MyClipBoard\integration\Clips.json"));
    }

    [Fact]
    public void ExistsWithExactCasing_FindsFileWhenPrefixDirectoryCasingWouldFailFullWalk()
    {
        string root = Path.Combine(Path.GetTempPath(), "winboard-mcb-prefix-" + Guid.NewGuid().ToString("n"));
        try
        {
            // Real tree. A full ordinal walk from a differently-cased prefix
            // (Users vs users) is exactly the Windows bug; we only walk the
            // three contract segments from this base directory.
            string baseDir = Path.Combine(root, "AppData", "Local");
            string exactDir = Path.Combine(baseDir, "MyClipBoard", "integration");
            Directory.CreateDirectory(exactDir);
            string exactFile = Path.Combine(exactDir, "clips.json");
            File.WriteAllText(exactFile, """{ "version": 1, "updatedAtMs": 0, "authorized": false, "clips": [] }""");

            Assert.True(MyClipboardContract.ExistsWithExactCasing(exactFile));
            string? walked = MyClipboardContract.TryWalkContractSuffix(baseDir);
            Assert.NotNull(walked);
            Assert.EndsWith(
                Path.Combine("MyClipBoard", "integration", "clips.json"),
                walked,
                StringComparison.Ordinal);

            string mixedPrefix = Path.Combine(
                root, "appdata", "local", "MyClipBoard", "integration", "clips.json");
            Assert.True(MyClipboardContract.HasExactContractSuffix(mixedPrefix));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Classify_ExistingUnreadableFile_IsInvalidNotMissing()
    {
        Assert.Equal(ClipFileStatus.Missing, MyClipboardContract.Classify(false, null));
        Assert.Equal(ClipFileStatus.Invalid, MyClipboardContract.Classify(true, null));
        Assert.Equal(
            ClipFileStatus.Unauthorized,
            MyClipboardContract.Classify(true, new ParsedClipFile(1, 0, false, [])));
        Assert.Equal(
            ClipFileStatus.Empty,
            MyClipboardContract.Classify(true, new ParsedClipFile(1, 0, true, [])));
    }
}
