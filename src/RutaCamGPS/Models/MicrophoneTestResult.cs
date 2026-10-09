namespace GPSCamRoute.Models;

public sealed class MicrophoneTestResult
{
	public bool Success { get; init; }

	public string ActiveDisplayName { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;

	public double PeakLevel { get; init; }
}
