using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RutaCam.Core.Licensing;

public static class LicenseCodec
{
	public const string CodePrefix = "RCTA-";
	public const byte CurrentVersion = 1;
	private static readonly byte[] Magic = new byte[] { (byte)'R', (byte)'C' };

	public static byte[] SerializePayloadToSign(byte version, LicenseTier tier, string deviceId, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
	{
		string normDevice = Base32Crockford.Normalize(deviceId);
		byte[] deviceBytes = Encoding.UTF8.GetBytes(normDevice);

		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(Magic);
		writer.Write(version);
		writer.Write((byte)tier);
		writer.Write((byte)deviceBytes.Length);
		writer.Write(deviceBytes);

		uint issuedSec = (uint)Math.Clamp(issuedAt.ToUnixTimeSeconds(), 0, uint.MaxValue);
		uint expiresSec = expiresAt == DateTimeOffset.MaxValue 
			? uint.MaxValue 
			: (uint)Math.Clamp(expiresAt.ToUnixTimeSeconds(), 0, uint.MaxValue);

		writer.Write(issuedSec);
		writer.Write(expiresSec);
		writer.Flush();

		return ms.ToArray();
	}

	public static string GenerateCode(byte version, LicenseTier tier, string deviceId, DateTimeOffset issuedAt, DateTimeOffset expiresAt, ECDsa privateKey)
	{
		byte[] dataToSign = SerializePayloadToSign(version, tier, deviceId, issuedAt, expiresAt);
		byte[] signature = privateKey.SignData(dataToSign, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

		byte[] fullToken = new byte[dataToSign.Length + signature.Length];
		Buffer.BlockCopy(dataToSign, 0, fullToken, 0, dataToSign.Length);
		Buffer.BlockCopy(signature, 0, fullToken, dataToSign.Length, signature.Length);

		string encoded = Base32Crockford.Encode(fullToken);
		var sb = new StringBuilder(CodePrefix.Length + encoded.Length + (encoded.Length / 5) + 2);
		sb.Append(CodePrefix);
		for (int i = 0; i < encoded.Length; i++)
		{
			if (i > 0 && i % 5 == 0)
			{
				sb.Append('-');
			}
			sb.Append(encoded[i]);
		}
		return sb.ToString();
	}

	public static bool TryParseAndVerify(string code, string currentDeviceId, ECDsa publicKey, out LicensePayload? payload, out string? errorMessage)
	{
		payload = null;
		errorMessage = null;

		if (string.IsNullOrWhiteSpace(code))
		{
			errorMessage = "El código está vacío.";
			return false;
		}

		string raw = code.Trim();
		if (raw.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
		{
			raw = raw.Substring(CodePrefix.Length);
		}

		if (!Base32Crockford.TryDecode(raw, out byte[] fullToken))
		{
			errorMessage = "El formato del código no es válido.";
			return false;
		}

		const int minSize = 2 + 1 + 1 + 1 + 1 + 4 + 4 + 64;
		if (fullToken.Length < minSize)
		{
			errorMessage = "El código es demasiado corto o está incompleto.";
			return false;
		}

		int payloadLen = fullToken.Length - 64;
		byte[] dataToVerify = new byte[payloadLen];
		byte[] signature = new byte[64];
		Buffer.BlockCopy(fullToken, 0, dataToVerify, 0, payloadLen);
		Buffer.BlockCopy(fullToken, payloadLen, signature, 0, 64);

		using var ms = new MemoryStream(dataToVerify);
		using var reader = new BinaryReader(ms);

		byte m0 = reader.ReadByte();
		byte m1 = reader.ReadByte();
		if (m0 != Magic[0] || m1 != Magic[1])
		{
			errorMessage = "Encabezado de licencia inválido.";
			return false;
		}

		byte version = reader.ReadByte();
		if (version != 1)
		{
			errorMessage = $"Versión de licencia no soportada (v{version}).";
			return false;
		}

		byte tierByte = reader.ReadByte();
		LicenseTier tier = Enum.IsDefined(typeof(LicenseTier), tierByte) ? (LicenseTier)tierByte : LicenseTier.Standard;

		byte devLen = reader.ReadByte();
		if (devLen <= 0 || devLen > 64 || ms.Position + devLen > payloadLen - 8)
		{
			errorMessage = "Identificador de dispositivo corrupto en la licencia.";
			return false;
		}

		byte[] devBytes = reader.ReadBytes(devLen);
		string licenseDeviceId = Encoding.UTF8.GetString(devBytes);

		uint issuedSec = reader.ReadUInt32();
		uint expiresSec = reader.ReadUInt32();

		DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedSec);
		DateTimeOffset expiresAt = expiresSec == uint.MaxValue 
			? DateTimeOffset.MaxValue 
			: DateTimeOffset.FromUnixTimeSeconds(expiresSec);

		bool sigOk = publicKey.VerifyData(dataToVerify, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
		if (!sigOk)
		{
			errorMessage = "La firma criptográfica no es válida. El código fue alterado o no es auténtico.";
			return false;
		}

		string normCurrentDevice = Base32Crockford.Normalize(currentDeviceId);
		string normLicenseDevice = Base32Crockford.Normalize(licenseDeviceId);
		if (!string.Equals(normCurrentDevice, normLicenseDevice, StringComparison.OrdinalIgnoreCase))
		{
			errorMessage = $"Este código fue emitido para otro dispositivo ({licenseDeviceId}). Tu dispositivo es {normCurrentDevice}.";
			return false;
		}

		if (expiresAt != DateTimeOffset.MaxValue && expiresAt <= DateTimeOffset.UtcNow)
		{
			errorMessage = $"La licencia expiró el {expiresAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
			return false;
		}

		payload = new LicensePayload(version, tier, licenseDeviceId, issuedAt, expiresAt);
		return true;
	}
}
