using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class LanguageModelTests
{
    [Fact]
    public void Cost_AfterMais_PrefersCommentOverUnigramRival()
    {
        LanguageModel model = LanguageModel.FromTables(
            new Dictionary<string, double>
            {
                ["comment"] = 0.15,
                ["collent"] = 0.95,
            },
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["mais"] = new Dictionary<string, double> { ["comment"] = 80 },
            });

        Assert.True(
            model.Cost("comment", "mais") < model.Cost("collent", "mais"),
            "P(comment|mais) must beat the unigram-favored rival");
        Assert.True(model.HasBigram("mais", "comment"));
    }

    [Fact]
    public void Cost_WithoutPrev_FallsBackToUnigram()
    {
        LanguageModel model = LanguageModel.FromTables(
            new Dictionary<string, double>
            {
                ["comment"] = 0.90,
                ["collent"] = 0.20,
            });

        Assert.True(model.Cost("comment", null) < model.Cost("collent", null));
    }

    [Fact]
    public void LanguageWeight_IsSmallerThanALengthMiss()
    {
        Assert.True(LanguageModel.LanguageWeight < 1.0);
        Assert.True(LanguageModel.LanguageWeight < SwipeDecoder.LetterCountWeight);
        Assert.True(
            LanguageModel.LanguageWeight < SwipeDecoder.LengthRatioLongWeight,
            "A length miss must outrank P(w|prev)");
    }

    [Fact]
    public void ApplyLanguage_FlipsCloseSpatialRivals_WithoutBanning()
    {
        WordList words = WordList.FromOrderedWords(["collent", "comment"]);
        WordEntry collent = words.All.First(e => e.Word == "collent");
        WordEntry comment = words.All.First(e => e.Word == "comment");
        var spatial = new List<(WordEntry Entry, double Score)>
        {
            (collent, 1.00),
            (comment, 1.20),
        };
        LanguageModel language = LanguageModel.FromTables(
            new Dictionary<string, double> { ["collent"] = 0.5, ["comment"] = 0.5 },
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["mais"] = new Dictionary<string, double> { ["comment"] = 200 },
            });

        List<(WordEntry Entry, double Score)> rescored =
            SwipeDecoder.ApplyLanguage(spatial, language, "mais");
        string[] order = rescored.OrderBy(s => s.Score).Select(s => s.Entry.Word).ToArray();
        Assert.Equal("comment", order[0]);
        Assert.Contains("collent", order);
    }

    [Fact]
    public void ShippedFrenchBigrams_IncludeCommentLeftContexts()
    {
        WordList words = WordList.LoadLanguage("fr");
        LanguageModel model = LanguageModel.LoadLanguage("fr", words);
        Assert.True(model.HasBigram("mais", "comment"));
        Assert.True(model.HasBigram("et", "comment"));
        Assert.True(
            model.Cost("comment", "mais") < model.Cost("collent", "mais"),
            "After « mais », comment must beat a near-spatial rival on language alone");
    }
}
