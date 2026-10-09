namespace GPSCamRoute.Models;

public sealed class CameraStabilizationDecision
{
	public bool? EnableVideoStabilization { get; init; }

	public bool? EnablePreviewStabilization { get; init; }

	public bool PreferOisHardware { get; init; }

	public string ShortStatus { get; init; } = "Estabilización: sistema";

	public string Detail { get; init; } = string.Empty;
}
