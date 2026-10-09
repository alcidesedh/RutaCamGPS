namespace GPSCamRoute.Models;

public sealed class CameraLensOption
{
	public string DisplayName { get; init; } = "Automática · cámara trasera";

	public string PhysicalCameraId { get; init; } = string.Empty;

	public bool IsAutomatic => string.IsNullOrWhiteSpace(PhysicalCameraId);

	public override string ToString()
	{
		return DisplayName;
	}
}
