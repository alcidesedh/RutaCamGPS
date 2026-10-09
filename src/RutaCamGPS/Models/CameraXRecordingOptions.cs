using System;

namespace GPSCamRoute.Models;

public sealed class CameraXRecordingOptions
{
	public string Resolution { get; init; } = "1080p · FHD";

	public int TargetFps { get; init; } = 30;

	public string Stabilization { get; init; } = "Automática inteligente · OIS/EIS";

	public string RutaCamSoftwareStabilization { get; init; } = "Desactivada";

	public string RutaCamGpuStabilization { get; init; } = "Desactivada";

	public string PhysicalCameraId { get; init; } = string.Empty;

	public string LensDisplayName { get; init; } = "Automática · cámara trasera";

	public bool IsRutaCamSoftwareStabilizationEnabled => RutaCamSoftwareStabilization.StartsWith("RutaCam", StringComparison.OrdinalIgnoreCase);

	public bool IsRutaCamGpuStabilizationEnabled => RutaCamGpuStabilization.StartsWith("GPU", StringComparison.OrdinalIgnoreCase);

	public bool IsCompatibilityProfile => Stabilization.StartsWith("Perfil recomendado", StringComparison.OrdinalIgnoreCase);

	public bool IsSmartAutomatic => Stabilization.StartsWith("Automática inteligente", StringComparison.OrdinalIgnoreCase) || Stabilization.StartsWith("Automática optimizada", StringComparison.OrdinalIgnoreCase) || string.Equals(Stabilization, "Automática", StringComparison.OrdinalIgnoreCase);

	public bool IsDashcamStable => Stabilization.StartsWith("Dashcam estable", StringComparison.OrdinalIgnoreCase) || Stabilization.StartsWith("Reforzada", StringComparison.OrdinalIgnoreCase);

	public bool IsDigitalCameraX => Stabilization.StartsWith("Digital CameraX", StringComparison.OrdinalIgnoreCase) || Stabilization.StartsWith("Activada", StringComparison.OrdinalIgnoreCase);

	public bool IsOisPreferred => Stabilization.StartsWith("OIS hardware", StringComparison.OrdinalIgnoreCase);

	public bool IsDigitalOff => Stabilization.StartsWith("Desactivada", StringComparison.OrdinalIgnoreCase);

	public string EffectiveResolution => (IsDashcamStable || IsCompatibilityProfile || IsRutaCamSoftwareStabilizationEnabled || IsRutaCamGpuStabilizationEnabled) ? "1080p · FHD" : Resolution;

	public int EffectiveTargetFps => (IsDashcamStable || IsCompatibilityProfile || IsRutaCamSoftwareStabilizationEnabled || IsRutaCamGpuStabilizationEnabled) ? 30 : TargetFps;

	public CameraStabilizationDecision ResolveStabilization(bool useFrontCamera, CameraStabilizationCapabilities capabilities)
	{
		if (IsRutaCamGpuStabilizationEnabled)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				EnablePreviewStabilization = false,
				PreferOisHardware = false,
				ShortStatus = "GPU Stabilizer V2 FIX27 · OIS/EIS OFF",
				Detail = "FIX27 conserva ejes/roll aprobados y reajusta la respuesta de Suave/Equilibrada/Fuerte; no fuerza estabilización del fabricante."
			};
		}
		if (IsRutaCamSoftwareStabilizationEnabled)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				EnablePreviewStabilization = false,
				PreferOisHardware = false,
				ShortStatus = "RutaCam Stabilizer · hardware OFF",
				Detail = "El motor propio usa giroscopio y recorte dinámico; no fuerza OIS/EIS simultáneamente."
			};
		}
		if (IsCompatibilityProfile)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				EnablePreviewStabilization = false,
				PreferOisHardware = false,
				ShortStatus = "Perfil compatible · 1080p/30",
				Detail = ((capabilities.EffectiveVideoStabilizationAvailable || capabilities.EffectivePreviewStabilizationAvailable || capabilities.OisAvailable) ? "Perfil conservador de 1080p/30. No fuerza estabilización; puede cambiarse manualmente a un modo compatible detectado." : "Android no publica OIS/EIS/Preview Stabilization para esta cámara. RutaCam usa 1080p/30 sin forzar controles no disponibles.")
			};
		}
		if (useFrontCamera)
		{
			return new CameraStabilizationDecision
			{
				ShortStatus = "Estab. frontal: sistema",
				Detail = "RutaCam no fuerza estabilización en la cámara frontal."
			};
		}
		if (IsDigitalOff)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				EnablePreviewStabilization = false,
				ShortStatus = "EIS OFF",
				Detail = (capabilities.OisAvailable ? "Estabilización digital desactivada. OIS queda bajo control del HAL." : "Estabilización digital desactivada.")
			};
		}
		if (IsOisPreferred)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				PreferOisHardware = true,
				ShortStatus = (capabilities.OisAvailable ? "OIS preferido · EIS OFF" : "OIS no publicado · EIS OFF"),
				Detail = (capabilities.OisAvailable ? "Camera2 publica OIS. RutaCam desactiva EIS para evitar interacción OIS+EIS; el HAL controla la activación mecánica." : "La cámara seleccionada no publica OIS mediante Camera2.")
			};
		}
		if (IsDashcamStable)
		{
			if (capabilities.EffectivePreviewStabilizationAvailable)
			{
				return new CameraStabilizationDecision
				{
					EnablePreviewStabilization = true,
					ShortStatus = "Preview+video estabilizados",
					Detail = "Perfil 1080p/30 con Preview Stabilization del dispositivo."
				};
			}
			if (capabilities.EffectiveVideoStabilizationAvailable)
			{
				return new CameraStabilizationDecision
				{
					EnableVideoStabilization = true,
					ShortStatus = "EIS vídeo · 1080p/30",
					Detail = "Preview Stabilization no publicado; se activa estabilización de video CameraX."
				};
			}
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				PreferOisHardware = capabilities.OisAvailable,
				ShortStatus = (capabilities.OisAvailable ? "OIS disponible · EIS no" : "Sin estabilización publicada"),
				Detail = (capabilities.OisAvailable ? "No hay EIS publicado. Se evita forzarlo y se deja OIS bajo control del HAL." : "La cámara no publica OIS ni EIS.")
			};
		}
		if (IsDigitalCameraX)
		{
			return capabilities.EffectiveVideoStabilizationAvailable ? new CameraStabilizationDecision
			{
				EnableVideoStabilization = true,
				ShortStatus = "EIS CameraX",
				Detail = "Estabilización digital de video solicitada."
			} : new CameraStabilizationDecision
			{
				ShortStatus = "EIS no disponible",
				Detail = "Camera2 no publica estabilización digital de video."
			};
		}
		if (EffectiveTargetFps <= 30 && !EffectiveResolution.StartsWith("4K", StringComparison.OrdinalIgnoreCase) && capabilities.EffectiveVideoStabilizationAvailable)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = true,
				ShortStatus = (capabilities.OisAvailable ? "AUTO · EIS vídeo · OIS disponible" : "AUTO · EIS vídeo"),
				Detail = "RutaCam usa EIS CameraX en un perfil <=1080p/30. OIS no se fuerza simultáneamente."
			};
		}
		if (capabilities.OisAvailable)
		{
			return new CameraStabilizationDecision
			{
				EnableVideoStabilization = false,
				PreferOisHardware = true,
				ShortStatus = "AUTO · OIS preferido",
				Detail = "El perfil actual no garantiza EIS; se evita forzar EIS y se deja OIS al HAL."
			};
		}
		return new CameraStabilizationDecision
		{
			ShortStatus = "AUTO · sistema",
			Detail = "No se encontró un modo de estabilización seguro para forzar."
		};
	}
}
