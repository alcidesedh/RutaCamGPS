namespace RutaCam.Core.Licensing;

public enum LicenseTier : byte
{
	Standard = 1,
	Pro = 2,
	Enterprise = 3
}

public sealed record LicensePayload(
	byte Version,
	LicenseTier Tier,
	string DeviceId,
	DateTimeOffset IssuedAtUtc,
	DateTimeOffset ExpiresAtUtc
)
{
	public bool IsLifetime => ExpiresAtUtc >= DateTimeOffset.UtcNow.AddYears(50);

	public bool IsExpired(DateTimeOffset nowUtc)
	{
		return !IsLifetime && nowUtc > ExpiresAtUtc;
	}
}
