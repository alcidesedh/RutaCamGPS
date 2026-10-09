using System;
using RutaCam.Core.Gps;
using Xunit;

namespace RutaCamGPS.Core.Tests;

public class GpsSpeedCalculatorTests
{
	[Fact]
	public void CalculateDistanceMeters_KnownCoordinates_ReturnsExpectedDistance()
	{
		// Distance between Times Square (40.7580, -73.9855) and Empire State Building (40.7484, -73.9857) ~ 1070 meters
		double lat1 = 40.7580;
		double lon1 = -73.9855;
		double lat2 = 40.7484;
		double lon2 = -73.9857;

		double distance = GpsSpeedCalculator.CalculateDistanceMeters(lat1, lon1, lat2, lon2);

		Assert.InRange(distance, 1000.0, 1150.0);
	}

	[Fact]
	public void CalculateDisplacementSpeedKmh_CalculatesAccurately()
	{
		// 100 meters in 3.6 seconds = 100 km/h
		double distanceMeters = 100.0;
		double elapsedSeconds = 3.6;

		double speed = GpsSpeedCalculator.CalculateDisplacementSpeedKmh(distanceMeters, elapsedSeconds);

		Assert.Equal(100.0, speed, 2);
	}

	[Fact]
	public void CalculateDisplacementSpeedKmh_ZeroTime_ReturnsZero()
	{
		double speed = GpsSpeedCalculator.CalculateDisplacementSpeedKmh(100.0, 0.0);
		Assert.Equal(0.0, speed);
	}

	[Fact]
	public void ApplySpeedResponse_ExcessiveAcceleration_ClampsToPhysicalLimit()
	{
		var filter = GpsFilterConfig.Balanced; // MaxAcceleration = 22 km/h/s
		double currentSpeed = 50.0;
		double candidateGlitchSpeed = 150.0; // Jump of 100 km/h in 1 second!
		double elapsedSeconds = 1.0;

		double filteredSpeed = GpsSpeedCalculator.ApplySpeedResponse(currentSpeed, candidateGlitchSpeed, elapsedSeconds, filter);

		// Should clamp to 50 + 22 = 72 km/h
		Assert.Equal(72.0, filteredSpeed, 1);
	}

	[Fact]
	public void ApplySpeedResponse_ExcessiveDeceleration_ClampsToPhysicalLimit()
	{
		var filter = GpsFilterConfig.Balanced; // MaxDeceleration = 36 km/h/s
		double currentSpeed = 100.0;
		double candidateGlitchSpeed = 0.0; // Jump down to 0 in 1 second!
		double elapsedSeconds = 1.0;

		double filteredSpeed = GpsSpeedCalculator.ApplySpeedResponse(currentSpeed, candidateGlitchSpeed, elapsedSeconds, filter);

		// Should clamp to 100 - 36 = 64 km/h
		Assert.Equal(64.0, filteredSpeed, 1);
	}
}
