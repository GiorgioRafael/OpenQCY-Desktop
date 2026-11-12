using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenQCY_Desktop.ViewModels;

public partial class GestureViewModel : ObservableObject
{
    // Function ids captured from the official app (N65 firmware 2.0.13).
    private static readonly (string Name, byte Id)[] ActionIds =
    [
        ("No action", 0x00),
        ("Play / pause", 0x01),
        ("Previous track", 0x02),
        ("Next track", 0x03),
        ("Voice assistant", 0x04),
        ("Volume up", 0x05),
        ("Volume down", 0x06),
        ("Noise control", 0x0B),
    ];

    public GestureViewModel(string title, bool isLeft, byte gesture, byte factoryFunction)
    {
        Title = title;
        IsLeft = isLeft;
        Gesture = gesture;
        FactoryFunction = factoryFunction;
    }

    public event EventHandler? Changed;

    public static IReadOnlyList<string> Actions { get; } = ActionIds.Select(action => action.Name).ToArray();

    public string Title { get; }

    public bool IsLeft { get; }

    // Also the key in the saved profile.
    public string Name => $"{Title} · {(IsLeft ? "left" : "right")}";

    public string ActionDisplay => Action ?? "Unknown";

    public byte Gesture { get; }

    public byte FactoryFunction { get; }

    [ObservableProperty]
    public partial string? Action { get; set; }

    public byte? Function => ToFunction(Action);

    public static string? FromFunction(byte function) =>
        ActionIds.FirstOrDefault(action => action.Id == function).Name;

    public static byte? ToFunction(string? action) =>
        ActionIds.Any(item => item.Name == action) ? ActionIds.First(item => item.Name == action).Id : null;

    partial void OnActionChanged(string? value)
    {
        OnPropertyChanged(nameof(ActionDisplay));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
