using Microsoft.Win32;

namespace OpenQCY_Desktop.Services;

public sealed class WindowsStartupService
{
    internal const string StartupArgument = "--startup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OpenQCY Desktop";

    public bool IsEnabled
    {
        get
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var registeredCommand = runKey?.GetValue(RunValueName) as string;
            return string.Equals(
                registeredCommand,
                BuildStartupCommand(GetExecutablePath()),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not access Windows startup settings.");

        if (enabled)
        {
            runKey.SetValue(
                RunValueName,
                BuildStartupCommand(GetExecutablePath()),
                RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    internal static bool IsStartupLaunch(IReadOnlyList<string> commandLineArguments) =>
        commandLineArguments
            .Skip(1)
            .Any(argument => string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));

    internal static string BuildStartupCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"'))
        {
            throw new ArgumentException("The application path contains quotation marks.", nameof(executablePath));
        }

        return $"\"{executablePath}\" {StartupArgument}";
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The OpenQCY Desktop path is unavailable.");
}
