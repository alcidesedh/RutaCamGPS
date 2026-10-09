namespace RutaCam.Core.Licensing;

/// <summary>
/// Clave pública maestra para validación de licencias fuera de línea.
/// Generada automáticamente por tools/keygen.
/// </summary>
public static class MasterPublicKey
{
	public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAERlf9A9FCMCckW+L9djSiGMdZ5ddk0PhnSReW8PkBMikjNigcLJiizytWToBGAM3934AhQ+LPARAxMSEFE3qY7w==";

	private static readonly Lazy<System.Security.Cryptography.ECDsa> _verifier = new(() =>
	{
		var ecdsa = System.Security.Cryptography.ECDsa.Create();
		byte[] pubBytes = System.Convert.FromBase64String(PublicKeyBase64);
		ecdsa.ImportSubjectPublicKeyInfo(pubBytes, out _);
		return ecdsa;
	});

	public static System.Security.Cryptography.ECDsa Instance => _verifier.Value;
}
