using System.Threading;
using System.Threading.Tasks;

namespace RokuDotNet.Client.Query
{
    public interface IRokuDeviceQuery
    {
        Task<GetActiveAppResult> GetActiveAppAsync(CancellationToken cancellationToken = default(CancellationToken));

        Task<GetActiveTvChannelResult> GetActiveTvChannelAsync(CancellationToken cancellationToken = default(CancellationToken));
    
        Task<GetAppsResult> GetAppsAsync(CancellationToken cancellationToken = default(CancellationToken));
    
        Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default(CancellationToken));

        Task<GetTvChannelsResult> GetTvChannelsAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}