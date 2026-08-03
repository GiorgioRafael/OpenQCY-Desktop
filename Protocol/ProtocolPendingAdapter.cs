using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.Protocol;

/// <summary>
/// Safety gate used until captured N70 commands are documented and replay-tested.
/// </summary>
public sealed class ProtocolPendingAdapter : IQcyProtocolAdapter
{
    public bool SupportsWrites => false;

    public Task<ProfileApplyResult> ApplyProfileAsync(
        DeviceProfile profile,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ProfileApplyResult.ProtocolPending());
    }
}
