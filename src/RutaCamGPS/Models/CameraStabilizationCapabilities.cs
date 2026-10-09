namespace GPSCamRoute.Models;

public sealed class CameraStabilizationCapabilities
{
	public bool IsAvailable { get; init; }

	public string CameraId { get; init; } = string.Empty;

	public string DeviceName { get; init; } = string.Empty;

	public string HardwareLevel { get; init; } = "Desconocido";

	public bool OisAvailable { get; init; }

	public bool EisAvailable { get; init; }

	public bool PreviewStabilizationAvailable { get; init; }

	public bool FreeformCropAvailable { get; init; }

	public string CropTypeText { get; init; } = "Desconocido";

	public bool GyroscopeAvailable { get; init; }

	public bool AccelerometerAvailable { get; init; }

	public bool OpenGlEs2Available { get; init; }

	public bool RutaCamGpuStabilizerAvailable => IsAvailable && OpenGlEs2Available && GyroscopeAvailable;

	public bool RutaCamSoftwareStabilizerAvailable => IsAvailable && FreeformCropAvailable && GyroscopeAvailable;

	public bool? CameraXVideoStabilizationAvailable { get; init; }

	public bool? CameraXPreviewStabilizationAvailable { get; init; }

	public string CameraXStatus { get; init; } = "No consultado";

	public bool EffectiveVideoStabilizationAvailable => CameraXVideoStabilizationAvailable == true || EisAvailable;

	public bool EffectivePreviewStabilizationAvailable => CameraXPreviewStabilizationAvailable == true || PreviewStabilizationAvailable;

	public string? Error { get; init; }

	public string Summary
	{
		get
		{
			if (!IsAvailable)
			{
				return string.IsNullOrWhiteSpace(Error) ? "No se pudieron leer las capacidades de estabilización." : ("No disponible: " + Error);
			}
			string ois = (OisAvailable ? "Sí" : "No");
			string eis = (EisAvailable ? "Sí" : "No");
			string preview = (PreviewStabilizationAvailable ? "Sí" : "No");
			string cxVideo = ((!CameraXVideoStabilizationAvailable.HasValue) ? "?" : (CameraXVideoStabilizationAvailable.Value ? "Sí" : "No"));
			string cxPreview = ((!CameraXPreviewStabilizationAvailable.HasValue) ? "?" : (CameraXPreviewStabilizationAvailable.Value ? "Sí" : "No"));
			string rc = (RutaCamSoftwareStabilizerAvailable ? "Sí" : "No");
			string gpu = (RutaCamGpuStabilizerAvailable ? "Sí" : "No");
			string gyro = (GyroscopeAvailable ? "Sí" : "No");
			string accel = (AccelerometerAvailable ? "Sí" : "No");
			string gl = (OpenGlEs2Available ? "Sí" : "No");
			return $"{DeviceName} · Cámara {CameraId} · {HardwareLevel}\nCamera2 → OIS: {ois} · EIS: {eis} · Preview: {preview}\nCameraX → Video: {cxVideo} · Preview: {cxPreview}\nRutaCam V1 → Crop: {CropTypeText} · Gyro: {gyro} · Accel: {accel} · Disponible: {rc}\nRutaCam GPU V2 → GLES2: {gl} · Gyro: {gyro} · Disponible: {gpu}";
		}
	}

	public static CameraStabilizationCapabilities Unavailable(string message)
	{
		return new CameraStabilizationCapabilities
		{
			IsAvailable = false,
			Error = message
		};
	}
}
