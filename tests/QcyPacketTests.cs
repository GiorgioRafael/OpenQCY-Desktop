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
}
