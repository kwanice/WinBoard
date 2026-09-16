using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class EmojiCatalogTests
{
    [Fact]
    public void Categories_CoverGboardGroups()
    {
        string[] ids = EmojiCatalog.Categories.Select(c => c.Id).ToArray();
        Assert.Contains("smileys", ids);
        Assert.Contains("people", ids);
        Assert.Contains("nature", ids);
        Assert.Contains("food", ids);
        Assert.Contains("activities", ids);
        Assert.Contains("travel", ids);
        Assert.Contains("objects", ids);
        Assert.Contains("symbols", ids);
        Assert.Contains("flags", ids);
        Assert.True(EmojiCatalog.All.Count >= 400, $"Catalog has {EmojiCatalog.All.Count} unique glyphs");
    }

    [Fact]
    public void Search_Coeur_FindsRedHeart()
    {
        IReadOnlyList<EmojiItem> hits = EmojiCatalog.Search("coeur");
        Assert.Contains(hits, e => e.Glyph.Contains('❤') || e.Glyph == "❤️");
    }

    [Fact]
    public void Search_France_FindsFrenchFlag()
    {
        IReadOnlyList<EmojiItem> hits = EmojiCatalog.Search("france");
        Assert.Contains(hits, e => e.Glyph == "🇫🇷");
    }

    [Fact]
    public void PushRecent_MovesGlyphToFrontAndCaps()
    {
        List<string> recents = EmojiCatalog.PushRecent(["😀", "🎉"], "❤️");
        Assert.Equal("❤️", recents[0]);
        Assert.DoesNotContain(recents.Skip(1), g => g == "❤️");

        var many = Enumerable.Range(0, 40).Select(i => $"x{i}").ToList();
        List<string> capped = EmojiCatalog.PushRecent(many, "🙂");
        Assert.Equal(EmojiCatalog.RecentLimit, capped.Count);
        Assert.Equal("🙂", capped[0]);
    }
}
