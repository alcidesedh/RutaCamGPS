using System;
using System.Linq;

namespace GPSCamRoute.Models;

public sealed class TelemetryOverlayState
{
	private readonly object _sync = new object();

	public double SpeedKmh { get; private set; }

	public double DistanceKm { get; private set; }

	public TimeSpan Elapsed { get; private set; }

	public double? Latitude { get; private set; }

	public double? Longitude { get; private set; }

	public double? AltitudeMeters { get; private set; }

	public bool IsRecording { get; private set; }

	public byte[]? LatestMapSnapshotPng { get; private set; }

	public void Update(double speedKmh, double distanceKm, TimeSpan elapsed, double? latitude, double? longitude, double? altitudeMeters, bool isRecording)
	{
		lock (_sync)
		{
			SpeedKmh = speedKmh;
			DistanceKm = distanceKm;
			Elapsed = elapsed;
			Latitude = latitude;
			Longitude = longitude;
			AltitudeMeters = altitudeMeters;
			IsRecording = isRecording;
		}
	}

	public void SetMapSnapshot(byte[]? png)
	{
		if (png == null || png.Length == 0)
		{
			return;
		}
		lock (_sync)
		{
			LatestMapSnapshotPng = png;
		}
	}

	public TelemetryOverlaySnapshot Snapshot()
	{
		lock (_sync)
		{
			return new TelemetryOverlaySnapshot(SpeedKmh, DistanceKm, Elapsed, Latitude, Longitude, AltitudeMeters, IsRecording, LatestMapSnapshotPng?.ToArray());
		}
	}
}
