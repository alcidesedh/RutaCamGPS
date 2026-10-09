using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;

namespace GPSCamRoute.Services;

public interface ICameraXOverlayRecorderService
{
	bool IsRecording { get; }

	string? LastError { get; }

	string? LastStabilizationStatus { get; }

	Task StartAsync(object previewPlatformView, string outputPath, bool useFrontCamera, bool recordAudio, string audioInputDeviceId, CameraXRecordingOptions recordingOptions, CameraXHudOptions hudOptions, CameraXHudSnapshot initialSnapshot, CancellationToken cancellationToken);

	Task StopAsync(CancellationToken cancellationToken);

	void UpdateHud(CameraXHudSnapshot snapshot);

	void UpdateMapState(CameraXMapState state);

	void SetZoom(float zoomFactor);

	void SetTorch(bool enabled);
}
