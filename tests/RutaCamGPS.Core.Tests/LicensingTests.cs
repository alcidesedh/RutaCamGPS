using System;
using System.Security.Cryptography;
using RutaCam.Core.Licensing;
using Xunit;

namespace RutaCamGPS.Core.Tests;

public class LicensingTests
{
	[Fact]
	public void Base32Crockford_RoundTrip_PreservesData()
	{
		byte[] original = new byte[] { 0x00, 0x12, 0xAB, 0xCD, 0xEF, 0x42, 0xFF, 0x88 };
		string encoded = Base32Crockford.Encode(original);
		bool ok = Base32Crockford.TryDecode(encoded, out byte[] decoded);

		Assert.True(ok);
		Assert.Equal(original, decoded);
	}

	[Fact]
	public void Base32Crockford_Normalize_FixesAmbiguousCharacters()
	{
		string input = "o1-iL-99"; // 'o' should become '0', 'i' and 'L' should become '1', hyphens stripped
		string normalized = Base32Crockford.Normalize(input);
		Assert.Equal("011199", normalized);
	}

	[Fact]
	public void LicenseCodec_ValidSignature_VerifiesSuccessfully()
	{
		using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string deviceId = "DEV12345";
		var issuedAt = DateTimeOffset.UtcNow;
		var expiresAt = issuedAt.AddDays(30);

		string code = LicenseCodec.GenerateCode(1, LicenseTier.Pro, deviceId, issuedAt, expiresAt, ecdsa);

		using var verifier = ECDsa.Create();
		verifier.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);

		bool valid = LicenseCodec.TryParseAndVerify(code, deviceId, verifier, out var payload, out var err);

		Assert.True(valid, err);
		Assert.NotNull(payload);
		Assert.Equal(LicenseTier.Pro, payload.Tier);
		Assert.Equal(deviceId, payload.DeviceId);
		Assert.False(payload.IsLifetime);
	}

	[Fact]
	public void LicenseCodec_LifetimeLicense_HasInfiniteExpiry()
	{
		using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string deviceId = "LIFETIME01";
		var issuedAt = DateTimeOffset.UtcNow;
		var expiresAt = DateTimeOffset.MaxValue;

		string code = LicenseCodec.GenerateCode(1, LicenseTier.Enterprise, deviceId, issuedAt, expiresAt, ecdsa);

		using var verifier = ECDsa.Create();
		verifier.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);

		bool valid = LicenseCodec.TryParseAndVerify(code, deviceId, verifier, out var payload, out var err);

		Assert.True(valid, err);
		Assert.NotNull(payload);
		Assert.True(payload.IsLifetime);
		Assert.False(payload.IsExpired(DateTimeOffset.UtcNow.AddYears(100)));
	}

	[Fact]
	public void LicenseCodec_TamperedSignature_IsRejected()
	{
		using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string deviceId = "DEV12345";
		string code = LicenseCodec.GenerateCode(1, LicenseTier.Pro, deviceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ecdsa);

		// Tamper with the code
		char[] chars = code.ToCharArray();
		chars[chars.Length - 1] = chars[chars.Length - 1] == 'A' ? 'B' : 'A';
		string tampered = new string(chars);

		using var verifier = ECDsa.Create();
		verifier.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);

		bool valid = LicenseCodec.TryParseAndVerify(tampered, deviceId, verifier, out _, out var err);
		Assert.False(valid);
		Assert.NotNull(err);
	}

	[Fact]
	public void LicenseCodec_ExpiredLicense_IsRejected()
	{
		using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string deviceId = "DEV12345";
		var issuedAt = DateTimeOffset.UtcNow.AddDays(-60);
		var expiresAt = DateTimeOffset.UtcNow.AddDays(-1);

		string code = LicenseCodec.GenerateCode(1, LicenseTier.Pro, deviceId, issuedAt, expiresAt, ecdsa);

		using var verifier = ECDsa.Create();
		verifier.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);

		bool valid = LicenseCodec.TryParseAndVerify(code, deviceId, verifier, out _, out var err);
		Assert.False(valid);
		Assert.Contains("expiró", err, StringComparison.OrdinalIgnoreCase);
	}
}
