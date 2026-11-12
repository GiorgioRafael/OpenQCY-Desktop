using CommunityToolkit.Mvvm.ComponentModel;
using OpenQCY_Desktop.Protocol;

namespace OpenQCY_Desktop.ViewModels;

public partial class EqualizerBandViewModel : ObservableObject
{
    // Index = filter type sent to the N65. Type 05 only appears in the factory presets.
    private static readonly string[] FilterTypeNames =
        ["High-pass", "Low-pass", "Peak", "High shelf", "Low shelf", "Factory shelf"];

    public static IReadOnlyList<string> FilterTypes => FilterTypeNames;

    public EqualizerBandViewModel(int number, QcyEqualizerBand band)
    {
        Number = number;
        Frequency = band.Frequency;
        Gain = band.Gain;
        Q = band.Q;
        FilterType = FilterTypes[Math.Min(band.Type, (byte)(FilterTypes.Count - 1))];
    }

    public event EventHandler? Changed;

    public int Number { get; }

    public string Title => $"Band {Number}";

    [ObservableProperty]
    public partial double Frequency { get; set; }

    [ObservableProperty]
    public partial double Gain { get; set; }

    [ObservableProperty]
    public partial double Q { get; set; }

    [ObservableProperty]
    public partial string FilterType { get; set; }

    public string GainDisplay => $"{Gain:+0.0;-0.0;0.0} dB";

    public QcyEqualizerBand ToBand() => QcyN65Equalizer.Clamp(new QcyEqualizerBand(
        (ushort)Math.Clamp(Math.Round(Frequency), 0, ushort.MaxValue),
        Gain,
        Q,
        (byte)Math.Max(0, Array.IndexOf(FilterTypeNames, FilterType))));

    partial void OnFrequencyChanged(double oldValue, double newValue)
    {
        // A cleared NumberBox reports NaN; keep the previous frequency instead.
        if (double.IsNaN(newValue))
        {
            Frequency = oldValue;
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnGainChanged(double value)
    {
        OnPropertyChanged(nameof(GainDisplay));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnQChanged(double oldValue, double newValue)
    {
        if (double.IsNaN(newValue))
        {
            Q = oldValue;
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFilterTypeChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}
