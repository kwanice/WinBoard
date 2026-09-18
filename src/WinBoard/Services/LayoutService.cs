using WinBoard.Core;
using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// Tracks the active page (alphabetic layout or the shared symbols page),
/// remembers the alphabetic layout to return to, and holds the Shift state.
/// Physical Shift hold is a momentary chord; a tap still cycles
/// Off → Shift → CapsLock → Off.
/// </summary>
public sealed class LayoutService
{
    private readonly ShiftChord _shift = new();
    private KeyboardLayout _alphabetic = LayoutCatalog.Azerty;

    public event EventHandler? Changed;

    public KeyboardLayout Current { get; private set; } = LayoutCatalog.Azerty;

    public ShiftState Shift => _shift.Latch;

    public bool ShiftHeld => _shift.Held;

    public bool IsSymbols => !Current.IsAlphabetic;

    public bool IsUpper => _shift.IsUpper;

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
        _shift.CycleLatch();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void BeginShiftHold() => _shift.BeginHold();

    public void MarkShiftModifierUsed() => _shift.MarkModifierUsed();

    /// <summary>
    /// End a physical Shift hold. Returns true when the latch changed (caller
    /// should rebuild caps if no other pointer is still down).
    /// </summary>
    public bool EndShiftHold(bool commitTap) => _shift.EndHold(commitTap);

    public void CancelShiftHold() => _shift.CancelHold();

    /// <summary>Clears a one-shot Shift after a character is typed (Caps stays on).</summary>
    public void ConsumeShift()
    {
        if (_shift.ConsumeLatch())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
