using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;

namespace GPSCamRoute.Services;

public interface ICameraStabilizationService
{
	Task<CameraStabilizationCapabilities> GetCapabilitiesAsync(bool useFrontCamera = false, string? physicalCameraId = null, CancellationToken cancellationToken = default(CancellationToken));

	Task<CameraDiagnosticReport> GetDiagnosticReportAsync(CancellationToken cancellationToken = default(CancellationToken));
}
