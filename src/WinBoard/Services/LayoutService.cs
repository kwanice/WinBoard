using WinBoard.Layouts;

namespace WinBoard.Services;

public enum ShiftState
{
    Off,
    Shift,
    CapsLock,
}

/// <summary>
/// Tracks the active page (alphabetic layout or the shared symbols page),
/// remembers the alphabetic layout to return to, and holds the Shift state.
/// </summary>
public sealed class LayoutService
{
    private KeyboardLayout _alphabetic = LayoutCatalog.Azerty;

    public event EventHandler? Changed;

    public KeyboardLayout Current { get; private set; } = LayoutCatalog.Azerty;

    public ShiftState Shift { get; private set; } = ShiftState.Off;

    public bool IsSymbols => !Current.IsAlphabetic;

    public bool IsUpper => Shift != ShiftState.Off;

    /// <summary>Sets the alphabetic layout (FR/EN) and shows it.</summary>
    public void SetAlphabetic(string id)
    {
        KeyboardLayout next = LayoutCatalog.GetById(id);
        if (!next.IsAlphabetic)
        {
            return;
        }

        _alphabetic = next;
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches between the current alphabetic layout and the symbols page.</summary>
    public void ToggleSymbols()
    {
        Current = Current.IsAlphabetic ? LayoutCatalog.Symbols : _alphabetic;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Toggles FR AZERTY / EN QWERTY (used by the space-bar long-press).</summary>
    public void ToggleLanguage()
    {
        SetAlphabetic(_alphabetic.Id == LayoutCatalog.AzertyId
            ? LayoutCatalog.QwertyId
            : LayoutCatalog.AzertyId);
    }

    /// <summary>Cycles Off → Shift → CapsLock → Off on tap.</summary>
    public void CycleShift()
    {
        Shift = Shift switch
        {
            ShiftState.Off => ShiftState.Shift,
            ShiftState.Shift => ShiftState.CapsLock,
            _ => ShiftState.Off,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears a one-shot Shift after a character is typed (Caps stays on).</summary>
    public void ConsumeShift()
    {
        if (Shift == ShiftState.Shift)
        {
            Shift = ShiftState.Off;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
