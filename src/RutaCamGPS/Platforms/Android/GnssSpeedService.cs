using System;
using System.Globalization;
using Android.App;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using GPSCamRoute.Services;
using Java.Interop;
using Java.Lang;

namespace GPSCamRoute.Platforms.Android;

public sealed class GnssSpeedService : Java.Lang.Object, IGnssSpeedService, ILocationListener, IJavaObject, IDisposable, IJavaPeerable
{
	private sealed class NmeaListener : Java.Lang.Object, IOnNmeaMessageListener, IJavaObject, IDisposable, IJavaPeerable
	{
		private readonly Action<string, long> _callback;

		public NmeaListener(Action<string, long> callback)
		{
			_callback = callback;
		}

		public void OnNmeaMessage(string? message, long timestamp)
		{
			if (!string.IsNullOrWhiteSpace(message))
			{
				_callback(message, timestamp);
			}
		}

	}

	private readonly LocationManager _locationManager;

	private long? _lastLocationElapsedRealtimeNanos;

	private long? _lastNmeaTimestampMs;

	private long _lastNmeaSeenElapsedMs = long.MinValue;

	private NmeaListener? _nmeaListener;

	private Handler? _nmeaHandler;

	public bool IsRunning { get; private set; }

	public event EventHandler<GnssSpeedSample>? SpeedChanged;

	public event EventHandler<string>? StatusChanged;

	public GnssSpeedService()
	{
		_locationManager = ((LocationManager)Application.Context.GetSystemService("location")) ?? throw new InvalidOperationException("LocationManager no está disponible.");
	}

	public void Start()
	{
		if (!IsRunning)
		{
			if (!_locationManager.IsProviderEnabled("gps"))
			{
				this.StatusChanged?.Invoke(this, "GPS del teléfono desactivado.");
				return;
			}
			if (Application.Context.CheckSelfPermission("android.permission.ACCESS_FINE_LOCATION") != Permission.Granted)
			{
				this.StatusChanged?.Invoke(this, "Falta permiso de ubicación precisa.");
				return;
			}
			_lastLocationElapsedRealtimeNanos = null;
			_lastNmeaTimestampMs = null;
			_lastNmeaSeenElapsedMs = long.MinValue;
			_locationManager.RequestLocationUpdates("gps", 0L, 0f, this, Looper.MainLooper);
			TryStartNmea();
			IsRunning = true;
			this.StatusChanged?.Invoke(this, "GNSS directo activo");
		}
	}

	private void TryStartNmea()
	{
		if (Build.VERSION.SdkInt < BuildVersionCodes.N)
		{
			return;
		}
		try
		{
			_nmeaHandler = new Handler(Looper.MainLooper);
			_nmeaListener = new NmeaListener(OnNmeaMessage);
			if (!_locationManager.AddNmeaListener(_nmeaListener, _nmeaHandler))
			{
				_nmeaListener.Dispose();
				_nmeaListener = null;
				_nmeaHandler.Dispose();
				_nmeaHandler = null;
			}
		}
		catch
		{
			_nmeaListener?.Dispose();
			_nmeaListener = null;
			_nmeaHandler?.Dispose();
			_nmeaHandler = null;
		}
	}

	public void Stop()
	{
		if (!IsRunning)
		{
			return;
		}
		try
		{
			if (_nmeaListener != null && Build.VERSION.SdkInt >= BuildVersionCodes.N)
			{
				_locationManager.RemoveNmeaListener(_nmeaListener);
			}
		}
		catch
		{
		}
		_nmeaListener?.Dispose();
		_nmeaListener = null;
		_nmeaHandler?.Dispose();
		_nmeaHandler = null;
		try
		{
			_locationManager.RemoveUpdates(this);
		}
		catch
		{
		}
		IsRunning = false;
		_lastLocationElapsedRealtimeNanos = null;
		_lastNmeaTimestampMs = null;
		_lastNmeaSeenElapsedMs = long.MinValue;
		this.StatusChanged?.Invoke(this, "GNSS directo detenido");
	}

	public void OnLocationChanged(Location location)
	{
		long nowElapsedMs = SystemClock.ElapsedRealtime();
		if (_lastNmeaSeenElapsedMs != long.MinValue && nowElapsedMs - _lastNmeaSeenElapsedMs <= 700)
		{
			return;
		}
		DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
		bool hasSpeed = location.HasSpeed;
		double rawMps = (hasSpeed ? System.Math.Max(0.0, location.Speed) : double.NaN);
		double rawKmh = (hasSpeed ? (rawMps * 3.6) : double.NaN);
		double? intervalMs = null;
		if (Build.VERSION.SdkInt >= BuildVersionCodes.JellyBeanMr1)
		{
			long elapsed = location.ElapsedRealtimeNanos;
			long? lastLocationElapsedRealtimeNanos = _lastLocationElapsedRealtimeNanos;
			if (lastLocationElapsedRealtimeNanos.HasValue)
			{
				long previous = lastLocationElapsedRealtimeNanos.GetValueOrDefault();
				if (elapsed > previous)
				{
					intervalMs = (double)(elapsed - previous) / 1000000.0;
				}
			}
			_lastLocationElapsedRealtimeNanos = elapsed;
		}
		this.SpeedChanged?.Invoke(this, new GnssSpeedSample(receivedAt, rawKmh, rawMps, hasSpeed, location.Accuracy, intervalMs, "LOCATION.SPEED"));
	}

	private void OnNmeaMessage(string message, long timestampMs)
	{
		if (!TryReadNmeaSpeed(message, out double speedKmh, out string source) || !double.IsFinite(speedKmh) || speedKmh < 0.0)
		{
			return;
		}
		_lastNmeaSeenElapsedMs = SystemClock.ElapsedRealtime();
		double? intervalMs = null;
		long? lastNmeaTimestampMs = _lastNmeaTimestampMs;
		if (lastNmeaTimestampMs.HasValue)
		{
			long previous = lastNmeaTimestampMs.GetValueOrDefault();
			if (timestampMs > previous)
			{
				intervalMs = timestampMs - previous;
			}
		}
		_lastNmeaTimestampMs = timestampMs;
		this.SpeedChanged?.Invoke(this, new GnssSpeedSample(DateTimeOffset.UtcNow, speedKmh, speedKmh / 3.6, HasSpeed: true, double.NaN, intervalMs, source));
	}

	private static bool TryReadNmeaSpeed(string sentence, out double speedKmh, out string source)
	{
		speedKmh = double.NaN;
		source = "NMEA";
		int star = sentence.IndexOf('*');
		if (star >= 0)
		{
			sentence = sentence.Substring(0, star);
		}
		string[] parts = sentence.Trim().Split(',');
		if (parts.Length < 2)
		{
			return false;
		}
		string id = parts[0];
		if (id.EndsWith("VTG", StringComparison.OrdinalIgnoreCase) && parts.Length > 7 && double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var kmh))
		{
			speedKmh = System.Math.Max(0.0, kmh);
			source = "NMEA VTG";
			return true;
		}
		if (id.EndsWith("RMC", StringComparison.OrdinalIgnoreCase) && parts.Length > 7)
		{
			if (parts.Length > 2 && !string.Equals(parts[2], "A", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var knots))
			{
				speedKmh = System.Math.Max(0.0, knots * 1.852);
				source = "NMEA RMC";
				return true;
			}
		}
		return false;
	}

	public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
	{
		this.StatusChanged?.Invoke(this, $"{provider ?? "GPS"} · {status}");
	}

	public void OnProviderEnabled(string provider)
	{
		this.StatusChanged?.Invoke(this, provider.ToUpperInvariant() + " habilitado.");
	}

	public void OnProviderDisabled(string provider)
	{
		this.StatusChanged?.Invoke(this, provider.ToUpperInvariant() + " deshabilitado.");
	}

}
