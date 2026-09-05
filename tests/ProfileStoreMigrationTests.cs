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
}
