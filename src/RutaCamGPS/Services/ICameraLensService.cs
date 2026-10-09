using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;

namespace GPSCamRoute.Services;

public interface ICameraLensService
{
	Task<IReadOnlyList<CameraLensOption>> GetRearLensOptionsAsync(CancellationToken cancellationToken = default(CancellationToken));
}
