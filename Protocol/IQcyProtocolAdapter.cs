using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.Protocol;

public interface IQcyProtocolAdapter
{
    bool SupportsWrites { get; }
    Task<ProfileApplyResult> ApplyProfileAsync(DeviceProfile profile, CancellationToken cancellationToken = default);
}
