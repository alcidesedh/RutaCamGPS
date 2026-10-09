using System;
using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class DeviceHealthService
{
	public double BatteryPercent => TryGetAndroidBatteryPercent();

	public bool IsCharging => TryGetAndroidChargingState() == true;

	private static double TryGetAndroidBatteryPercent()
	{
		try
		{
			Context context = Application.Context;
			IntentFilter filter = new IntentFilter("android.intent.action.BATTERY_CHANGED");
			Intent intent = context.RegisterReceiver(null, filter);
			if (intent == null)
			{
				return -1.0;
			}
			int level = intent.GetIntExtra("level", -1);
			int scale = intent.GetIntExtra("scale", -1);
			if (level < 0 || scale <= 0)
			{
				return -1.0;
			}
			return Math.Clamp((double)level * 100.0 / (double)scale, 0.0, 100.0);
		}
		catch
		{
			return -1.0;
		}
	}

	private static bool? TryGetAndroidChargingState()
	{
		try
		{
			Context context = Application.Context;
			IntentFilter filter = new IntentFilter("android.intent.action.BATTERY_CHANGED");
			Intent intent = context.RegisterReceiver(null, filter);
			if (intent == null)
			{
				return null;
			}
			int status = intent.GetIntExtra("status", -1);
			if (status < 0)
			{
				return null;
			}
			return status == 2 || status == 5;
		}
		catch
		{
			return null;
		}
	}

	public long GetAvailableStorageBytes()
	{
		try
		{
			using StatFs stat = new StatFs(FileSystem.AppDataDirectory);
			return stat.AvailableBytes;
		}
		catch
		{
			return -1L;
		}
	}

	public static double BytesToGb(long bytes)
	{
		return (bytes < 0) ? (-1.0) : ((double)bytes / 1024.0 / 1024.0 / 1024.0);
	}

	public static double BytesToMb(long bytes)
	{
		return (bytes < 0) ? (-1.0) : ((double)bytes / 1024.0 / 1024.0);
	}

	public static string FormatStorage(long bytes)
	{
		if (bytes < 0)
		{
			return "ESPACIO --";
		}
		double gb = BytesToGb(bytes);
		return (gb >= 1.0) ? $"LIBRE {gb:F1} GB" : $"LIBRE {BytesToMb(bytes):F0} MB";
	}

	public string FormatBattery()
	{
		double value = BatteryPercent;
		if (value < 0.0)
		{
			return "BAT --";
		}
		return IsCharging ? $"BAT {value:F0}% ⚡" : $"BAT {value:F0}%";
	}
}
