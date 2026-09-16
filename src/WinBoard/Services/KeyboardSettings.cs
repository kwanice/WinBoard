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

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Window opacity, 0.4–1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Key size multiplier, 0.8–1.4.</summary>
    public double SizeScale { get; set; } = 1.0;

    public KeyboardSettings Clone() => (KeyboardSettings)MemberwiseClone();

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
        Opacity = Math.Clamp(Opacity, 0.4, 1.0);
        SizeScale = Math.Clamp(SizeScale, 0.8, 1.4);
    }
}
