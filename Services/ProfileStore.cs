using System.Text.Json;

namespace OpenQCY_Desktop.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQCY Desktop",
        "profile.json");

    public DeviceProfile? Load()
    {
        try
        {
            var profile = File.Exists(_profilePath)
                ? JsonSerializer.Deserialize<DeviceProfile>(File.ReadAllText(_profilePath), SerializerOptions)
                : null;
            return profile is null ? null : Migrate(profile);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(DeviceProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profile, SerializerOptions));
    }

    private static DeviceProfile Migrate(DeviceProfile profile)
    {
        if (profile.ProfileVersion < 2 || profile.EqBands.Length != 10)
        {
            profile.ProfileVersion = 2;
            profile.EqBands = new double[10];
            if (profile.SelectedEqualizerPreset == "Balanceado")
            {
                profile.SelectedEqualizerPreset = "Padrão";
            }
        }

        if (profile.ProfileVersion < 3)
        {
            profile.ProfileVersion = 3;
            profile.SelectedNoiseCancellationMode = "Adaptativo";
        }

        profile.EqBands = profile.EqBands
            .Select(value => Math.Clamp(value, -8, 8))
            .ToArray();
        return profile;
    }
}
