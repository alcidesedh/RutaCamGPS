namespace GPSCamRoute.Models;

public sealed class CameraXHudOptions
{
	public bool Enabled { get; init; } = true;

	public bool ShowLogo { get; init; } = true;

	public bool ShowMap { get; init; } = true;

	public bool ShowRec { get; init; } = false;

	public bool ShowSpeed { get; init; } = true;

	public bool ShowDistance { get; init; } = true;

	public bool ShowTimer { get; init; } = true;

	public bool ShowAverageSpeed { get; init; }

	public bool ShowMaxSpeed { get; init; }

	public bool ShowGpsStatus { get; init; }

	public bool ShowCoordinates { get; init; }

	public bool ShowAltitude { get; init; }

	public bool ShowDateTime { get; init; }

	public string DateTimeCorner { get; init; } = "Inferior derecha";

	public bool ShowBatteryStatus { get; init; }

	public bool ShowStorageStatus { get; init; }

	public double LogoOpacity { get; init; } = 0.92;

	public double LogoWidthDp { get; init; } = 88.0;

	public string CustomLogoPath { get; init; } = string.Empty;

	public double MapOpacity { get; init; } = 0.92;

	public double MapWidthDp { get; init; } = 150.0;

	public string MapCorner { get; init; } = "Superior derecha";

	public string MapShape { get; init; } = "Circular";
}
