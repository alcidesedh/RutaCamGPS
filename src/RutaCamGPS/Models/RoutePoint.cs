using System;

namespace GPSCamRoute.Models;

public sealed class RoutePoint
{
	public double Latitude { get; set; }

	public double Longitude { get; set; }

	public double SpeedKmh { get; set; }

	public double? ReportedSpeedKmh { get; set; }

	public double? DisplacementSpeedKmh { get; set; }

	public double? AccuracyMeters { get; set; }

	public double? AltitudeMeters { get; set; }

	public double? VerticalAccuracyMeters { get; set; }

	public double? CourseDegrees { get; set; }

	public double? FixAgeMilliseconds { get; set; }

	public bool ReducedAccuracy { get; set; }

	public bool IsFromMockProvider { get; set; }

	public DateTimeOffset Timestamp { get; set; }
}
