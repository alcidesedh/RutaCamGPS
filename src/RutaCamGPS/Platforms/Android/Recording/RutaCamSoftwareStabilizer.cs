using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware;
using Android.Hardware.Camera2;
using Android.OS;
using Android.Runtime;
using Android.Util;
using AndroidX.Camera.Core;
using Java.Interop;
using Java.Lang;

namespace GPSCamRoute.Platforms.Android.Recording;

public sealed class RutaCamSoftwareStabilizer : Java.Lang.Object, ISensorEventListener, IJavaObject, IDisposable, IJavaPeerable
{
	private sealed record RutaCamSoftwareStabilizerProfile(bool Enabled, string DisplayName, double Strength, double SideMarginFraction, double MaxCompensationDegrees, double RecenterTimeConstantSeconds, double GyroDeadband, double GyroLowPassAlpha, int UpdateIntervalMs)
	{
		public double ZoomEquivalent => Enabled ? (1.0 / System.Math.Max(0.1, 1.0 - 2.0 * SideMarginFraction)) : 1.0;

		public static RutaCamSoftwareStabilizerProfile Off { get; } = new RutaCamSoftwareStabilizerProfile(Enabled: false, "OFF", 0.0, 0.0, 0.0, 0.2, 0.03, 0.55, 100);

		public static RutaCamSoftwareStabilizerProfile FromMode(string? mode)
		{
			if (string.IsNullOrWhiteSpace(mode) || mode.StartsWith("Desactivada", StringComparison.OrdinalIgnoreCase))
			{
				return Off;
			}
			if (mode.Contains("Fuerte", StringComparison.OrdinalIgnoreCase))
			{
				return new RutaCamSoftwareStabilizerProfile(Enabled: true, "Fuerte", 1.0, 0.105, 4.8, 0.17, 0.02, 0.5, 60);
			}
			if (mode.Contains("Suave", StringComparison.OrdinalIgnoreCase))
			{
				return new RutaCamSoftwareStabilizerProfile(Enabled: true, "Suave", 0.58, 0.06, 2.7, 0.24, 0.03, 0.6, 100);
			}
			return new RutaCamSoftwareStabilizerProfile(Enabled: true, "Equilibrada", 0.8, 0.08, 3.6, 0.2, 0.025, 0.55, 80);
		}

		[CompilerGenerated]
		private RutaCamSoftwareStabilizerProfile(RutaCamSoftwareStabilizerProfile original)
		{
			Enabled = original.Enabled;
			DisplayName = original.DisplayName;
			Strength = original.Strength;
			SideMarginFraction = original.SideMarginFraction;
			MaxCompensationDegrees = original.MaxCompensationDegrees;
			RecenterTimeConstantSeconds = original.RecenterTimeConstantSeconds;
			GyroDeadband = original.GyroDeadband;
			GyroLowPassAlpha = original.GyroLowPassAlpha;
			UpdateIntervalMs = original.UpdateIntervalMs;
		}
	}

	private readonly object _sync = new object();

	private SensorManager? _sensorManager;

	private Sensor? _gyroscope;

	private Sensor? _accelerometer;

	private HandlerThread? _sensorThread;

	private Handler? _sensorHandler;

	private object? _camera2Control;

	private Type? _captureRequestOptionsType;

	private Type? _captureRequestBuilderType;

	private MethodInfo? _setCaptureRequestOptionsMethod;

	private MethodInfo? _clearCaptureRequestOptionsMethod;

	private Rect? _activeArray;

	private Rect? _baseFrame;

	private int _sensorOrientation;

	private double _horizontalFovRadians;

	private double _verticalFovRadians;

	private int _relativeRotationDegrees;

	private RutaCamSoftwareStabilizerProfile _profile = RutaCamSoftwareStabilizerProfile.Off;

	private long _lastGyroTimestampNs;

	private long _lastCropUpdateTimestampNs;

	private double _filteredGyroX;

	private double _filteredGyroY;

	private double _anglePitch;

	private double _angleYaw;

	private double _gravityX;

	private double _gravityY;

	private double _gravityZ;

	private double _vibrationG;

	private volatile bool _running;

	private volatile bool _disposed;

	public bool IsRunning => _running;

	public string Status { get; private set; } = "RutaCam Stabilizer: OFF";

	public double LastVibrationG => _vibrationG;

	public double LastShiftPercentX { get; private set; }

	public double LastShiftPercentY { get; private set; }

	public async Task<string> StartAsync(ICameraControl cameraControl, bool useFrontCamera, string? physicalCameraId, string? mode, int displayRotation, CancellationToken cancellationToken)
	{
		Stop();
		if (_disposed)
		{
			return Status = "RutaCam Stabilizer: servicio cerrado";
		}
		_profile = RutaCamSoftwareStabilizerProfile.FromMode(mode);
		if (!_profile.Enabled)
		{
			return Status = "RutaCam Stabilizer: OFF";
		}
		if (useFrontCamera)
		{
			return Status = "RutaCam Stabilizer: V1 solo cámara trasera";
		}
		try
		{
			Context context = Application.Context;
			if (!(context.GetSystemService("camera") is CameraManager manager))
			{
				return Status = "RutaCam Stabilizer: CameraManager no disponible";
			}
			(string CameraId, CameraCharacteristics? Characteristics) selected = SelectCamera(manager, useFrontCamera, physicalCameraId);
			if (selected.Characteristics == null || string.IsNullOrWhiteSpace(selected.CameraId))
			{
				return Status = "RutaCam Stabilizer: cámara no encontrada";
			}
			if (!IsFreeformCrop(selected.Characteristics))
			{
				return Status = "RutaCam Stabilizer: no disponible · crop CENTER_ONLY";
			}
			_activeArray = ReadActiveArray(selected.Characteristics);
			if (_activeArray == null || _activeArray.Width() <= 0 || _activeArray.Height() <= 0)
			{
				return Status = "RutaCam Stabilizer: active array no publicado";
			}
			double focalMm = ReadFirstFocalLength(selected.Characteristics);
			SizeF physicalSize = ReadSensorPhysicalSize(selected.Characteristics);
			if (focalMm <= 0.0 || physicalSize == null || physicalSize.Width <= 0f || physicalSize.Height <= 0f)
			{
				return Status = "RutaCam Stabilizer: focal/tamaño de sensor no disponibles";
			}
			_sensorOrientation = ReadSensorOrientation(selected.Characteristics);
			int displayDegrees = RotationToDegrees(displayRotation);
			_relativeRotationDegrees = NormalizeRightAngle(_sensorOrientation - displayDegrees);
			_baseFrame = ComputeBaseFrame16x9(_activeArray);
			if (_baseFrame.Width() <= 0 || _baseFrame.Height() <= 0)
			{
				return Status = "RutaCam Stabilizer: crop base inválido";
			}
			double physicalBaseWidth = (double)physicalSize.Width * ((double)_baseFrame.Width() / (double)_activeArray.Width());
			double physicalBaseHeight = (double)physicalSize.Height * ((double)_baseFrame.Height() / (double)_activeArray.Height());
			_horizontalFovRadians = 2.0 * System.Math.Atan(physicalBaseWidth / (2.0 * focalMm));
			_verticalFovRadians = 2.0 * System.Math.Atan(physicalBaseHeight / (2.0 * focalMm));
			if (!TryInitializeCamera2Interop(cameraControl))
			{
				return Status = "RutaCam Stabilizer: Camera2 interop no disponible";
			}
			_sensorManager = context.GetSystemService("sensor") as SensorManager;
			if (_sensorManager == null)
			{
				return Status = "RutaCam Stabilizer: SensorManager no disponible";
			}
			_gyroscope = _sensorManager.GetDefaultSensor(SensorType.Gyroscope);
			if (_gyroscope == null)
			{
				return Status = "RutaCam Stabilizer: giroscopio no disponible";
			}
			_accelerometer = _sensorManager.GetDefaultSensor(SensorType.Accelerometer);
			_sensorThread = new HandlerThread("RutaCam-Stabilizer-Sensors");
			_sensorThread.Start();
			_sensorHandler = new Handler(_sensorThread.Looper ?? throw new InvalidOperationException("No se pudo iniciar hilo de sensores."));
			try
			{
				cameraControl.SetZoomRatio(1f);
			}
			catch
			{
			}
			ResetFilterState();
			if (!_sensorManager.RegisterListener(this, _gyroscope, SensorDelay.Game, _sensorHandler))
			{
				return Status = "RutaCam Stabilizer: no se pudo activar giroscopio";
			}
			if (_accelerometer != null)
			{
				try
				{
					_sensorManager.RegisterListener(this, _accelerometer, SensorDelay.Game, _sensorHandler);
				}
				catch
				{
				}
			}
			_running = true;
			ApplyCropForAngles(0.0, 0.0);
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			return Status = $"RutaCam Stabilizer V1 · {_profile.DisplayName} · crop {_profile.ZoomEquivalent:F2}x · {_profile.UpdateIntervalMs} ms";
		}
		catch (System.OperationCanceledException)
		{
			Stop();
			throw;
		}
		catch (System.Exception ex2)
		{
			Stop();
			return Status = "RutaCam Stabilizer: " + ex2.Message;
		}
	}

	public void Stop()
	{
		_running = false;
		try
		{
			if (_sensorManager != null)
			{
				_sensorManager.UnregisterListener(this);
			}
		}
		catch
		{
		}
		try
		{
			ClearCamera2Crop();
		}
		catch
		{
		}
		ResetFilterState();
		_gyroscope = null;
		_accelerometer = null;
		_sensorManager = null;
		_sensorHandler?.Dispose();
		_sensorHandler = null;
		if (_sensorThread != null)
		{
			try
			{
				_sensorThread.QuitSafely();
			}
			catch
			{
			}
			_sensorThread.Dispose();
			_sensorThread = null;
		}
		_camera2Control = null;
		_captureRequestOptionsType = null;
		_captureRequestBuilderType = null;
		_setCaptureRequestOptionsMethod = null;
		_clearCaptureRequestOptionsMethod = null;
		_activeArray?.Dispose();
		_activeArray = null;
		_baseFrame?.Dispose();
		_baseFrame = null;
		if (!_disposed)
		{
			Status = "RutaCam Stabilizer: OFF";
		}
	}

	public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
	{
	}

	public void OnSensorChanged(SensorEvent? e)
	{
		if (!_running || e?.Sensor == null)
		{
			return;
		}
		try
		{
			if (e.Sensor.Type == SensorType.Accelerometer)
			{
				ProcessAccelerometer(e);
			}
			else if (e.Sensor.Type == SensorType.Gyroscope && e.Values != null && e.Values.Count >= 3)
			{
				ProcessGyroscope(e);
			}
		}
		catch
		{
		}
	}

	private void ProcessAccelerometer(SensorEvent e)
	{
		if (e.Values != null && e.Values.Count >= 3)
		{
			double ax = e.Values[0];
			double ay = e.Values[1];
			double az = e.Values[2];
			_gravityX = 0.9 * _gravityX + 0.09999999999999998 * ax;
			_gravityY = 0.9 * _gravityY + 0.09999999999999998 * ay;
			_gravityZ = 0.9 * _gravityZ + 0.09999999999999998 * az;
			double lx = ax - _gravityX;
			double ly = ay - _gravityY;
			double lz = az - _gravityZ;
			double magnitudeG = System.Math.Sqrt(lx * lx + ly * ly + lz * lz) / 9.80665;
			_vibrationG = _vibrationG * 0.82 + System.Math.Clamp(magnitudeG, 0.0, 3.0) * 0.18;
		}
	}

	private void ProcessGyroscope(SensorEvent e)
	{
		long timestamp = e.Timestamp;
		if (_lastGyroTimestampNs == 0)
		{
			_lastGyroTimestampNs = timestamp;
			return;
		}
		double dt = (double)(timestamp - _lastGyroTimestampNs) / 1000000000.0;
		_lastGyroTimestampNs = timestamp;
		if (!(dt <= 0.0) && !(dt > 0.1))
		{
			double gx = ApplyDeadband(e.Values[0], _profile.GyroDeadband);
			double gy = ApplyDeadband(e.Values[1], _profile.GyroDeadband);
			double lpfAlpha = _profile.GyroLowPassAlpha;
			_filteredGyroX = _filteredGyroX * lpfAlpha + gx * (1.0 - lpfAlpha);
			_filteredGyroY = _filteredGyroY * lpfAlpha + gy * (1.0 - lpfAlpha);
			double decay = System.Math.Exp((0.0 - dt) / _profile.RecenterTimeConstantSeconds);
			double vibrationBoost = System.Math.Clamp(0.9 + _vibrationG * 0.16, 0.9, 1.18);
			double gain = _profile.Strength * vibrationBoost;
			_anglePitch = _anglePitch * decay + _filteredGyroX * dt * gain;
			_angleYaw = _angleYaw * decay + _filteredGyroY * dt * gain;
			double maxAngle = _profile.MaxCompensationDegrees * System.Math.PI / 180.0;
			_anglePitch = System.Math.Clamp(_anglePitch, 0.0 - maxAngle, maxAngle);
			_angleYaw = System.Math.Clamp(_angleYaw, 0.0 - maxAngle, maxAngle);
			double elapsedMs = ((_lastCropUpdateTimestampNs == 0L) ? double.MaxValue : ((double)(timestamp - _lastCropUpdateTimestampNs) / 1000000.0));
			if (!(elapsedMs < (double)_profile.UpdateIntervalMs))
			{
				_lastCropUpdateTimestampNs = timestamp;
				ApplyCropForAngles(_anglePitch, _angleYaw);
			}
		}
	}

	private void ApplyCropForAngles(double pitchRadians, double yawRadians)
	{
		if ((!_running && (_anglePitch != 0.0 || _angleYaw != 0.0)) || _activeArray == null || _baseFrame == null || _camera2Control == null || (object)_captureRequestBuilderType == null)
		{
			return;
		}
		double sideMargin = _profile.SideMarginFraction;
		int cropWidth = System.Math.Max(32, (int)System.Math.Round((double)_baseFrame.Width() * (1.0 - 2.0 * sideMargin)));
		int cropHeight = System.Math.Max(32, (int)System.Math.Round((double)_baseFrame.Height() * (1.0 - 2.0 * sideMargin)));
		cropWidth &= -2;
		cropHeight &= -2;
		int maxShiftX = System.Math.Max(0, (_baseFrame.Width() - cropWidth) / 2);
		int maxShiftY = System.Math.Max(0, (_baseFrame.Height() - cropHeight) / 2);
		double screenShiftX = AngleToPixelShift(yawRadians, _horizontalFovRadians, _baseFrame.Width());
		double screenShiftY = AngleToPixelShift(pitchRadians, _verticalFovRadians, _baseFrame.Height());
		(double, double) sensorShift = RotateOutputShiftToSensor(screenShiftX, screenShiftY, _relativeRotationDegrees);
		int shiftX = System.Math.Clamp((int)System.Math.Round(sensorShift.Item1), -maxShiftX, maxShiftX);
		int shiftY = System.Math.Clamp((int)System.Math.Round(sensorShift.Item2), -maxShiftY, maxShiftY);
		LastShiftPercentX = ((maxShiftX <= 0) ? 0.0 : ((double)shiftX / (double)maxShiftX * 100.0));
		LastShiftPercentY = ((maxShiftY <= 0) ? 0.0 : ((double)shiftY / (double)maxShiftY * 100.0));
		int centerX = _baseFrame.CenterX() + shiftX;
		int centerY = _baseFrame.CenterY() + shiftY;
		int left = centerX - cropWidth / 2;
		int top = centerY - cropHeight / 2;
		left = System.Math.Clamp(left, _baseFrame.Left, _baseFrame.Right - cropWidth);
		top = System.Math.Clamp(top, _baseFrame.Top, _baseFrame.Bottom - cropHeight);
		using Rect crop = new Rect(left, top, left + cropWidth, top + cropHeight);
		TryApplyCamera2Crop(crop);
	}

	private bool TryInitializeCamera2Interop(ICameraControl cameraControl)
	{
		try
		{
			Type camera2ControlType = ResolveInteropType("AndroidX.Camera.Camera2.Interop.Camera2CameraControl");
			if ((object)camera2ControlType == null)
			{
				return false;
			}
			MethodInfo fromMethod = camera2ControlType.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "From", StringComparison.Ordinal) && m.GetParameters().Length == 1);
			if ((object)fromMethod == null)
			{
				return false;
			}
			_camera2Control = fromMethod.Invoke(null, new object[1] { cameraControl });
			if (_camera2Control == null)
			{
				return false;
			}
			Assembly assembly = camera2ControlType.Assembly;
			_captureRequestOptionsType = assembly.GetType("AndroidX.Camera.Camera2.Interop.CaptureRequestOptions");
			_captureRequestBuilderType = _captureRequestOptionsType?.GetNestedType("Builder", BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)_captureRequestBuilderType == null)
			{
				return false;
			}
			_setCaptureRequestOptionsMethod = _camera2Control.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => (string.Equals(m.Name, "SetCaptureRequestOptions", StringComparison.Ordinal) || string.Equals(m.Name, "AddCaptureRequestOptions", StringComparison.Ordinal)) && m.GetParameters().Length == 1);
			_clearCaptureRequestOptionsMethod = _camera2Control.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "ClearCaptureRequestOptions", StringComparison.Ordinal) && m.GetParameters().Length == 0);
			return (object)_setCaptureRequestOptionsMethod != null;
		}
		catch
		{
			return false;
		}
	}

	private void TryApplyCamera2Crop(Rect crop)
	{
		try
		{
			if (_camera2Control == null || (object)_captureRequestBuilderType == null || (object)_setCaptureRequestOptionsMethod == null)
			{
				return;
			}
			object builder = Activator.CreateInstance(_captureRequestBuilderType);
			if (builder == null)
			{
				return;
			}
			CaptureRequest.Key scalerKey = CaptureRequest.ScalerCropRegion;
			if (scalerKey != null && TryInvokeSetCaptureRequestOption(builder, scalerKey, crop))
			{
				object options = _captureRequestBuilderType.GetMethod("Build", BindingFlags.Instance | BindingFlags.Public)?.Invoke(builder, null);
				if (options != null)
				{
					_setCaptureRequestOptionsMethod.Invoke(_camera2Control, new object[1] { options });
				}
			}
		}
		catch
		{
		}
	}

	private static bool TryInvokeSetCaptureRequestOption(object builder, object key, object value)
	{
		MethodInfo[] methods = (from m in builder.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where string.Equals(m.Name, "SetCaptureRequestOption", StringComparison.Ordinal) && m.GetParameters().Length == 2
			select m).ToArray();
		MethodInfo[] array = methods;
		foreach (MethodInfo candidate in array)
		{
			try
			{
				MethodInfo method = candidate;
				if (method.IsGenericMethodDefinition)
				{
					method = method.MakeGenericMethod(value.GetType());
				}
				method.Invoke(builder, new object[2] { key, value });
				return true;
			}
			catch
			{
			}
		}
		return false;
	}

	private void ClearCamera2Crop()
	{
		try
		{
			_clearCaptureRequestOptionsMethod?.Invoke(_camera2Control, null);
		}
		catch
		{
		}
	}

	private static Type? ResolveInteropType(string fullName)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			try
			{
				Type type = assembly.GetType(fullName, throwOnError: false);
				if ((object)type != null)
				{
					return type;
				}
			}
			catch
			{
			}
		}
		string[] array = new string[2] { "Xamarin.AndroidX.Camera.Camera2", "AndroidX.Camera.Camera2" };
		foreach (string assemblyName in array)
		{
			try
			{
				Type type2 = Type.GetType(fullName + ", " + assemblyName, throwOnError: false);
				if ((object)type2 != null)
				{
					return type2;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static (string CameraId, CameraCharacteristics? Characteristics) SelectCamera(CameraManager manager, bool useFrontCamera, string? physicalCameraId)
	{
		if (!string.IsNullOrWhiteSpace(physicalCameraId))
		{
			try
			{
				return (CameraId: physicalCameraId, Characteristics: manager.GetCameraCharacteristics(physicalCameraId));
			}
			catch
			{
			}
		}
		string desiredFacing = (useFrontCamera ? "0" : "1");
		string[] cameraIdList = manager.GetCameraIdList();
		foreach (string cameraId in cameraIdList)
		{
			try
			{
				CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);
				string facing = characteristics.Get(CameraCharacteristics.LensFacing)?.ToString();
				if (string.Equals(facing, desiredFacing, StringComparison.Ordinal))
				{
					return (CameraId: cameraId, Characteristics: characteristics);
				}
			}
			catch
			{
			}
		}
		return (CameraId: string.Empty, Characteristics: null);
	}

	private static bool IsFreeformCrop(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.ScalerCroppingType);
			int value;
			return raw != null && int.TryParse(raw.ToString(), out value) && value == 1;
		}
		catch
		{
			return false;
		}
	}

	private static Rect? ReadActiveArray(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.SensorInfoActiveArraySize);
			if (raw is Rect rect)
			{
				return new Rect(rect);
			}
			if (raw?.Handle != IntPtr.Zero)
			{
				Rect cast = global::Android.Runtime.Extensions.JavaCast<Rect>(raw);
				if (cast != null)
				{
					return new Rect(cast);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static SizeF? ReadSensorPhysicalSize(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.SensorInfoPhysicalSize);
			if (raw is SizeF size)
			{
				return size;
			}
			if (raw?.Handle != IntPtr.Zero)
			{
				return global::Android.Runtime.Extensions.JavaCast<SizeF>(raw);
			}
		}
		catch
		{
		}
		return null;
	}

	private static double ReadFirstFocalLength(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.LensInfoAvailableFocalLengths);
			if (raw == null || raw.Handle == IntPtr.Zero)
			{
				return 0.0;
			}
			float[] values = JNIEnv.GetArray<float>(raw.Handle) ?? Array.Empty<float>();
			return (values.Length == 0) ? 0f : values[0];
		}
		catch
		{
			return 0.0;
		}
	}

	private static int ReadSensorOrientation(CameraCharacteristics characteristics)
	{
		try
		{
			Java.Lang.Object raw = characteristics.Get(CameraCharacteristics.SensorOrientation);
			int value;
			return (raw != null && int.TryParse(raw.ToString(), out value)) ? value : 90;
		}
		catch
		{
			return 90;
		}
	}

	private static Rect ComputeBaseFrame16x9(Rect active)
	{
		int width = active.Width();
		int height = active.Height();
		double currentAspect = (double)width / (double)height;
		int baseWidth;
		int baseHeight;
		if (currentAspect >= 1.7777777777777777)
		{
			baseHeight = height;
			baseWidth = (int)System.Math.Round((double)baseHeight * 1.7777777777777777);
		}
		else
		{
			baseWidth = width;
			baseHeight = (int)System.Math.Round((double)baseWidth / 1.7777777777777777);
		}
		baseWidth &= -2;
		baseHeight &= -2;
		int left = active.Left + (width - baseWidth) / 2;
		int top = active.Top + (height - baseHeight) / 2;
		return new Rect(left, top, left + baseWidth, top + baseHeight);
	}

	private static double AngleToPixelShift(double angleRadians, double fovRadians, int frameSize)
	{
		if (System.Math.Abs(fovRadians) < 1E-06)
		{
			return 0.0;
		}
		double denominator = System.Math.Tan(fovRadians / 2.0);
		if (System.Math.Abs(denominator) < 1E-06)
		{
			return 0.0;
		}
		return System.Math.Tan(angleRadians) / denominator * ((double)frameSize / 2.0);
	}

	private static (double X, double Y) RotateOutputShiftToSensor(double x, double y, int rotationDegrees)
	{
		if (1 == 0)
		{
		}
		(double, double) result = rotationDegrees switch
		{
			90 => (y, 0.0 - x), 
			180 => (0.0 - x, 0.0 - y), 
			270 => (0.0 - y, x), 
			_ => (x, y), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static int RotationToDegrees(int rotation)
	{
		if (1 == 0)
		{
		}
		int result = rotation switch
		{
			1 => 90, 
			2 => 180, 
			3 => 270, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static int NormalizeRightAngle(int degrees)
	{
		int value = (degrees % 360 + 360) % 360;
		if (value < 45 || value >= 315)
		{
			return 0;
		}
		if (value < 135)
		{
			return 90;
		}
		if (value < 225)
		{
			return 180;
		}
		return 270;
	}

	private static double ApplyDeadband(double value, double deadband)
	{
		if (System.Math.Abs(value) <= deadband)
		{
			return 0.0;
		}
		return (value > 0.0) ? (value - deadband) : (value + deadband);
	}

	private void ResetFilterState()
	{
		_lastGyroTimestampNs = 0L;
		_lastCropUpdateTimestampNs = 0L;
		_filteredGyroX = 0.0;
		_filteredGyroY = 0.0;
		_anglePitch = 0.0;
		_angleYaw = 0.0;
		_gravityX = 0.0;
		_gravityY = 0.0;
		_gravityZ = 0.0;
		_vibrationG = 0.0;
		LastShiftPercentX = 0.0;
		LastShiftPercentY = 0.0;
	}

	public new void Dispose()
	{
		if (!_disposed)
		{
			Stop();
			_disposed = true;
		}
	}

}
