using System;
using GPSCamRoute.Models;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class OverlaySettingsService
{
	private readonly ActivationService _activationService;

	public OverlaySettingsService(ActivationService activationService)
	{
		_activationService = activationService;
	}

	public OverlaySettings Load()
	{
		OverlaySettings settings = new OverlaySettings
		{
			ShowLogo = Preferences.Default.Get("ShowLogo", defaultValue: true),
			ShowMap = Preferences.Default.Get("ShowMap", defaultValue: true),
			ShowSpeed = Preferences.Default.Get("ShowSpeed", defaultValue: true),
			ShowDistance = Preferences.Default.Get("ShowDistance", defaultValue: true),
			ShowTimer = Preferences.Default.Get("ShowTimer", defaultValue: true),
			ShowRec = false,
			ShowGpsStatus = Preferences.Default.Get("ShowGpsStatus", defaultValue: false),
			ShowCoordinates = Preferences.Default.Get("ShowCoordinates", defaultValue: false),
			ShowAltitude = Preferences.Default.Get("ShowAltitude", defaultValue: false),
			ShowDateTime = Preferences.Default.Get("ShowDateTime", defaultValue: false),
			DateTimeCorner = Preferences.Default.Get("DateTimeCorner", "Inferior derecha"),
			ShowAverageSpeed = Preferences.Default.Get("ShowAverageSpeed", defaultValue: false),
			ShowMaxSpeed = Preferences.Default.Get("ShowMaxSpeed", defaultValue: false),
			ShowBatteryStatus = false,
			ShowStorageStatus = false,
			LogoOpacity = Preferences.Default.Get("LogoOpacity", 0.92),
			CustomLogoPath = Preferences.Default.Get("CustomLogoPath", string.Empty),
			LogoWidth = Preferences.Default.Get("LogoWidth", 88.0),
			MapOpacity = Preferences.Default.Get("MapOpacity", 0.92),
			MapWidth = Preferences.Default.Get("MapWidth", 150.0),
			MapCorner = Preferences.Default.Get("MapCorner", "Superior derecha"),
			MapShape = Preferences.Default.Get("MapShape", "Circular"),
			CameraPreviewMode = Preferences.Default.Get("CameraPreviewMode", "Llenar pantalla"),
			DefaultCameraPosition = Preferences.Default.Get("DefaultCameraPosition", "Trasera"),
			DefaultZoomFactor = Preferences.Default.Get("DefaultZoomFactor", 1.0),
			ShowCameraControls = Preferences.Default.Get("ShowCameraControls", defaultValue: true),
			VideoQuality = Preferences.Default.Get("VideoQuality", "Alta · hasta 1080×1920"),
			HideSystemBarsDuringRecording = Preferences.Default.Get("HideSystemBarsDuringRecording", defaultValue: true),
			LockOrientationDuringRecording = Preferences.Default.Get("LockOrientationDuringRecording", defaultValue: true),
			RecordAudio = Preferences.Default.Get("RecordAudio", defaultValue: true),
			AudioInputDeviceId = Preferences.Default.Get("AudioInputDeviceId", "automatic"),
			AudioInputDisplayName = Preferences.Default.Get("AudioInputDisplayName", "Automática · Android decide"),
			VideoSegmentation = Preferences.Default.Get("VideoSegmentation", "5 minutos"),
			LoopRecording = Preferences.Default.Get("LoopRecording", "Desactivada"),
			ProtectEventContext = Preferences.Default.Get("ProtectEventContext", defaultValue: true),
			RecordingEngine = NormalizeRecordingEngine(Preferences.Default.Get("RecordingEngine", "CameraX · recomendado")),
			CameraXResolution = Preferences.Default.Get("CameraXResolution", "1080p · FHD"),
			CameraXFrameRate = Preferences.Default.Get("CameraXFrameRate", "30 FPS"),
			CameraXStabilization = NormalizeCameraXStabilization(Preferences.Default.Get("CameraXStabilization", "Automática inteligente · OIS/EIS")),
			RutaCamSoftwareStabilization = NormalizeRutaCamSoftwareStabilization(Preferences.Default.Get("RutaCamSoftwareStabilization", "Desactivada")),
			RutaCamGpuStabilization = NormalizeRutaCamGpuStabilization(Preferences.Default.Get("RutaCamGpuStabilization", "Desactivada")),
			CameraXPhysicalCameraId = string.Empty,
			CameraXLensDisplayName = "Automática · cámara trasera",
			CameraXHudMode = Preferences.Default.Get("CameraXHudMode", "Básico · incrustado"),
			MinimumFreeStorageGb = Preferences.Default.Get("MinimumFreeStorageGb", 1.0),
			AutoStopOnCriticalStorage = Preferences.Default.Get("AutoStopOnCriticalStorage", defaultValue: true),
			CriticalFreeStorageMb = Preferences.Default.Get("CriticalFreeStorageMb", 250.0),
			WarnOnLowBattery = Preferences.Default.Get("WarnOnLowBattery", defaultValue: true),
			GpsFilterProfile = Preferences.Default.Get("GpsFilterProfile", "Equilibrado"),
			RequireGpsFixBeforeRecording = false,
			RecordingMode = Preferences.Default.Get("RecordingMode", "Con telemetría")
		};
		_activationService.ApplyLimitedRestrictions(settings);
		return settings;
	}

	public void Save(OverlaySettings s)
	{
		_activationService.ApplyLimitedRestrictions(s);
		Preferences.Default.Set("ShowLogo", s.ShowLogo);
		Preferences.Default.Set("ShowMap", s.ShowMap);
		Preferences.Default.Set("ShowSpeed", s.ShowSpeed);
		Preferences.Default.Set("ShowDistance", s.ShowDistance);
		Preferences.Default.Set("ShowTimer", s.ShowTimer);
		Preferences.Default.Set("ShowRec", value: false);
		Preferences.Default.Set("ShowGpsStatus", s.ShowGpsStatus);
		Preferences.Default.Set("ShowCoordinates", s.ShowCoordinates);
		Preferences.Default.Set("ShowAltitude", s.ShowAltitude);
		Preferences.Default.Set("ShowDateTime", s.ShowDateTime);
		Preferences.Default.Set("DateTimeCorner", s.DateTimeCorner ?? "Inferior derecha");
		Preferences.Default.Set("ShowAverageSpeed", s.ShowAverageSpeed);
		Preferences.Default.Set("ShowMaxSpeed", s.ShowMaxSpeed);
		Preferences.Default.Set("ShowBatteryStatus", value: false);
		Preferences.Default.Set("ShowStorageStatus", value: false);
		Preferences.Default.Set("LogoOpacity", s.LogoOpacity);
		Preferences.Default.Set("CustomLogoPath", s.CustomLogoPath ?? string.Empty);
		Preferences.Default.Set("LogoWidth", s.LogoWidth);
		Preferences.Default.Set("MapOpacity", s.MapOpacity);
		Preferences.Default.Set("MapWidth", s.MapWidth);
		Preferences.Default.Set("MapCorner", s.MapCorner);
		Preferences.Default.Set("MapShape", s.MapShape);
		Preferences.Default.Set("CameraPreviewMode", s.CameraPreviewMode);
		Preferences.Default.Set("DefaultCameraPosition", s.DefaultCameraPosition);
		Preferences.Default.Set("DefaultZoomFactor", s.DefaultZoomFactor);
		Preferences.Default.Set("ShowCameraControls", s.ShowCameraControls);
		Preferences.Default.Set("VideoQuality", s.VideoQuality);
		Preferences.Default.Set("HideSystemBarsDuringRecording", s.HideSystemBarsDuringRecording);
		Preferences.Default.Set("LockOrientationDuringRecording", s.LockOrientationDuringRecording);
		Preferences.Default.Set("RecordAudio", s.RecordAudio);
		Preferences.Default.Set("AudioInputDeviceId", s.AudioInputDeviceId ?? "automatic");
		Preferences.Default.Set("AudioInputDisplayName", s.AudioInputDisplayName ?? "Automática · Android decide");
		Preferences.Default.Set("VideoSegmentation", s.VideoSegmentation);
		Preferences.Default.Set("LoopRecording", s.LoopRecording);
		Preferences.Default.Set("ProtectEventContext", s.ProtectEventContext);
		Preferences.Default.Set("RecordingEngine", NormalizeRecordingEngine(s.RecordingEngine));
		Preferences.Default.Set("CameraXResolution", s.CameraXResolution);
		Preferences.Default.Set("CameraXFrameRate", s.CameraXFrameRate);
		Preferences.Default.Set("CameraXStabilization", s.CameraXStabilization);
		Preferences.Default.Set("RutaCamSoftwareStabilization", NormalizeRutaCamSoftwareStabilization(s.RutaCamSoftwareStabilization));
		Preferences.Default.Set("RutaCamGpuStabilization", NormalizeRutaCamGpuStabilization(s.RutaCamGpuStabilization));
		Preferences.Default.Set("CameraXPhysicalCameraId", string.Empty);
		Preferences.Default.Set("CameraXLensDisplayName", "Automática · cámara trasera");
		Preferences.Default.Set("CameraXHudMode", s.CameraXHudMode);
		Preferences.Default.Set("MinimumFreeStorageGb", s.MinimumFreeStorageGb);
		Preferences.Default.Set("AutoStopOnCriticalStorage", s.AutoStopOnCriticalStorage);
		Preferences.Default.Set("CriticalFreeStorageMb", s.CriticalFreeStorageMb);
		Preferences.Default.Set("WarnOnLowBattery", s.WarnOnLowBattery);
		Preferences.Default.Set("GpsFilterProfile", s.GpsFilterProfile);
		Preferences.Default.Set("RequireGpsFixBeforeRecording", value: false);
		Preferences.Default.Set("RecordingMode", s.RecordingMode);
	}

	private static string NormalizeRutaCamGpuStabilization(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Desactivada";
		}
		if (value.Contains("Fuerte", StringComparison.OrdinalIgnoreCase))
		{
			return "GPU Fuerte · experimental";
		}
		if (value.Contains("Suave", StringComparison.OrdinalIgnoreCase))
		{
			return "GPU Suave · experimental";
		}
		if (value.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
		{
			return "GPU Equilibrada · experimental";
		}
		return "Desactivada";
	}

	private static string NormalizeRutaCamSoftwareStabilization(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Desactivada";
		}
		if (value.Contains("Fuerte", StringComparison.OrdinalIgnoreCase))
		{
			return "RutaCam Fuerte · experimental";
		}
		if (value.Contains("Suave", StringComparison.OrdinalIgnoreCase))
		{
			return "RutaCam Suave · experimental";
		}
		if (value.StartsWith("RutaCam", StringComparison.OrdinalIgnoreCase))
		{
			return "RutaCam Equilibrada · experimental";
		}
		return "Desactivada";
	}

	private static string NormalizeCameraXStabilization(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Automática", StringComparison.OrdinalIgnoreCase) || value.StartsWith("Automática optimizada", StringComparison.OrdinalIgnoreCase))
		{
			return "Automática inteligente · OIS/EIS";
		}
		if (value.StartsWith("Perfil recomendado", StringComparison.OrdinalIgnoreCase))
		{
			return "Perfil recomendado · 1080p/30";
		}
		if (value.StartsWith("Reforzada", StringComparison.OrdinalIgnoreCase))
		{
			return "Dashcam estable · 1080p/30";
		}
		if (value.StartsWith("Activada", StringComparison.OrdinalIgnoreCase))
		{
			return "Digital CameraX · EIS";
		}
		if (value.StartsWith("Desactivada", StringComparison.OrdinalIgnoreCase))
		{
			return "Desactivada · digital";
		}
		return value;
	}

	private static string NormalizeRecordingEngine(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "CameraX · recomendado";
		}
		if (value.StartsWith("CameraX", StringComparison.OrdinalIgnoreCase))
		{
			return "CameraX · recomendado";
		}
		if (value.Contains("MediaProjection", StringComparison.OrdinalIgnoreCase))
		{
			return "Compatibilidad · MediaProjection";
		}
		return "CameraX · recomendado";
	}
}
