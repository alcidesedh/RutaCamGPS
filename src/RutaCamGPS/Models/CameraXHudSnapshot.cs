using System;

namespace GPSCamRoute.Models;

public sealed record CameraXHudSnapshot(double SpeedKmh, double DistanceKm, TimeSpan Elapsed, double AverageSpeedKmh, double MaxSpeedKmh, string GpsStatusText, string CoordinatesText, string AltitudeText, string BatteryText, string StorageText, bool IsRecording);
