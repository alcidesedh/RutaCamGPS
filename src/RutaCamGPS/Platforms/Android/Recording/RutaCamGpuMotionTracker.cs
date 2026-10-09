using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Android.App;
using Android.Content;
using Android.Hardware;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Java.Interop;
using Java.Lang;

namespace GPSCamRoute.Platforms.Android.Recording;

public sealed class RutaCamGpuMotionTracker : Java.Lang.Object, ISensorEventListener, IJavaObject, IDisposable, IJavaPeerable
{
	public readonly record struct MotionSnapshot(double PitchNormalized, double YawNormalized, double RollRadians, double VibrationG, double Zoom, double TranslationMargin, double RollStrength, double HorizontalSign, double VerticalSign, double RollSign, double PitchDegrees, double YawDegrees, double RollDegrees, double GyroX, double GyroY, double GyroZ, bool PitchSaturated, bool YawSaturated, bool RollSaturated, int TargetRotation, bool UseFrontCamera, string ProfileName);

	private sealed record RutaCamGpuProfile(bool Enabled, string DisplayName, double Strength, double Zoom, double TranslationMargin, double MaxAngleDegrees, double RollStrength, double RecenterSeconds, double Deadband, double GyroLowPass, double HorizontalSign, double VerticalSign, double RollSign)
	{
		public double MaxAngleRadians => MaxAngleDegrees * System.Math.PI / 180.0;

		public static RutaCamGpuProfile Off { get; } = new RutaCamGpuProfile(Enabled: false, "OFF", 0.0, 1.0, 0.0, 0.0, 0.0, 0.2, 0.03, 0.55, -1.0, 1.0, -1.0);

		public static RutaCamGpuProfile FromMode(string? mode)
		{
			if (string.IsNullOrWhiteSpace(mode) || mode.StartsWith("Desactivada", StringComparison.OrdinalIgnoreCase))
			{
				return Off;
			}
			if (mode.Contains("Fuerte", StringComparison.OrdinalIgnoreCase))
			{
				return new RutaCamGpuProfile(Enabled: true, "FUERTE", 0.88, 1.22, 0.15, 4.5, 0.66, 0.2, 0.022, 0.6, -1.0, 1.0, -1.0);
			}
			if (mode.Contains("Suave", StringComparison.OrdinalIgnoreCase))
			{
				return new RutaCamGpuProfile(Enabled: true, "SUAVE", 0.64, 1.11, 0.09, 3.0, 0.44, 0.27, 0.03, 0.65, -1.0, 1.0, -1.0);
			}
			return new RutaCamGpuProfile(Enabled: true, "EQUILIBRADA", 0.78, 1.17, 0.12, 3.8, 0.56, 0.22, 0.025, 0.6, -1.0, 1.0, -1.0);
		}

		[CompilerGenerated]
		private RutaCamGpuProfile(RutaCamGpuProfile original)
		{
			Enabled = original.Enabled;
			DisplayName = original.DisplayName;
			Strength = original.Strength;
			Zoom = original.Zoom;
			TranslationMargin = original.TranslationMargin;
			MaxAngleDegrees = original.MaxAngleDegrees;
			RollStrength = original.RollStrength;
			RecenterSeconds = original.RecenterSeconds;
			Deadband = original.Deadband;
			GyroLowPass = original.GyroLowPass;
			HorizontalSign = original.HorizontalSign;
			VerticalSign = original.VerticalSign;
			RollSign = original.RollSign;
		}
	}

	private readonly object _sync = new object();

	private SensorManager? _sensorManager;

	private Sensor? _gyro;

	private Sensor? _accel;

	private HandlerThread? _thread;

	private Handler? _handler;

	private long _lastGyroTimestampNs;

	private double _gx;

	private double _gy;

	private double _gz;

	private double _pitch;

	private double _yaw;

	private double _roll;

	private double _gravityX;

	private double _gravityY;

	private double _gravityZ;

	private double _vibrationG;

	private RutaCamGpuProfile _profile = RutaCamGpuProfile.Off;

	private int _targetRotation;

	private bool _useFrontCamera;

	private bool _running;

	private bool _disposed;

	public bool IsRunning => _running;

	public MotionSnapshot Snapshot
	{
		get
		{
			lock (_sync)
			{
				double max = _profile.MaxAngleRadians;
				double pitch = System.Math.Clamp(_pitch, 0.0 - max, max);
				double yaw = System.Math.Clamp(_yaw, 0.0 - max, max);
				double roll = System.Math.Clamp(_roll, 0.0 - max, max);
				bool pitchSaturated = max > 0.0 && System.Math.Abs(_pitch) >= max;
				bool yawSaturated = max > 0.0 && System.Math.Abs(_yaw) >= max;
				bool rollSaturated = max > 0.0 && System.Math.Abs(_roll) >= max;
				return new MotionSnapshot((max > 0.0) ? (pitch / max) : 0.0, (max > 0.0) ? (yaw / max) : 0.0, roll, _vibrationG, _profile.Zoom, _profile.TranslationMargin, _profile.RollStrength, _profile.HorizontalSign, _profile.VerticalSign, _profile.RollSign, pitch * 180.0 / System.Math.PI, yaw * 180.0 / System.Math.PI, roll * 180.0 / System.Math.PI, _gx, _gy, _gz, pitchSaturated, yawSaturated, rollSaturated, _targetRotation, _useFrontCamera, _profile.DisplayName);
			}
		}
	}

	public bool Start(string? mode)
	{
		return Start(mode, 0, useFrontCamera: false);
	}

	public bool Start(string? mode, int targetRotation, bool useFrontCamera)
	{
		Stop();
		_profile = RutaCamGpuProfile.FromMode(mode);
		_targetRotation = targetRotation;
		_useFrontCamera = useFrontCamera;
		if (!_profile.Enabled)
		{
			return false;
		}
		Context context = Application.Context;
		_sensorManager = context.GetSystemService("sensor") as SensorManager;
		if (_sensorManager == null)
		{
			return false;
		}
		_gyro = _sensorManager.GetDefaultSensor(SensorType.Gyroscope);
		_accel = _sensorManager.GetDefaultSensor(SensorType.Accelerometer);
		if (_gyro == null)
		{
			return false;
		}
		_thread = new HandlerThread("RutaCam-GPU-Motion");
		_thread.Start();
		_handler = new Handler(_thread.Looper ?? throw new InvalidOperationException("No se pudo crear hilo de sensores GPU."));
		ResetState();
		if (!_sensorManager.RegisterListener(this, _gyro, SensorDelay.Game, _handler))
		{
			Stop();
			return false;
		}
		if (_accel != null)
		{
			try
			{
				_sensorManager.RegisterListener(this, _accel, SensorDelay.Game, _handler);
			}
			catch
			{
			}
		}
		_running = true;
		System.Diagnostics.Debug.WriteLine($"RutaCam GPU CAL start · profile={_profile.DisplayName} · rotation={RotationLabel(_targetRotation)} · camera={(_useFrontCamera ? "FRONT" : "BACK")} · signs X={_profile.HorizontalSign:+0;-0;0} Y={_profile.VerticalSign:+0;-0;0} R={_profile.RollSign:+0;-0;0}");
		return true;
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
				ProcessGyro(e);
			}
		}
		catch
		{
		}
	}

	private void ProcessAccelerometer(SensorEvent e)
	{
		if (e.Values == null || e.Values.Count < 3)
		{
			return;
		}
		double ax = e.Values[0];
		double ay = e.Values[1];
		double az = e.Values[2];
		_gravityX = 0.91 * _gravityX + 0.08999999999999997 * ax;
		_gravityY = 0.91 * _gravityY + 0.08999999999999997 * ay;
		_gravityZ = 0.91 * _gravityZ + 0.08999999999999997 * az;
		double lx = ax - _gravityX;
		double ly = ay - _gravityY;
		double lz = az - _gravityZ;
		double vibration = System.Math.Sqrt(lx * lx + ly * ly + lz * lz) / 9.80665;
		lock (_sync)
		{
			_vibrationG = _vibrationG * 0.84 + System.Math.Clamp(vibration, 0.0, 3.0) * 0.16;
		}
	}

	private void ProcessGyro(SensorEvent e)
	{
		long timestamp = e.Timestamp;
		if (_lastGyroTimestampNs == 0)
		{
			_lastGyroTimestampNs = timestamp;
			return;
		}
		double dt = (double)(timestamp - _lastGyroTimestampNs) / 1000000000.0;
		_lastGyroTimestampNs = timestamp;
		if (dt <= 0.0 || dt > 0.1)
		{
			return;
		}
		double rawX = e.Values[0];
		double rawY = e.Values[1];
		double rawZ = e.Values[2];
		RemapGyroToDisplay(rawX, rawY, rawZ, _targetRotation, out var displayX, out var displayY, out var displayZ);
		double x = Deadband(displayX, _profile.Deadband);
		double y = Deadband(displayY, _profile.Deadband);
		double z = Deadband(displayZ, _profile.Deadband);
		lock (_sync)
		{
			_gx = _gx * _profile.GyroLowPass + x * (1.0 - _profile.GyroLowPass);
			_gy = _gy * _profile.GyroLowPass + y * (1.0 - _profile.GyroLowPass);
			_gz = _gz * _profile.GyroLowPass + z * (1.0 - _profile.GyroLowPass);
			double decay = System.Math.Exp((0.0 - dt) / _profile.RecenterSeconds);
			double vibrationDamping = System.Math.Clamp(1.0 - _vibrationG * 0.2, 0.82, 1.0);
			double gain = _profile.Strength * vibrationDamping;
			_pitch = _pitch * decay + _gx * dt * gain;
			_yaw = _yaw * decay + _gy * dt * gain;
			_roll = _roll * decay + _gz * dt * gain;
		}
	}

	private static void RemapGyroToDisplay(double rawX, double rawY, double rawZ, int targetRotation, out double x, out double y, out double z)
	{
		switch ((SurfaceOrientation)targetRotation)
		{
		case SurfaceOrientation.Rotation90:
			x = rawY;
			y = 0.0 - rawX;
			break;
		case SurfaceOrientation.Rotation180:
			x = 0.0 - rawX;
			y = 0.0 - rawY;
			break;
		case SurfaceOrientation.Rotation270:
			x = 0.0 - rawY;
			y = rawX;
			break;
		default:
			x = rawX;
			y = rawY;
			break;
		}
		z = rawZ;
	}

	public void Stop()
	{
		_running = false;
		try
		{
			_sensorManager?.UnregisterListener(this);
		}
		catch
		{
		}
		_gyro = null;
		_accel = null;
		_sensorManager = null;
		_handler?.Dispose();
		_handler = null;
		if (_thread != null)
		{
			try
			{
				_thread.QuitSafely();
			}
			catch
			{
			}
			_thread.Dispose();
			_thread = null;
		}
		ResetState();
	}

	private void ResetState()
	{
		lock (_sync)
		{
			_lastGyroTimestampNs = 0L;
			_gx = (_gy = (_gz = 0.0));
			_pitch = (_yaw = (_roll = 0.0));
			_gravityX = (_gravityY = (_gravityZ = 0.0));
			_vibrationG = 0.0;
		}
	}

	private static double Deadband(double value, double deadband)
	{
		if (System.Math.Abs(value) <= deadband)
		{
			return 0.0;
		}
		return (value > 0.0) ? (value - deadband) : (value + deadband);
	}

	private static string RotationLabel(int targetRotation)
	{
		if (1 == 0)
		{
		}
		string result = targetRotation switch
		{
			1 => "ROT_90", 
			2 => "ROT_180", 
			3 => "ROT_270", 
			_ => "ROT_0", 
		};
		if (1 == 0)
		{
		}
		return result;
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
