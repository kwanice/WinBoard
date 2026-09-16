using WinBoard.Layouts;

namespace WinBoard.Services;

/// <summary>
/// Holds the active keyboard layout (FR AZERTY / EN QWERTY) for the session.
/// </summary>
public sealed class LayoutService
{
    public event EventHandler? LayoutChanged;

    public KeyboardLayout Current { get; private set; } = LayoutCatalog.Azerty;

    public IReadOnlyList<KeyboardLayout> Available => LayoutCatalog.All;

    public void SetLayout(string id)
    {
        KeyboardLayout next = LayoutCatalog.GetById(id);
        if (ReferenceEquals(next, Current))
        {
            return;
        }

        Current = next;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
