using WinBoard.Core;
using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// User-configurable options, persisted as JSON. Defaults reproduce the
/// Gboard-style AZERTY reference (number row + secondary glyphs visible).
/// </summary>
public sealed class KeyboardSettings
{
    public string LayoutId { get; set; } = LayoutCatalog.AzertyId;

    public bool ShowNumberRow { get; set; } = true;

    public bool ShowSecondaryGlyphs { get; set; } = true;

    public bool KeyRepeatEnabled { get; set; } = true;

    public int KeyRepeatInitialDelayMs { get; set; } = 350;

    public int KeyRepeatIntervalMs { get; set; } = 55;

    public bool LongPressEnabled { get; set; } = true;

    public int LongPressDelayMs { get; set; } = 300;

    /// <summary>Glide/swipe typing: trace across letters to type a whole word.</summary>
    public bool SwipeEnabled { get; set; } = true;

    /// <summary>Draw the swipe trail while gliding.</summary>
    public bool ShowSwipeTrail { get; set; } = true;

    /// <summary>Draw a high-contrast outline around keys (2.5 px, live).</summary>
    public bool ShowKeyOutlines { get; set; } = true;

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Whole-window opacity via WS_EX_LAYERED, 0.25–1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Overall keyboard size multiplier, 0.70–1.80.</summary>
    public double SizeScale { get; set; } = 1.0;

    /// <summary>Letter font size multiplier, independent of <see cref="SizeScale"/>.</summary>
    public double LetterFontScale { get; set; } = 1.0;

    /// <summary>Recently tapped emojis (glyphs), most recent first. Local only.</summary>
    public List<string> RecentEmojis { get; set; } = [];

    public KeyboardSettings Clone()
    {
        var copy = (KeyboardSettings)MemberwiseClone();
        copy.RecentEmojis = [.. RecentEmojis ?? []];
        return copy;
    }

    public void Normalize()
    {
        if (LayoutId != LayoutCatalog.QwertyId)
        {
            LayoutId = LayoutCatalog.AzertyId;
        }

        if (Theme is not ("Dark" or "Light"))
        {
            Theme = "Dark";
        }

        KeyRepeatInitialDelayMs = Math.Clamp(KeyRepeatInitialDelayMs, 150, 800);
        KeyRepeatIntervalMs = Math.Clamp(KeyRepeatIntervalMs, 20, 300);
        LongPressDelayMs = Math.Clamp(LongPressDelayMs, 150, 700);
        Opacity = Math.Clamp(Opacity, 0.25, 1.0);
        SizeScale = Math.Clamp(SizeScale, 0.70, 1.80);
        LetterFontScale = Math.Clamp(LetterFontScale, 0.70, 1.60);

        RecentEmojis ??= [];
        RecentEmojis = EmojiCatalog.PushRecent(RecentEmojis.Where(g => !string.IsNullOrWhiteSpace(g)), glyph: string.Empty);
    }
}
