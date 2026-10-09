namespace GPSCamRoute.Models;

public sealed class OverlaySettings
{
	public bool ShowLogo { get; set; } = true;

	public bool ShowMap { get; set; } = true;

	public bool ShowSpeed { get; set; } = true;

	public bool ShowDistance { get; set; } = true;

	public bool ShowTimer { get; set; } = true;

	public bool ShowRec { get; set; } = false;

	public bool ShowGpsStatus { get; set; } = false;

	public bool ShowCoordinates { get; set; }

	public bool ShowAltitude { get; set; }

	public bool ShowDateTime { get; set; }

	public string DateTimeCorner { get; set; } = "Inferior derecha";

	public bool ShowAverageSpeed { get; set; }

	public bool ShowMaxSpeed { get; set; }

	public bool ShowBatteryStatus { get; set; } = false;

	public bool ShowStorageStatus { get; set; } = false;

	public double LogoOpacity { get; set; } = 0.92;

	public string CustomLogoPath { get; set; } = string.Empty;

	public double LogoWidth { get; set; } = 88.0;

	public double MapOpacity { get; set; } = 0.92;

	public double MapWidth { get; set; } = 150.0;

	public string MapCorner { get; set; } = "Superior derecha";

	public string MapShape { get; set; } = "Circular";

	public string CameraPreviewMode { get; set; } = "Llenar pantalla";

	public string DefaultCameraPosition { get; set; } = "Trasera";

	public double DefaultZoomFactor { get; set; } = 1.0;

	public bool ShowCameraControls { get; set; } = true;

	public string VideoQuality { get; set; } = "Alta · hasta 1080×1920";

	public bool HideSystemBarsDuringRecording { get; set; } = true;

	public bool LockOrientationDuringRecording { get; set; } = true;

	public bool RecordAudio { get; set; } = true;

	public string AudioInputDeviceId { get; set; } = "automatic";

	public string AudioInputDisplayName { get; set; } = "Automática · Android decide";

	public string VideoSegmentation { get; set; } = "5 minutos";

	public string LoopRecording { get; set; } = "Desactivada";

	public bool ProtectEventContext { get; set; } = true;

	public string RecordingEngine { get; set; } = "CameraX · recomendado";

	public string CameraXResolution { get; set; } = "1080p · FHD";

	public string CameraXFrameRate { get; set; } = "30 FPS";

	public string CameraXStabilization { get; set; } = "Automática inteligente · OIS/EIS";

	public string RutaCamSoftwareStabilization { get; set; } = "Desactivada";

	public string RutaCamGpuStabilization { get; set; } = "Desactivada";

	public string CameraXPhysicalCameraId { get; set; } = string.Empty;

	public string CameraXLensDisplayName { get; set; } = "Automática · cámara trasera";

	public string CameraXHudMode { get; set; } = "Básico · incrustado";

	public double MinimumFreeStorageGb { get; set; } = 1.0;

	public bool AutoStopOnCriticalStorage { get; set; } = true;

	public double CriticalFreeStorageMb { get; set; } = 250.0;

	public bool WarnOnLowBattery { get; set; } = true;

	public string GpsFilterProfile { get; set; } = "Equilibrado";

	public bool RequireGpsFixBeforeRecording { get; set; } = false;

	public string RecordingMode { get; set; } = "Con telemetría";
}
