using OpenQCY_Desktop.Protocol;

namespace OpenQCY.Desktop.Tests;

[TestClass]
public sealed class QcyN65Tests
{
    [TestMethod]
    public void WrapMatchesCapturedBatteryQuery()
    {
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF000424FE012FEF"),
            QcyJieliFrame.Wrap(0x24, QcyCommands.Request(0x2F)));
    }

    [TestMethod]
    [DataRow(QcyNoiseMode.Normal, 0x30, "FEDCBAC0FF0006301703020000EF")]
    [DataRow(QcyNoiseMode.Transparency, 0x31, "FEDCBAC0FF0006311703030104EF")]
    [DataRow(QcyNoiseMode.NoiseCancellation, 0x32, "FEDCBAC0FF0006321703010500EF")]
    public void WrapMatchesCapturedNoiseControlWrites(QcyNoiseMode mode, int sequence, string expected)
    {
        CollectionAssert.AreEqual(
            Convert.FromHexString(expected),
            QcyJieliFrame.Wrap((byte)sequence, QcyCommands.SetNoiseMode(mode)));
    }

    [TestMethod]
    public void ParseReadsResponseStatusSequenceAndCommand()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString("FEDCBA00FF000700242F03646464EF"));

        Assert.HasCount(1, messages);
        Assert.IsTrue(messages[0].IsResponse);
        Assert.AreEqual((byte)0x00, messages[0].Status);
        Assert.AreEqual((byte)0x24, messages[0].Sequence);
        Assert.HasCount(1, messages[0].Commands);
        Assert.AreEqual((byte)0x2F, messages[0].Commands[0].Opcode);
        CollectionAssert.AreEqual(new byte[] { 100, 100, 100 }, messages[0].Commands[0].Parameters);
    }

    [TestMethod]
    public void ParseReadsAcknowledgementWithoutCommands()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString("FEDCBA00FF00020030EF"));

        Assert.HasCount(1, messages);
        Assert.IsTrue(messages[0].IsResponse);
        Assert.AreEqual((byte)0x30, messages[0].Sequence);
        Assert.IsEmpty(messages[0].Commands);
    }

    [TestMethod]
    public void ParseReadsDevicePushWithoutStatus()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString("FEDCBA80FF0006181703020000EF"));

        Assert.HasCount(1, messages);
        Assert.IsFalse(messages[0].IsResponse);
        Assert.IsNull(messages[0].Status);
        Assert.AreEqual((byte)0x17, messages[0].Commands[0].Opcode);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x00, 0x00 }, messages[0].Commands[0].Parameters);
    }

    [TestMethod]
    public void ParseReadsConcatenatedFrames()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString("FEDCBA80FF00041B270100EFFEDCBA00FF00020036EF"));

        Assert.HasCount(2, messages);
        Assert.AreEqual((byte)0x27, messages[0].Commands[0].Opcode);
        Assert.AreEqual((byte)0x36, messages[1].Sequence);
    }

    [TestMethod]
    public void ParseSkipsNativeJieliCommands()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString("FEDCBAC0C400027702EFFEDCBA80FF000478100102EF"));

        Assert.HasCount(1, messages);
        Assert.AreEqual((byte)0x10, messages[0].Commands[0].Opcode);
    }

    [TestMethod]
    public void ParseRejectsStandardQcyPacket()
    {
        Assert.IsEmpty(QcyJieliFrame.Parse(Convert.FromHexString("FF03FE012F")));
    }

    [TestMethod]
    public void CapturedEqualizerResponseHasSevenBands()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString(
            "FEDCBA00FF0038002722340188FA1400B4FB500002A00024FA3C0002EE02D4FE6400025E0138FF640002540B1405500002401F2C012C010210275802960005EF"));
        var equalizer = QcyEqualizerState.ParseV2(messages[0].Commands[0].Parameters);

        Assert.IsNotNull(equalizer);
        Assert.AreEqual((byte)0x01, equalizer.PresetIndex);
        Assert.AreEqual(-14d, equalizer.MasterGain);
        Assert.HasCount(7, equalizer.Bands);
        Assert.AreEqual((ushort)20, equalizer.Bands[0].Frequency);
        Assert.AreEqual(-11d, equalizer.Bands[0].Gain);
        Assert.AreEqual(13d, equalizer.Bands[4].Gain);
    }

    [TestMethod]
    public void PresetMatchesCapturedPopWrite()
    {
        CollectionAssert.AreEqual(
            Convert.FromHexString(
                "FEDCBAC0FF003E37223B0288FA1400ECFA460002A000C0F93C0002EE0238FF6400025E0170FE640002540B1405500002401F2C012C0102102758029600053200D4FE460002EF"),
            QcyJieliFrame.Wrap(0x37, QcyN65Equalizer.SetPreset(0x02)));
    }

    [TestMethod]
    public void EveryPresetParsesWithItsOwnId()
    {
        foreach (var preset in QcyN65Equalizer.Presets)
        {
            var state = QcyEqualizerState.ParseV2(Convert.FromHexString(preset.Data));

            Assert.IsNotNull(state, preset.Name);
            Assert.AreEqual(preset.Id, state.PresetIndex, preset.Name);
        }
    }

    [TestMethod]
    [DataRow(0x4B, "FEDCBAC0FF00374B22348131F91400E0FC500002A0003AFD3C0002EE02D4FE6400025E0138FF640002540B1405500002401F2C012C010210275802960005EF")]
    [DataRow(0x55, "FEDCBAC0FF00375522348138F91400E0FC500002A000E0FC3C0002EE027AFE6400025E014CFF640002540B1405500002401F2C012C010210275802960005EF")]
    [DataRow(0x5C, "FEDCBAC0FF00375C22348188FA1400E0FC500002A000E0FC3C0002EE027AFE6400025E01E0FC640002540BE0FC500002401F2C012C010210275802960005EF")]
    [DataRow(0x90, "FEDCBAC0FF004C9022498188FA540BE0FC500002401FE0FC2C0102204EE0FCD0070420000000460002400000004600027D000000460002FA000000460002F4010000460002E8030000460002D0070000460002EF")]
    public void CustomCurveMatchesCapturedWriteIncludingMasterGain(int sequence, string captured)
    {
        var frame = Convert.FromHexString(captured);
        var bands = QcyEqualizerState.ParseV2(QcyJieliFrame.Parse(
            [0xFE, 0xDC, 0xBA, 0x80, .. frame[4..]])[0].Commands[0].Parameters)!.Bands;

        CollectionAssert.AreEqual(frame, QcyJieliFrame.Wrap((byte)sequence, QcyN65Equalizer.BuildCustom(bands)));
    }

    [TestMethod]
    public void MasterGainNeverExceedsFactoryCeiling()
    {
        Assert.AreEqual(-14d, QcyN65Equalizer.MasterGain([new QcyEqualizerBand(1000, -8, 1, 0x02)]));
        Assert.IsLessThan(-14d, QcyN65Equalizer.MasterGain([new QcyEqualizerBand(1000, 15, 1, 0x02)]));
    }

    [TestMethod]
    public void BuildCustomClampsBandsToTheAppLimits()
    {
        var state = QcyEqualizerState.ParseV2(
            QcyN65Equalizer.BuildCustom([new QcyEqualizerBand(5, 30, 50, 0x02)])[4..]);

        Assert.IsNotNull(state);
        Assert.AreEqual(QcyN65Equalizer.CustomPreset, state.PresetIndex);
        Assert.AreEqual(new QcyEqualizerBand(20, 15, 20, 0x02), state.Bands[0]);
    }

    [TestMethod]
    public void NewBandsUseTheAppFrequencies()
    {
        var first = QcyN65Equalizer.NewBand(QcyN65Equalizer.FactoryTuning.Bands);
        var second = QcyN65Equalizer.NewBand([.. QcyN65Equalizer.FactoryTuning.Bands, first]);

        Assert.AreEqual(new QcyEqualizerBand(32, 0, 0.7, 0x02), first);
        Assert.AreEqual((ushort)64, second.Frequency);
    }

    [TestMethod]
    public void BuildCustomRejectsMoreThanTenBands()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QcyN65Equalizer.BuildCustom(
            Enumerable.Repeat(new QcyEqualizerBand(1000, 0, 1, 0x02), 11).ToArray()));
    }

    [TestMethod]
    public void N65CommandsMatchCapture()
    {
        CollectionAssert.AreEqual(Convert.FromHexString("FF044A020100"), QcyCommands.SetN65GameMode(true));
        CollectionAssert.AreEqual(Convert.FromHexString("FF044A020000"), QcyCommands.SetN65GameMode(false));
        CollectionAssert.AreEqual(Convert.FromHexString("FF03230100"), QcyCommands.SetN65Ldac(false));
        CollectionAssert.AreEqual(Convert.FromHexString("FF03270101"), QcyCommands.SetDynamicEqualizer(true));
        CollectionAssert.AreEqual(Convert.FromHexString("FF03160132"), QcyCommands.SetChannelBalance(50));
    }

    [TestMethod]
    [DataRow(0x40, 0x01, 0x00, "FEDCBAC0FF0005402B020100EF")]
    [DataRow(0x47, 0x01, 0x0B, "FEDCBAC0FF0005472B02010BEF")]
    [DataRow(0x50, 0x0A, 0x01, "FEDCBAC0FF0005502B020A01EF")]
    public void KeyFunctionMatchesCapturedGestureWrites(int sequence, int gesture, int function, string expected)
    {
        CollectionAssert.AreEqual(
            Convert.FromHexString(expected),
            QcyJieliFrame.Wrap((byte)sequence, QcyCommands.SetN65KeyFunction((byte)gesture, (byte)function)));
    }

    [TestMethod]
    public void CapturedKeyFunctionResponseMapsEveryGesture()
    {
        var messages = QcyJieliFrame.Parse(Convert.FromHexString(
            "FEDCBA00FF002C00282B2801060205030104030502060B0700080009040A044180428043804480458046804780488049804A80EF"));

        var keyFunctions = QcyCommands.ParseKeyFunctions(messages[0].Commands[0].Parameters);

        Assert.AreEqual((byte)0x06, keyFunctions[0x01]);
        Assert.AreEqual((byte)0x0B, keyFunctions[0x06]);
        Assert.AreEqual((byte)0x04, keyFunctions[0x0A]);
        Assert.AreEqual((byte)0x80, keyFunctions[0x41]);
    }

    [TestMethod]
    public void N65WritesMatchCapturedFrames()
    {
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF000498230100EF"),
            QcyJieliFrame.Wrap(0x98, QcyCommands.SetN65Ldac(false)));
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF0005C64A020100EF"),
            QcyJieliFrame.Wrap(0xC6, QcyCommands.SetN65GameMode(true)));
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF0005D64A020000EF"),
            QcyJieliFrame.Wrap(0xD6, QcyCommands.SetN65GameMode(false)));
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF0004E8270101EF"),
            QcyJieliFrame.Wrap(0xE8, QcyCommands.SetDynamicEqualizer(true)));
        CollectionAssert.AreEqual(
            Convert.FromHexString("FEDCBAC0FF0004EF16013CEF"),
            QcyJieliFrame.Wrap(0xEF, QcyCommands.SetChannelBalance(60)));
    }

    [TestMethod]
    public void N65VendorIdIsSupported()
    {
        Assert.IsTrue(QcyUuids.IsSupported(32894));
        Assert.IsTrue(QcyUuids.IsN65(0x807E));
        Assert.IsFalse(QcyUuids.IsN70(32894));
        Assert.AreEqual("QCY MeloBuds N65", QcyUuids.ModelName(32894));
    }
}
