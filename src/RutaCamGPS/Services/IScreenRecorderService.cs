using System.Threading;
using System.Threading.Tasks;

namespace GPSCamRoute.Services;

public interface IScreenRecorderService
{
	bool IsRecording { get; }

	Task StartAsync(string outputPath, CancellationToken cancellationToken = default(CancellationToken));

	Task StopAsync(CancellationToken cancellationToken = default(CancellationToken));
}
