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

    internal static DeviceProfile Migrate(DeviceProfile profile)
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

        if (profile.ProfileVersion < 4)
        {
            profile.ProfileVersion = 4;
            profile.SelectedNoiseMode = TranslateNoiseMode(profile.SelectedNoiseMode);
            profile.SelectedNoiseCancellationMode = TranslateNoiseCancellationMode(
                profile.SelectedNoiseCancellationMode);
            profile.SelectedEqualizerPreset = TranslateEqualizerPreset(profile.SelectedEqualizerPreset);
            profile.LeftDoubleTap = TranslateTouchAction(profile.LeftDoubleTap);
            profile.RightDoubleTap = TranslateTouchAction(profile.RightDoubleTap);
            profile.LeftLongPress = TranslateTouchAction(profile.LeftLongPress);
            profile.RightLongPress = TranslateTouchAction(profile.RightLongPress);
            profile.DisconnectTimeout = TranslateDisconnectTimeout(profile.DisconnectTimeout);
        }

        profile.EqBands = profile.EqBands
            .Select(value => Math.Clamp(value, -8, 8))
            .ToArray();
        return profile;
    }

    private static string TranslateNoiseMode(string value) => value switch
    {
        "Cancelamento" => "Noise cancellation",
        "Transparência" => "Transparency",
        _ => value,
    };

    private static string TranslateNoiseCancellationMode(string value) => value switch
    {
        "Adaptativo" => "Adaptive",
        "Ambiente interno" => "Indoor",
        "Deslocamento" => "Commuting",
        "Ambiente ruidoso" => "Noisy environment",
        "Anti-vento" => "Anti-wind",
        _ => value,
    };

    private static string TranslateEqualizerPreset(string value) => value switch
    {
        "Som espacial" => "Spatial audio",
        "Padrão" or "Balanceado" => "Default",
        "Palco suave" => "Soft stage",
        "Graves fortes" => "Bass boost",
        "Suave" => "Soft",
        "Clássico" => "Classical",
        "Personalizado" => "Custom",
        _ => value,
    };

    private static string TranslateTouchAction(string value) => value switch
    {
        "Sem ação" => "No action",
        "Reproduzir / pausar" => "Play / pause",
        "Faixa anterior" => "Previous track",
        "Próxima faixa" => "Next track",
        "Assistente" => "Voice assistant",
        "Aumentar volume" => "Volume up",
        "Diminuir volume" => "Volume down",
        "Modo jogo" => "Game mode",
        "Modo de ruído" => "Noise control",
        _ => value,
    };

    private static string TranslateDisconnectTimeout(string value) => value switch
    {
        "Nunca" => "Never",
        "5 minutos" => "5 minutes",
        "15 minutos" => "15 minutes",
        "30 minutos" => "30 minutes",
        "1 hora" => "1 hour",
        _ => value,
    };
}
