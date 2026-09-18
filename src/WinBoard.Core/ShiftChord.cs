namespace WinBoard.Core;

/// <summary>
/// Gboard-like Shift. A short tap cycles the latch
/// (<see cref="ShiftState.Off"/> → <see cref="ShiftState.Shift"/> →
/// <see cref="ShiftState.CapsLock"/> → Off). Holding Shift is a momentary
/// modifier: it does not cycle on release if another key was chorded, and it
/// does not consume a sticky latch until the hold ends.
/// </summary>
public sealed class ShiftChord
{
    public ShiftState Latch { get; private set; } = ShiftState.Off;

    public bool Held { get; private set; }

    public bool UsedAsModifier { get; private set; }

    public bool IsUpper => Held || Latch != ShiftState.Off;

    public void BeginHold()
    {
        Held = true;
        UsedAsModifier = false;
    }

    public void MarkModifierUsed()
    {
        if (Held)
        {
            UsedAsModifier = true;
        }
    }

    /// <summary>
    /// Drop the physical hold without cycling (pointer cancel / layout rebuild).
    /// Latch is unchanged.
    /// </summary>
    public void CancelHold()
    {
        Held = false;
        UsedAsModifier = false;
    }

    /// <summary>
    /// End a physical hold. Returns true when the latch changed and the
    /// keyboard caps need a full refresh.
    /// </summary>
    public bool EndHold(bool commitTap)
    {
        Held = false;
        if (!commitTap)
        {
            UsedAsModifier = false;
            return false;
        }

        if (UsedAsModifier)
        {
            UsedAsModifier = false;
            if (Latch == ShiftState.Shift)
            {
                Latch = ShiftState.Off;
                return true;
            }

            return false;
        }

        CycleLatch();
        return true;
    }

    public void CycleLatch()
    {
        Latch = Latch switch
        {
            ShiftState.Off => ShiftState.Shift,
            ShiftState.Shift => ShiftState.CapsLock,
            _ => ShiftState.Off,
        };
    }

    /// <summary>
    /// Clears a one-shot sticky Shift after a character. No-op while physically
    /// holding (the hold is the modifier) and no-op for CapsLock.
    /// </summary>
    public bool ConsumeLatch()
    {
        if (Held || Latch != ShiftState.Shift)
        {
            return false;
        }

        Latch = ShiftState.Off;
        return true;
    }

    /// <summary>
    /// Letters upper-case when shifted. Non-letters use the first character of
    /// <paramref name="secondaryGlyph"/> when the layout already exposes one
    /// (number-row superscripts, punctuation corner glyphs).
    /// </summary>
    public static char Resolve(char primary, string? secondaryGlyph, bool shifted)
    {
        if (!shifted)
        {
            return primary;
        }

        if (char.IsLetter(primary))
        {
            return char.ToUpperInvariant(primary);
        }

        if (!string.IsNullOrEmpty(secondaryGlyph))
        {
            return secondaryGlyph[0];
        }

        return primary;
    }
}

public enum ShiftState
{
    Off,
    Shift,
    CapsLock,
}
