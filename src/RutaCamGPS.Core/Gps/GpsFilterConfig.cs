using System;

namespace RutaCam.Core.Gps;

public sealed record GpsFilterConfig(
	double MaxAccuracyMeters,
	double MaxWeakAccuracyMeters,
	int InitialFixes,
	double MinimumMovementMeters,
	double MaximumNoiseMeters,
	double AccuracyNoiseFactor,
	double MaxVehicleSpeedKmh,
	TimeSpan MaximumAge,
	TimeSpan SignalHold,
	TimeSpan SignalTimeout,
	TimeSpan ResetAfter,
	TimeSpan StationaryConfirmation,
	double MaxAccelerationKmhPerSecond,
	double MaxDecelerationKmhPerSecond,
	double MaxVerticalAccuracyMeters
)
{
	public static GpsFilterConfig Precise => new(
		MaxAccuracyMeters: 20.0,
		MaxWeakAccuracyMeters: 45.0,
		InitialFixes: 2,
		MinimumMovementMeters: 1.5,
		MaximumNoiseMeters: 5.0,
		AccuracyNoiseFactor: 0.12,
		MaxVehicleSpeedKmh: 180.0,
		MaximumAge: TimeSpan.FromSeconds(2.0),
		SignalHold: TimeSpan.FromSeconds(1.8),
		SignalTimeout: TimeSpan.FromSeconds(4.0),
		ResetAfter: TimeSpan.FromSeconds(4.0),
		StationaryConfirmation: TimeSpan.FromSeconds(3.0),
		MaxAccelerationKmhPerSecond: 16.0,
		MaxDecelerationKmhPerSecond: 28.0,
		MaxVerticalAccuracyMeters: 18.0
	);

	public static GpsFilterConfig Balanced => new(
		MaxAccuracyMeters: 28.0,
		MaxWeakAccuracyMeters: 65.0,
		InitialFixes: 1,
		MinimumMovementMeters: 1.2,
		MaximumNoiseMeters: 7.0,
		AccuracyNoiseFactor: 0.15,
		MaxVehicleSpeedKmh: 195.0,
		MaximumAge: TimeSpan.FromSeconds(3.0),
		SignalHold: TimeSpan.FromSeconds(2.5),
		SignalTimeout: TimeSpan.FromSeconds(6.0),
		ResetAfter: TimeSpan.FromSeconds(6.0),
		StationaryConfirmation: TimeSpan.FromSeconds(2.5),
		MaxAccelerationKmhPerSecond: 22.0,
		MaxDecelerationKmhPerSecond: 36.0,
		MaxVerticalAccuracyMeters: 26.0
	);

	public static GpsFilterConfig Flexible => new(
		MaxAccuracyMeters: 40.0,
		MaxWeakAccuracyMeters: 90.0,
		InitialFixes: 1,
		MinimumMovementMeters: 1.0,
		MaximumNoiseMeters: 10.0,
		AccuracyNoiseFactor: 0.20,
		MaxVehicleSpeedKmh: 210.0,
		MaximumAge: TimeSpan.FromSeconds(4.5),
		SignalHold: TimeSpan.FromSeconds(3.5),
		SignalTimeout: TimeSpan.FromSeconds(8.0),
		ResetAfter: TimeSpan.FromSeconds(8.0),
		StationaryConfirmation: TimeSpan.FromSeconds(2.0),
		MaxAccelerationKmhPerSecond: 28.0,
		MaxDecelerationKmhPerSecond: 45.0,
		MaxVerticalAccuracyMeters: 38.0
	);

	public static GpsFilterConfig FromProfileName(string? profileName)
	{
		return (profileName ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"preciso" or "precise" => Precise,
			"flexible" => Flexible,
			_ => Balanced
		};
	}
}
