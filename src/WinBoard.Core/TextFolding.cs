using System.Globalization;
using System.Text;

namespace WinBoard.Core;

/// <summary>Accent folding shared by the word lists and decoder.</summary>
public static class TextFolding
{
    public static char[] ToLetters(string value)
    {
        string expanded = value
            .Replace("œ", "oe", StringComparison.OrdinalIgnoreCase)
            .Replace("æ", "ae", StringComparison.OrdinalIgnoreCase)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);

        string normalized = expanded.Normalize(NormalizationForm.FormD);
        var result = new List<char>(normalized.Length);
        foreach (char ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z')
            {
                result.Add(lower);
            }
        }

        return result.ToArray();
    }
}
