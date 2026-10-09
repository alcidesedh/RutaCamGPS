using System;

namespace GPSCamRoute.Services;

public sealed class ActivationResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public DateTimeOffset? ExpiresAt { get; init; }
}
