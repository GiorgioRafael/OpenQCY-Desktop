using OpenQCY_Desktop.Protocol;

namespace OpenQCY.Desktop.Tests;

[TestClass]
public sealed class QcyPacketTests
{
    [TestMethod]
    public void PackBuildsDocumentedInEarDisableCommand()
    {
        CollectionAssert.AreEqual(
            new byte[] { 0xFF, 0x03, 0x06, 0x01, 0x02 },
            QcyCommands.SetInEarDetection(false));
    }

    [TestMethod]
    public void ParseReadsMultipleCommands()
    {
        var commands = QcyPacket.Parse([0xFF, 0x07, 0x06, 0x01, 0x02, 0x09, 0x02, 0x01, 0x02]);

        Assert.HasCount(2, commands);
        Assert.AreEqual((byte)0x06, commands[0].Opcode);
        CollectionAssert.AreEqual(new byte[] { 0x02 }, commands[0].Parameters);
        Assert.AreEqual((byte)0x09, commands[1].Opcode);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 }, commands[1].Parameters);
    }

    [TestMethod]
    public void ParseRejectsInvalidBodyLength()
    {
        Assert.IsEmpty(QcyPacket.Parse([0xFF, 0x05, 0x06, 0x01, 0x02]));
    }

    [TestMethod]
    public void WearingDetectionPreservesDeviceActionsWhenDisabled()
    {
        var current = new QcyWearingDetection(true, 0x04, 0x02, true);

        CollectionAssert.AreEqual(
            new byte[] { 0xFF, 0x06, 0x2C, 0x04, 0x02, 0x04, 0x02, 0x01 },
            QcyCommands.SetWearingDetection(false, current));
    }

    [TestMethod]
    public void CustomEqualizerUsesN70TenBandLayout()
    {
        var packet = QcyCommands.BuildCustomEqualizer(Enumerable.Repeat(0d, 10).ToArray());

        Assert.AreEqual((byte)0xFF, packet[0]);
        Assert.AreEqual((byte)0x22, packet[2]);
        Assert.AreEqual(77, packet.Length);
        Assert.AreEqual((byte)31, packet[7]);
        Assert.AreEqual((byte)0, packet[8]);
    }

    [TestMethod]
    public void EqualizerV2ParsesPresetAndSignedGains()
    {
        var parameters = QcyCommands.BuildCustomEqualizer(
            new double[] { -8, -4, 0, 1.25, 2, 3, 4, 5, 6, 8 })[4..];

        var equalizer = QcyEqualizerState.ParseV2(parameters);

        Assert.IsNotNull(equalizer);
        Assert.AreEqual((byte)8, equalizer.PresetIndex);
        Assert.HasCount(10, equalizer.Bands);
        Assert.AreEqual((ushort)31, equalizer.Bands[0].Frequency);
        Assert.AreEqual(-8d, equalizer.Bands[0].Gain);
        Assert.AreEqual(1.25d, equalizer.Bands[3].Gain);
        Assert.AreEqual(8d, equalizer.Bands[9].Gain);
    }

    [TestMethod]
    public void AdvertisementParsesN70IdentityBatteryAndControlAddress()
    {
        var data = new byte[24];
        data[0] = 0x5D;
        data[1] = 0x40; // vendor ID 23872
        data[5] = 0x80 | 86;
        data[6] = 82;
        data[7] = 74;
        data[11] = 0x33;
        data[12] = 0x44;
        data[13] = 0x55;
        data[14] = 0x88;
        data[15] = 0x77;
        data[16] = 0x66;

        var advertisement = QcyAdvertisement.Parse(data);

        Assert.IsNotNull(advertisement);
        Assert.AreEqual(QcyUuids.N70BlackVendorId, advertisement.VendorId);
        Assert.AreEqual((byte)86, advertisement.LeftBattery);
        Assert.IsTrue(advertisement.LeftCharging);
        Assert.AreEqual("44:33:55:66:77:88", QcyAdvertisement.FormatAddress(advertisement.ControlAddress!.Value));
    }

    [TestMethod]
    public void PromptVolumeUsesRawDeviceScale()
    {
        CollectionAssert.AreEqual(
            new byte[] { 0xFF, 0x03, 0x1D, 0x01, 0x0F },
            QcyCommands.SetPromptVolume(15));
    }

    [TestMethod]
    [DataRow(QcyNoiseCancellationMode.Adaptive, 0x05, 0x00)]
    [DataRow(QcyNoiseCancellationMode.Indoor, 0x01, 0x02)]
    [DataRow(QcyNoiseCancellationMode.Commuting, 0x02, 0x02)]
    [DataRow(QcyNoiseCancellationMode.Noisy, 0x03, 0x02)]
    [DataRow(QcyNoiseCancellationMode.AntiWind, 0x04, 0x00)]
    public void NoiseCancellationPresetsUseN70AdvancedAncLayout(
        QcyNoiseCancellationMode mode,
        int subScene,
        int noiseValue)
    {
        CollectionAssert.AreEqual(
            new byte[] { 0xFF, 0x05, 0x17, 0x03, 0x01, (byte)subScene, (byte)noiseValue },
            QcyCommands.SetNoiseMode(QcyNoiseMode.NoiseCancellation, mode));
    }

    [TestMethod]
    public void NoiseControlStatePreservesCancellationPreset()
    {
        var state = QcyNoiseControlState.Parse([0x01, 0x03, 0x02]);

        Assert.IsNotNull(state);
        Assert.AreEqual(QcyNoiseMode.NoiseCancellation, state.Mode);
        Assert.AreEqual(QcyNoiseCancellationMode.Noisy, state.CancellationMode);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x03, 0x02 }, state.ToParameters());
    }

    [TestMethod]
    [DataRow(QcyNoiseMode.Transparency, 0x03, 0x01, 0x04)]
    [DataRow(QcyNoiseMode.Normal, 0x02, 0x00, 0x00)]
    public void NoiseControlTopLevelModesUseExactN70Layout(
        QcyNoiseMode mode,
        int modeValue,
        int subScene,
        int noiseValue)
    {
        CollectionAssert.AreEqual(
            new byte[]
            {
                0xFF, 0x05, 0x17, 0x03,
                (byte)modeValue, (byte)subScene, (byte)noiseValue,
            },
            QcyCommands.SetNoiseMode(mode));
    }
}
