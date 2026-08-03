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
            return File.Exists(_profilePath)
                ? JsonSerializer.Deserialize<DeviceProfile>(File.ReadAllText(_profilePath), SerializerOptions)
                : null;
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
}
