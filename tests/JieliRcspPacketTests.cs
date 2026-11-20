using OpenQCY_Desktop.Protocol;

namespace OpenQCY.Desktop.Tests;

[TestClass]
public sealed class JieliRcspPacketTests
{
    [TestMethod]
    public void PackCustomRequestBuildsExpectedRcspFrame()
    {
        var frame = JieliRcspPacket.PackCustomRequest(sn: 0x02, opcode: 0x17);

        // FE DC BA C0 FF 00 04 02 FE 01 17 EF
        byte[] expected = [0xFE, 0xDC, 0xBA, 0xC0, 0xFF, 0x00, 0x04, 0x02, 0xFE, 0x01, 0x17, 0xEF];
        CollectionAssert.AreEqual(expected, frame);
    }

    [TestMethod]
    public void PackCustomCommandBuildsExpectedRcspFrame()
    {
        // Set ANC: opcode 0x17, params: [0x01, 0x05, 0x00]
        var frame = JieliRcspPacket.PackCustomCommand(sn: 0x05, opcode: 0x17, [0x01, 0x05, 0x00]);

        // FE DC BA C0 FF 00 06 05 17 03 01 05 00 EF
        byte[] expected = [0xFE, 0xDC, 0xBA, 0xC0, 0xFF, 0x00, 0x06, 0x05, 0x17, 0x03, 0x01, 0x05, 0x00, 0xEF];
        CollectionAssert.AreEqual(expected, frame);
    }

    [TestMethod]
    public void PackAdvGetBatteryBuildsExpectedRcspFrame()
    {
        var frame = JieliRcspPacket.PackAdvGet(sn: 0x00, mask: 0x00000001);

        byte[] expected = [0xFE, 0xDC, 0xBA, 0xC0, 0xC1, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x01, 0xEF];
        CollectionAssert.AreEqual(expected, frame);
    }

    [TestMethod]
    public void PackAnswerBuildsExpectedResponseFrame()
    {
        var frame = JieliRcspPacket.PackAnswer(opcode: 0xFF, sn: 0x05, status: 0);

        byte[] expected = [0xFE, 0xDC, 0xBA, 0x00, 0xFF, 0x00, 0x02, 0x00, 0x05, 0xEF];
        CollectionAssert.AreEqual(expected, frame);
    }

    [TestMethod]
    public void ParserExtractsCustomResponseFrame()
    {
        // Device response for ANC query: FEDCBA00FF000700021703020000EF
        byte[] raw = [0xFE, 0xDC, 0xBA, 0x00, 0xFF, 0x00, 0x07, 0x00, 0x02, 0x17, 0x03, 0x02, 0x00, 0x00, 0xEF];

        var parser = new JieliRcspParser();
        var frames = parser.Feed(raw);

        Assert.HasCount(1, frames);
        var frame = frames[0];
        Assert.IsFalse(frame.IsCommand);
        Assert.IsFalse(frame.WantsAnswer);
        Assert.AreEqual((byte)0xFF, frame.Opcode);
        Assert.AreEqual((byte)0x02, frame.Sn);
        Assert.AreEqual((byte)0x00, frame.Status);

        var commands = JieliRcspPacket.ExtractCommands(frame);
        Assert.HasCount(1, commands);
        Assert.AreEqual((byte)0x17, commands[0].Opcode);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0x00, 0x00 }, commands[0].Parameters);
    }

    [TestMethod]
    public void ParserExtractsBatteryFromAdvResponse()
    {
        // FEDCBA00C1000700000400141E00EF
        byte[] raw = [0xFE, 0xDC, 0xBA, 0x00, 0xC1, 0x00, 0x07, 0x00, 0x00, 0x04, 0x00, 0x14, 0x1E, 0x00, 0xEF];

        var parser = new JieliRcspParser();
        var frames = parser.Feed(raw);

        Assert.HasCount(1, frames);
        var commands = JieliRcspPacket.ExtractCommands(frames[0]);
        Assert.HasCount(1, commands);
        Assert.AreEqual((byte)0x2F, commands[0].Opcode);
        CollectionAssert.AreEqual(new byte[] { 0x14, 0x1E, 0x00 }, commands[0].Parameters);
    }

    [TestMethod]
    public void ParserHandlesChunkedStreamingPackets()
    {
        byte[] part1 = [0xFE, 0xDC, 0xBA, 0x00, 0xFF, 0x00];
        byte[] part2 = [0x05, 0x00, 0x03, 0x09, 0x01, 0x02, 0xEF]; // Game mode response: 09 01 02

        var parser = new JieliRcspParser();
        var frames1 = parser.Feed(part1);
        Assert.IsEmpty(frames1);

        var frames2 = parser.Feed(part2);
        Assert.HasCount(1, frames2);
        Assert.AreEqual((byte)0x09, frames2[0].Payload[0]);
    }

    [TestMethod]
    public void N30VendorIdsAreIdentifiedCorrectly()
    {
        Assert.IsTrue(QcyUuids.IsN30(32881));
        Assert.IsTrue(QcyUuids.IsN30(32883));
        Assert.IsTrue(QcyUuids.IsSupported(32881));
        Assert.IsTrue(QcyUuids.IsJieli(32881));
        Assert.AreEqual("QCY MeloBuds N30", QcyUuids.GetModelName(32881));
    }
}
