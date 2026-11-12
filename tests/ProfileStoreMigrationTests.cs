using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.Tests;

[TestClass]
public sealed class ProfileStoreMigrationTests
{
    [TestMethod]
    public void VersionThreeProfileMigratesStoredLabelsToEnglish()
    {
        var profile = new DeviceProfile
        {
            ProfileVersion = 3,
            SelectedNoiseMode = "Cancelamento",
            SelectedNoiseCancellationMode = "Ambiente ruidoso",
            SelectedEqualizerPreset = "Personalizado",
            LeftDoubleTap = "Reproduzir / pausar",
            RightDoubleTap = "Próxima faixa",
            LeftLongPress = "Modo de ruído",
            RightLongPress = "Assistente",
            DisconnectTimeout = "30 minutos",
        };

        var migrated = ProfileStore.Migrate(profile);

        Assert.AreEqual(4, migrated.ProfileVersion);
        Assert.AreEqual("Noise cancellation", migrated.SelectedNoiseMode);
        Assert.AreEqual("Noisy environment", migrated.SelectedNoiseCancellationMode);
        Assert.AreEqual("Custom", migrated.SelectedEqualizerPreset);
        Assert.AreEqual("Play / pause", migrated.LeftDoubleTap);
        Assert.AreEqual("Next track", migrated.RightDoubleTap);
        Assert.AreEqual("Noise control", migrated.LeftLongPress);
        Assert.AreEqual("Voice assistant", migrated.RightLongPress);
        Assert.AreEqual("30 minutes", migrated.DisconnectTimeout);
    }

    [TestMethod]
    public void SavedEqualizerCurvesSurviveSerialization()
    {
        var profile = new DeviceProfile
        {
            N65EqualizerCurves =
            [
                new SavedEqualizerCurve
                {
                    Name = "Bass",
                    Bands = [new SavedEqualizerBand { Frequency = 64, Gain = 4.5, Q = 0.7, Type = 2 }],
                },
            ],
        };

        var restored = System.Text.Json.JsonSerializer.Deserialize<DeviceProfile>(
            System.Text.Json.JsonSerializer.Serialize(profile));

        Assert.IsNotNull(restored);
        Assert.HasCount(1, restored.N65EqualizerCurves);
        Assert.AreEqual("Bass", restored.N65EqualizerCurves[0].Name);
        Assert.AreEqual(4.5, restored.N65EqualizerCurves[0].Bands[0].Gain);
    }

    [TestMethod]
    public void ProfileWithoutEqualizerCurvesLoadsEmptyLists()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<DeviceProfile>("{\"ProfileVersion\":4}");

        Assert.IsNotNull(restored);
        Assert.IsEmpty(restored.N65EqualizerCurves);
        Assert.IsEmpty(restored.N65CustomBands);
    }
}
