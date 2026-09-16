using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

/// <summary>
/// Smoke tests for the shipped FR/EN lexicons (FrequencyWords 2018, local files).
/// </summary>
public sealed class WordListTests
{
    private const int MinimumLexiconSize = 50_000;

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void ShippedLexicon_ExceedsMinimumSize(string language)
    {
        WordList list = WordList.LoadLanguage(language);
        Assert.True(list.Count >= MinimumLexiconSize,
            $"{language} lexicon has {list.Count} words, expected ≥ {MinimumLexiconSize}");
    }

    [Fact]
    public void FrenchLexicon_ContainsEverydayWords()
    {
        WordList list = WordList.LoadLanguage("fr");
        string[] required = ["comment", "content", "comme", "commencer", "bonjour", "merci", "être", "c'est"];
        foreach (string word in required)
        {
            Assert.True(list.Contains(word), $"French lexicon missing « {word} »");
        }

        Assert.False(list.Contains("coment"),
            "Subtitle typo « coment » must not outrank « comment »");
    }

    [Fact]
    public void EnglishLexicon_ContainsEverydayWords()
    {
        WordList list = WordList.LoadLanguage("en");
        string[] required = ["the", "and", "you", "that", "because", "people", "comment", "content", "please"];
        foreach (string word in required)
        {
            Assert.True(list.Contains(word), $"English lexicon missing '{word}'");
        }
    }

    [Fact]
    public void FromLines_SkipsCommentsAndOptionalFrequency()
    {
        WordList list = WordList.FromLines(
        [
            "# header",
            "",
            "comment 999",
            "content\t12",
            "comme",
        ]);

        Assert.Equal(3, list.Count);
        Assert.True(list.Contains("comment"));
        Assert.True(list.Contains("content"));
        Assert.True(list.Contains("comme"));
        Assert.Equal("comment", list.All.First().Word);
    }
}
