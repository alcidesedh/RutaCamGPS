namespace GPSCamRoute.Models;

public sealed class AudioRouteResult
{
	public bool Success { get; init; }

	public bool UsedFallback { get; init; }

	public string ActiveDisplayName { get; init; } = "Automática";

	public string Message { get; init; } = string.Empty;
}
