namespace GPSCamRoute.Models;

public sealed class AudioInputOption
{
	public string Id { get; init; } = "automatic";

	public string DisplayName { get; init; } = "Automática";

	public int DeviceType { get; init; }

	public string ProductName { get; init; } = string.Empty;

	public bool IsBluetooth { get; init; }

	public bool IsBuiltIn { get; init; }

	public override string ToString()
	{
		return DisplayName;
	}
}
