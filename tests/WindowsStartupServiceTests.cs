using OpenQCY_Desktop.Services;

namespace OpenQCY.Desktop.Tests;

[TestClass]
public sealed class WindowsStartupServiceTests
{
    [TestMethod]
    public void StartupCommandQuotesExecutablePathAndAddsBackgroundArgument()
    {
        var command = WindowsStartupService.BuildStartupCommand(
            @"C:\Users\Giorgio Rafael\OpenQCY Desktop\OpenQCY.Desktop.exe");

        Assert.AreEqual(
            "\"C:\\Users\\Giorgio Rafael\\OpenQCY Desktop\\OpenQCY.Desktop.exe\" --startup",
            command);
    }

    [TestMethod]
    public void StartupLaunchRecognizesDedicatedArgumentCaseInsensitively()
    {
        Assert.IsTrue(WindowsStartupService.IsStartupLaunch(
            [@"C:\OpenQCY.Desktop.exe", "--STARTUP"]));
    }

    [TestMethod]
    public void RegularLaunchDoesNotStartHidden()
    {
        Assert.IsFalse(WindowsStartupService.IsStartupLaunch(
            [@"C:\OpenQCY.Desktop.exe"]));
    }
}
