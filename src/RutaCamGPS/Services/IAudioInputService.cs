using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;

namespace GPSCamRoute.Services;

public interface IAudioInputService
{
	Task<IReadOnlyList<AudioInputOption>> GetAvailableInputsAsync(bool requestBluetoothPermission, CancellationToken cancellationToken);

	Task<AudioRouteResult> ActivateAsync(string selectionId, CancellationToken cancellationToken);

	Task<AudioRouteResult> EnsureSelectedRouteAsync(string selectionId, CancellationToken cancellationToken);

	Task<MicrophoneTestResult> TestMicrophoneAsync(string selectionId, IProgress<double>? levelProgress, CancellationToken cancellationToken);

	Task ReleaseAsync();
}
