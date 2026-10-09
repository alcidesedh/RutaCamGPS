using System;

namespace GPSCamRoute.Models;

public sealed record TelemetryOverlaySnapshot(double SpeedKmh, double DistanceKm, TimeSpan Elapsed, double? Latitude, double? Longitude, double? AltitudeMeters, bool IsRecording, byte[]? MapSnapshotPng);
