using System;

namespace GPSCamRoute.Services;

public sealed record GnssSpeedSample(DateTimeOffset ReceivedAt, double SpeedKmh, double SpeedMetersPerSecond, bool HasSpeed, double AccuracyMeters, double? IntervalMilliseconds, string Provider);
