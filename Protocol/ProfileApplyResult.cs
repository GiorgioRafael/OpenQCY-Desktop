namespace OpenQCY_Desktop.Protocol;

public sealed record ProfileApplyResult(bool Applied, string Message)
{
    public static ProfileApplyResult ProtocolPending() => new(
        false,
        "Profile saved locally. Bluetooth writes remain disabled until the N70 protocol is validated.");
}
