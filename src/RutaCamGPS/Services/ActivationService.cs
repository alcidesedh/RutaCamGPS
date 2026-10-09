using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using GPSCamRoute.Models;
using Microsoft.Maui.Storage;
using RutaCam.Core.Licensing;

namespace GPSCamRoute.Services;

public sealed class ActivationService
{
	private const string ExpiryKey = "FullActivationExpiresUtc";
	private const string InstallationIdKey = "ActivationInstallationId";
	private const string InitialRequestShownKey = "ActivationInitialRequestShown";
	private const string ActiveLicenseCodeKey = "ActiveLicenseCode";
	private const string ActivationWhatsAppNumber = "584142368774";

	public bool IsFullAccess
	{
		get
		{
			string code = Preferences.Default.Get(ActiveLicenseCodeKey, string.Empty);
			if (string.IsNullOrWhiteSpace(code))
			{
				return false;
			}

			string deviceId = GetOrCreateInstallationId();
			if (!LicenseCodec.TryParseAndVerify(code, deviceId, MasterPublicKey.Instance, out var payload, out _))
			{
				return false;
			}

			return payload != null && !payload.IsExpired(DateTimeOffset.UtcNow);
		}
	}

	public DateTimeOffset? ExpiresAtLocal
	{
		get
		{
			string code = Preferences.Default.Get(ActiveLicenseCodeKey, string.Empty);
			if (string.IsNullOrWhiteSpace(code))
			{
				return null;
			}

			string deviceId = GetOrCreateInstallationId();
			if (LicenseCodec.TryParseAndVerify(code, deviceId, MasterPublicKey.Instance, out var payload, out _) && payload != null)
			{
				return payload.IsLifetime ? null : payload.ExpiresAtUtc.ToLocalTime();
			}

			return null;
		}
	}

	public bool IsLifetime
	{
		get
		{
			string code = Preferences.Default.Get(ActiveLicenseCodeKey, string.Empty);
			if (string.IsNullOrWhiteSpace(code))
			{
				return false;
			}

			string deviceId = GetOrCreateInstallationId();
			return LicenseCodec.TryParseAndVerify(code, deviceId, MasterPublicKey.Instance, out var payload, out _) 
				&& payload != null && payload.IsLifetime;
		}
	}

	public bool ShouldShowInitialRequest => !IsFullAccess && !Preferences.Default.Get(InitialRequestShownKey, defaultValue: false);

	public void MarkInitialRequestShown()
	{
		Preferences.Default.Set(InitialRequestShownKey, value: true);
	}

	public ActivationResult TryActivate(string? suppliedCode)
	{
		if (string.IsNullOrWhiteSpace(suppliedCode))
		{
			return new ActivationResult
			{
				Success = false,
				Message = "Por favor ingresa un código de activación."
			};
		}

		string deviceId = GetOrCreateInstallationId();
		bool valid = LicenseCodec.TryParseAndVerify(
			suppliedCode.Trim(),
			deviceId,
			MasterPublicKey.Instance,
			out var payload,
			out var errorMessage
		);

		if (!valid || payload == null)
		{
			return new ActivationResult
			{
				Success = false,
				Message = errorMessage ?? "El código de activación no es válido para este dispositivo."
			};
		}

		Preferences.Default.Set(ActiveLicenseCodeKey, suppliedCode.Trim());
		Preferences.Default.Set(ExpiryKey, payload.ExpiresAtUtc == DateTimeOffset.MaxValue ? long.MaxValue : payload.ExpiresAtUtc.ToUnixTimeSeconds());

		string expiryText = payload.IsLifetime 
			? "acceso permanente" 
			: $"activado hasta el {payload.ExpiresAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

		return new ActivationResult
		{
			Success = true,
			ExpiresAt = payload.IsLifetime ? null : payload.ExpiresAtUtc.ToLocalTime(),
			Message = $"¡Acceso {payload.Tier} desbloqueado con éxito ({expiryText})!"
		};
	}

	public Task<bool> OpenWhatsAppRequestAsync()
	{
		string message = BuildWhatsAppRequestMessage();
		return Task.FromResult(OpenWhatsAppOnAndroid(message));
	}

	private static bool OpenWhatsAppOnAndroid(string message)
	{
		string encodedMessage = System.Uri.EscapeDataString(message);
		global::Android.Content.Context context = global::Android.App.Application.Context;
		string nativeAddress = $"whatsapp://send?phone={ActivationWhatsAppNumber}&text=" + encodedMessage;

		if (TryStartAndroidIntent(context, nativeAddress, "com.whatsapp"))
		{
			return true;
		}
		if (TryStartAndroidIntent(context, nativeAddress, "com.whatsapp.w4b"))
		{
			return true;
		}

		string webAddress = $"https://api.whatsapp.com/send?phone={ActivationWhatsAppNumber}&text=" + encodedMessage;
		if (TryStartAndroidIntent(context, webAddress, "com.whatsapp"))
		{
			return true;
		}
		if (TryStartAndroidIntent(context, webAddress, "com.whatsapp.w4b"))
		{
			return true;
		}
		return TryStartAndroidIntent(context, webAddress, null);
	}

	private static bool TryStartAndroidIntent(global::Android.Content.Context context, string address, string? packageName)
	{
		try
		{
			global::Android.Net.Uri? uri = global::Android.Net.Uri.Parse(address);
			if (uri == null)
			{
				return false;
			}
			using var intent = new Intent(Intent.ActionView, uri);
			if (!string.IsNullOrWhiteSpace(packageName))
			{
				intent.SetPackage(packageName);
			}
			intent.AddFlags(ActivityFlags.NewTask);
			context.StartActivity(intent);
			return true;
		}
		catch (ActivityNotFoundException)
		{
			return false;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public string BuildWhatsAppRequestMessage()
	{
		string id = GetOrCreateInstallationId();
		return $"Hola, he instalado RutaCam GPS. Mi ID de dispositivo es [{id}], solicito el código de activación.";
	}

	public void ApplyLimitedRestrictions(OverlaySettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings, nameof(settings));
		if (!string.Equals(settings.DefaultCameraPosition, "Trasera", StringComparison.OrdinalIgnoreCase) && !string.Equals(settings.DefaultCameraPosition, "Frontal", StringComparison.OrdinalIgnoreCase))
		{
			settings.DefaultCameraPosition = "Trasera";
		}

		if (!IsFullAccess)
		{
			settings.DefaultCameraPosition = "Trasera";
			if (string.Equals(settings.CameraXResolution, "4K · UHD", StringComparison.OrdinalIgnoreCase))
			{
				settings.CameraXResolution = "1080p · FHD";
			}
			settings.CameraXFrameRate = "30 FPS";
			if (settings.CameraXHudMode.StartsWith("Completo", StringComparison.OrdinalIgnoreCase))
			{
				settings.CameraXHudMode = "Básico · incrustado";
			}
			settings.VideoQuality = "Ligera · hasta 720×1280";
			settings.AudioInputDeviceId = "phone";
			settings.AudioInputDisplayName = "Micrófono del teléfono";
			settings.VideoSegmentation = "5 minutos";
			settings.LoopRecording = "1 hora";
			settings.ShowLogo = true;
			settings.CustomLogoPath = string.Empty;
			settings.LogoWidth = 88.0;
			settings.LogoOpacity = 0.8;
			settings.ShowAverageSpeed = false;
			settings.ShowMaxSpeed = false;
			settings.ShowGpsStatus = false;
			settings.ShowCoordinates = false;
			settings.ShowAltitude = false;
			settings.ShowMap = true;
			settings.MapShape = "Cuadrado";
			settings.MapCorner = "Superior derecha";
			settings.MapOpacity = 0.8;
		}
	}

	public static string GetOrCreateInstallationId()
	{
		string existing = Preferences.Default.Get(InstallationIdKey, string.Empty);
		if (!string.IsNullOrWhiteSpace(existing))
		{
			return Base32Crockford.Normalize(existing);
		}

		byte[] randomBytes = new byte[5];
		RandomNumberGenerator.Fill(randomBytes);
		string generated = Base32Crockford.Encode(randomBytes);
		Preferences.Default.Set(InstallationIdKey, generated);
		return generated;
	}
}
