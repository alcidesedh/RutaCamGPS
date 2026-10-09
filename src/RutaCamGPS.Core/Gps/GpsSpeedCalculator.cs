using System;

namespace RutaCam.Core.Gps;

public static class GpsSpeedCalculator
{
	private const double EarthRadiusMeters = 6371000.0;

	/// <summary>
	/// Calcula la distancia geodésica en metros entre dos coordenadas (fórmula Haversine).
	/// </summary>
	public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
	{
		double dLat = ToRadians(lat2 - lat1);
		double dLon = ToRadians(lon2 - lon1);

		double rLat1 = ToRadians(lat1);
		double rLat2 = ToRadians(lat2);

		double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
		           Math.Cos(rLat1) * Math.Cos(rLat2) *
		           Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);

		double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
		return EarthRadiusMeters * c;
	}

	/// <summary>
	/// Calcula la velocidad de desplazamiento en km/h entre dos puntos y un tiempo transcurrido en segundos.
	/// </summary>
	public static double CalculateDisplacementSpeedKmh(double distanceMeters, double elapsedSeconds)
	{
		if (elapsedSeconds <= 0.001 || double.IsNaN(distanceMeters) || distanceMeters < 0.0)
		{
			return 0.0;
		}

		double metersPerSecond = distanceMeters / elapsedSeconds;
		return metersPerSecond * 3.6;
	}

	/// <summary>
	/// Aplica el filtro de respuesta y límites físicos de aceleración/desaceleración automotriz.
	/// </summary>
	public static double ApplySpeedResponse(
		double currentSpeedKmh,
		double candidateSpeedKmh,
		double seconds,
		GpsFilterConfig filter
	)
	{
		if (!double.IsFinite(candidateSpeedKmh) || candidateSpeedKmh <= 0.0)
		{
			candidateSpeedKmh = 0.0;
		}

		if (seconds <= 0.0 || !double.IsFinite(currentSpeedKmh))
		{
			return Math.Clamp(candidateSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh);
		}

		double delta = candidateSpeedKmh - currentSpeedKmh;
		if (delta > 0.0)
		{
			double maxAllowedIncrease = filter.MaxAccelerationKmhPerSecond * seconds;
			if (delta > maxAllowedIncrease)
			{
				candidateSpeedKmh = currentSpeedKmh + maxAllowedIncrease;
			}
		}
		else if (delta < 0.0)
		{
			double maxAllowedDecrease = filter.MaxDecelerationKmhPerSecond * seconds;
			if (Math.Abs(delta) > maxAllowedDecrease)
			{
				candidateSpeedKmh = currentSpeedKmh - maxAllowedDecrease;
			}
		}

		return Math.Clamp(candidateSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh);
	}

	private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
