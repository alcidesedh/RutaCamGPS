using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Hardware.Camera2;
using Android.OS;
using Android.Runtime;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using AndroidX.Core.Content;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Google.Common.Util.Concurrent;
using Java.Lang;
using Java.Util.Concurrent;

namespace GPSCamRoute.Platforms.Android;

public sealed class CameraStabilizationService : ICameraStabilizationService
{
	private readonly record struct CameraXProbeResult(bool? Video, bool? Preview, string Status);

	public async Task<CameraStabilizationCapabilities> GetCapabilitiesAsync(bool useFrontCamera = false, string? physicalCameraId = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			Context context = Application.Context;
			if (!(context.GetSystemService("camera") is CameraManager manager))
			{
				return CameraStabilizationCapabilities.Unavailable("CameraManager no disponible.");
			}
			string desiredFacing = (useFrontCamera ? "0" : "1");
			string[] cameraIds = manager.GetCameraIdList();
			string selectedId = null;
			CameraCharacteristics selectedCharacteristics = null;
			if (!string.IsNullOrWhiteSpace(physicalCameraId))
			{
				try
				{
					selectedCharacteristics = manager.GetCameraCharacteristics(physicalCameraId);
					selectedId = physicalCameraId;
				}
				catch
				{
					selectedCharacteristics = null;
					selectedId = null;
				}
			}
			if (selectedCharacteristics == null)
			{
				string[] array = cameraIds;
				foreach (string cameraId in array)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);
					string facing = ReadFacingValue(characteristics);
					if (string.Equals(facing, desiredFacing, StringComparison.Ordinal))
					{
						selectedId = cameraId;
						selectedCharacteristics = characteristics;
						break;
					}
				}
			}
			if (selectedCharacteristics == null || string.IsNullOrWhiteSpace(selectedId))
			{
				return CameraStabilizationCapabilities.Unavailable(useFrontCamera ? "No se encontró una cámara frontal." : "No se encontró una cámara trasera.");
			}
			int[] oisModes = ReadIntArray(selectedCharacteristics, CameraCharacteristics.LensInfoAvailableOpticalStabilization);
			int[] videoModes = ReadIntArray(selectedCharacteristics, CameraCharacteristics.ControlAvailableVideoStabilizationModes);
			bool camera2Ois = ((ReadOnlySpan<int>)oisModes).Contains(1);
			bool camera2Eis = ((ReadOnlySpan<int>)videoModes).Contains(1);
			bool camera2Preview = OperatingSystem.IsAndroidVersionAtLeast(33) && ((ReadOnlySpan<int>)videoModes).Contains(2);
			CameraXProbeResult cameraX = await QueryCameraXAsync(useFrontCamera, physicalCameraId, cancellationToken);
			(bool IsFreeform, string Text) cropType = ReadScalerCroppingType(selectedCharacteristics);
			SensorManager sensorManager = context.GetSystemService("sensor") as SensorManager;
			bool gyroAvailable = sensorManager?.GetDefaultSensor(SensorType.Gyroscope) != null;
			bool accelerometerAvailable = sensorManager?.GetDefaultSensor(SensorType.Accelerometer) != null;
			bool openGlEs2Available = ((context.GetSystemService("activity") as ActivityManager)?.DeviceConfigurationInfo?.ReqGlEsVersion).GetValueOrDefault() >= 131072;
			return new CameraStabilizationCapabilities
			{
				IsAvailable = true,
				CameraId = selectedId,
				DeviceName = GetDeviceName(),
				HardwareLevel = ReadHardwareLevel(selectedCharacteristics),
				OisAvailable = camera2Ois,
				EisAvailable = camera2Eis,
				PreviewStabilizationAvailable = camera2Preview,
				FreeformCropAvailable = cropType.IsFreeform,
				CropTypeText = cropType.Text,
				GyroscopeAvailable = gyroAvailable,
				AccelerometerAvailable = accelerometerAvailable,
				OpenGlEs2Available = openGlEs2Available,
				CameraXVideoStabilizationAvailable = cameraX.Video,
				CameraXPreviewStabilizationAvailable = cameraX.Preview,
				CameraXStatus = cameraX.Status
			};
		}
		catch (System.OperationCanceledException)
		{
			throw;
		}
		catch (System.Exception ex2)
		{
			System.Exception ex3 = ex2;
			return CameraStabilizationCapabilities.Unavailable(ex3.Message);
		}
	}

	public async Task<CameraDiagnosticReport> GetDiagnosticReportAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<CameraDiagnosticEntry> entries = new List<CameraDiagnosticEntry>();
		HashSet<string> seenPhysical = new HashSet<string>(StringComparer.Ordinal);
		try
		{
			Context context = Application.Context;
			if (!(context.GetSystemService("camera") is CameraManager manager))
			{
				return new CameraDiagnosticReport
				{
					DeviceName = GetDeviceName(),
					GeneralError = "CameraManager no disponible."
				};
			}
			string[] cameraIds = manager.GetCameraIdList();
			foreach (string logicalId in cameraIds.OrderBy<string, string>((string x) => x, StringComparer.Ordinal))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CameraCharacteristics characteristics;
				try
				{
					characteristics = manager.GetCameraCharacteristics(logicalId);
				}
				catch (System.Exception ex)
				{
					entries.Add(new CameraDiagnosticEntry
					{
						CameraId = logicalId,
						Kind = "Cámara",
						Error = ex.Message
					});
					continue;
				}
				string facingValue = ReadFacingValue(characteristics);
				string facingText = FacingToText(facingValue);
				IReadOnlyList<string> physicalIds = GetPhysicalIds(characteristics);
				entries.Add(CreateEntry(cameraX: await QueryCameraXForIdsAsync(facingValue, null, cancellationToken), cameraId: logicalId, kind: "Lógica", facing: facingText, parentLogicalCameraId: string.Empty, characteristics: characteristics, physicalIds: physicalIds));
				foreach (string physicalId in physicalIds)
				{
					if (!seenPhysical.Add(physicalId))
					{
						continue;
					}
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						CameraCharacteristics physicalCharacteristics = manager.GetCameraCharacteristics(physicalId);
						string physicalFacingValue = ReadFacingValue(physicalCharacteristics);
						if (string.IsNullOrWhiteSpace(physicalFacingValue))
						{
							physicalFacingValue = facingValue;
						}
						entries.Add(CreateEntry(cameraX: await QueryCameraXForIdsAsync(physicalFacingValue, physicalId, cancellationToken), cameraId: physicalId, kind: "Física", facing: FacingToText(physicalFacingValue), parentLogicalCameraId: logicalId, characteristics: physicalCharacteristics, physicalIds: Array.Empty<string>()));
					}
					catch (System.Exception ex2)
					{
						System.Exception ex3 = ex2;
						entries.Add(new CameraDiagnosticEntry
						{
							CameraId = physicalId,
							Kind = "Física",
							Facing = facingText,
							ParentLogicalCameraId = logicalId,
							CameraXStatus = "No consultado",
							Error = "Android publicó el ID físico, pero no permitió leer sus características: " + ex3.Message
						});
					}
				}
			}
			return new CameraDiagnosticReport
			{
				DeviceName = GetDeviceName(),
				GeneratedAt = DateTimeOffset.Now,
				Cameras = entries
			};
		}
		catch (System.OperationCanceledException)
		{
			throw;
		}
		catch (System.Exception ex2)
		{
			System.Exception ex5 = ex2;
			return new CameraDiagnosticReport
			{
				DeviceName = GetDeviceName(),
				GeneratedAt = DateTimeOffset.Now,
				Cameras = entries,
				GeneralError = ex5.Message
			};
		}
	}

	private static CameraDiagnosticEntry CreateEntry(string cameraId, string kind, string facing, string parentLogicalCameraId, CameraCharacteristics characteristics, IReadOnlyList<string> physicalIds, CameraXProbeResult cameraX)
	{
		int[] oisModes = ReadIntArray(characteristics, CameraCharacteristics.LensInfoAvailableOpticalStabilization);
		int[] videoModes = ReadIntArray(characteristics, CameraCharacteristics.ControlAvailableVideoStabilizationModes);
		return new CameraDiagnosticEntry
		{
			CameraId = cameraId,
			Kind = kind,
			Facing = facing,
			ParentLogicalCameraId = parentLogicalCameraId,
			HardwareLevel = ReadHardwareLevel(characteristics),
			FocalLengths = ReadFocalLengths(characteristics),
			PhysicalCameraIds = physicalIds,
			OisAvailable = ((ReadOnlySpan<int>)oisModes).Contains(1),
			EisAvailable = ((ReadOnlySpan<int>)videoModes).Contains(1),
			PreviewStabilizationAvailable = (OperatingSystem.IsAndroidVersionAtLeast(33) && ((ReadOnlySpan<int>)videoModes).Contains(2)),
			CropTypeText = ReadScalerCroppingType(characteristics).Text,
			CameraXVideoStabilizationAvailable = cameraX.Video,
			CameraXPreviewStabilizationAvailable = cameraX.Preview,
			CameraXStatus = cameraX.Status
		};
	}

	private static async Task<CameraXProbeResult> QueryCameraXAsync(bool useFrontCamera, string? physicalCameraId, CancellationToken cancellationToken)
	{
		return await QueryCameraXForIdsAsync(useFrontCamera ? "0" : "1", physicalCameraId, cancellationToken);
	}

	private static async Task<CameraXProbeResult> QueryCameraXForIdsAsync(string? facingValue, string? physicalCameraId, CancellationToken cancellationToken)
	{
		try
		{
			Context context = Application.Context;
			ProcessCameraProvider provider = await GetCameraProviderAsync(context, cancellationToken);
			CameraSelector selector = BuildCameraSelector(string.Equals(facingValue, "0", StringComparison.Ordinal), physicalCameraId);
			MethodInfo getCameraInfo = provider.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "GetCameraInfo", StringComparison.Ordinal) && m.GetParameters().Length == 1);
			if ((object)getCameraInfo == null)
			{
				return new CameraXProbeResult(null, null, "GetCameraInfo no expuesto por el binding.");
			}
			object cameraInfo = getCameraInfo.Invoke(provider, new object[1] { selector });
			if (cameraInfo == null)
			{
				return new CameraXProbeResult(null, null, "CameraX no devolvió CameraInfo.");
			}
			bool? video = QueryStaticCapability(typeof(Recorder), "GetVideoCapabilities", cameraInfo);
			bool? preview = QueryStaticCapability(typeof(AndroidX.Camera.Core.Preview), "GetPreviewCapabilities", cameraInfo);
			string status = "CameraX consultado" + (string.IsNullOrWhiteSpace(physicalCameraId) ? string.Empty : (" · física " + physicalCameraId));
			return new CameraXProbeResult(video, preview, status);
		}
		catch (TargetInvocationException ex)
		{
			TargetInvocationException ex2 = ex;
			return new CameraXProbeResult(null, null, ex2.InnerException?.Message ?? ex2.Message);
		}
		catch (System.Exception ex3)
		{
			System.Exception ex4 = ex3;
			return new CameraXProbeResult(null, null, ex4.Message);
		}
	}

	private static bool? QueryStaticCapability(Type ownerType, string methodName, object cameraInfo)
	{
		try
		{
			MethodInfo method = ownerType.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, methodName, StringComparison.Ordinal) && m.GetParameters().Length == 1);
			if ((object)method == null)
			{
				return null;
			}
			object capabilities = method.Invoke(null, new object[1] { cameraInfo });
			if (capabilities == null)
			{
				return null;
			}
			return ReadStabilizationBoolean(capabilities);
		}
		catch
		{
			return null;
		}
	}

	private static bool? ReadStabilizationBoolean(object capabilities)
	{
		Type type = capabilities.GetType();
		try
		{
			PropertyInfo property = type.GetProperty("IsStabilizationSupported", BindingFlags.Instance | BindingFlags.Public);
			if (property?.PropertyType == typeof(bool))
			{
				return (bool?)property.GetValue(capabilities);
			}
		}
		catch
		{
		}
		string[] array = new string[2] { "IsStabilizationSupported", "isStabilizationSupported" };
		foreach (string name in array)
		{
			try
			{
				MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
				if ((object)method != null && (method.ReturnType == typeof(bool) || method.ReturnType == typeof(Java.Lang.Boolean)))
				{
					object result = method.Invoke(capabilities, null);
					if (result is bool b)
					{
						return b;
					}
					if (result is Java.Lang.Boolean jb)
					{
						return jb.BooleanValue();
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static CameraSelector BuildCameraSelector(bool useFrontCamera, string? physicalCameraId)
	{
		if (useFrontCamera || string.IsNullOrWhiteSpace(physicalCameraId) || !OperatingSystem.IsAndroidVersionAtLeast(28))
		{
			return useFrontCamera ? CameraSelector.DefaultFrontCamera : CameraSelector.DefaultBackCamera;
		}
		CameraSelector.Builder builder = new CameraSelector.Builder();
		builder.RequireLensFacing(1);
		builder.SetPhysicalCameraId(physicalCameraId);
		return builder.Build();
	}

	private static async Task<ProcessCameraProvider> GetCameraProviderAsync(Context context, CancellationToken cancellationToken)
	{
		IListenableFuture future = ProcessCameraProvider.GetInstance(context) ?? throw new InvalidOperationException("CameraX no devolvió ProcessCameraProvider.");
		TaskCompletionSource<ProcessCameraProvider> tcs = new TaskCompletionSource<ProcessCameraProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
		IExecutor executor = ContextCompat.GetMainExecutor(context) ?? throw new InvalidOperationException("No se pudo obtener el ejecutor principal Android.");
		future.AddListener(new Runnable(delegate
		{
			try
			{
				ProcessCameraProvider result = (ProcessCameraProvider)(future.Get() ?? throw new InvalidOperationException("CameraX devolvió un proveedor no válido."));
				tcs.TrySetResult(result);
			}
			catch (System.Exception exception)
			{
				tcs.TrySetException(exception);
			}
		}), executor);
		return await tcs.Task.WaitAsync(cancellationToken);
	}

	private static IReadOnlyList<string> GetPhysicalIds(CameraCharacteristics characteristics)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(28))
		{
			return Array.Empty<string>();
		}
		try
		{
			return characteristics.PhysicalCameraIds.Where((string id) => !string.IsNullOrWhiteSpace(id)).OrderBy<string, string>((string id) => id, StringComparer.Ordinal).ToArray();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static (bool IsFreeform, string Text) ReadScalerCroppingType(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.ScalerCroppingType);
			if (raw == null)
			{
				return (IsFreeform: false, Text: "No publicado");
			}
			if (!int.TryParse(raw.ToString(), out var value))
			{
				return (IsFreeform: false, Text: raw.ToString() ?? "Desconocido");
			}
			return (value == 1) ? (IsFreeform: true, Text: "FREEFORM") : (IsFreeform: false, Text: "CENTER_ONLY");
		}
		catch
		{
			return (IsFreeform: false, Text: "Error");
		}
	}

	private static string ReadFacingValue(CameraCharacteristics characteristics)
	{
		try
		{
			return characteristics.Get(CameraCharacteristics.LensFacing)?.ToString() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string FacingToText(string? facing)
	{
		if (1 == 0)
		{
		}
		string result = facing switch
		{
			"0" => "Frontal", 
			"1" => "Trasera", 
			"2" => "Externa", 
			_ => "Desconocida", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string GetDeviceName()
	{
		string manufacturer = Build.Manufacturer ?? "Android";
		string model = Build.Model ?? "dispositivo";
		return (manufacturer + " " + model).Trim();
	}

	private static int[] ReadIntArray(CameraCharacteristics characteristics, CameraCharacteristics.Key? key)
	{
		if (key == null)
		{
			return Array.Empty<int>();
		}
		try
		{
			Java.Lang.Object raw = characteristics.Get(key);
			if (raw == null || raw.Handle == IntPtr.Zero)
			{
				return Array.Empty<int>();
			}
			return JNIEnv.GetArray<int>(raw.Handle) ?? Array.Empty<int>();
		}
		catch
		{
			return Array.Empty<int>();
		}
	}

	private static string ReadFocalLengths(CameraCharacteristics characteristics)
	{
		try
		{
			CameraCharacteristics.Key key = CameraCharacteristics.LensInfoAvailableFocalLengths;
			if (key == null)
			{
				return "No publicadas";
			}
			Java.Lang.Object raw = characteristics.Get(key);
			if (raw == null || raw.Handle == IntPtr.Zero)
			{
				return "No publicadas";
			}
			float[] values = JNIEnv.GetArray<float>(raw.Handle) ?? Array.Empty<float>();
			if (values.Length == 0)
			{
				return "No publicadas";
			}
			return string.Join(", ", values.Select((float v) => $"{v:F2} mm"));
		}
		catch
		{
			return "No publicadas";
		}
	}

	private static string ReadHardwareLevel(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.InfoSupportedHardwareLevel);
			if (raw == null)
			{
				return "Nivel desconocido";
			}
			if (!int.TryParse(raw.ToString(), out var level))
			{
				return $"Nivel {raw}";
			}
			if (1 == 0)
			{
			}
			string result = level switch
			{
				0 => "LIMITED", 
				1 => "FULL", 
				2 => "LEGACY", 
				3 => "LEVEL_3", 
				4 => "EXTERNAL", 
				_ => $"Nivel {level}", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		catch
		{
			return "Nivel desconocido";
		}
	}
}
