using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using RutaCam.Core.Licensing;

namespace RutaCam.Keygen;

public class Program
{
	private const string KeysFileName = "master_keys.json";

	public static int Main(string[] args)
	{
		string repoRoot = FindRepoRoot();
		string keysPath = Path.Combine(repoRoot, "tools", "keygen", KeysFileName);
		string corePublicKeyPath = Path.Combine(repoRoot, "src", "RutaCamGPS.Core", "Licensing", "MasterPublicKey.cs");

		if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
		{
			PrintHelp();
			return 0;
		}

		string command = args[0].ToLowerInvariant();
		try
		{
			switch (command)
			{
				case "init-keys":
					return InitKeys(keysPath, corePublicKeyPath);

				case "generate":
					return GenerateLicense(args, keysPath);

				case "verify":
					return VerifyLicense(args, keysPath);

				default:
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"Comando desconocido: '{command}'");
					Console.ResetColor();
					PrintHelp();
					return 1;
			}
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Error: {ex.Message}");
			Console.ResetColor();
			return 1;
		}
	}

	private static void PrintHelp()
	{
		Console.WriteLine("==========================================================");
		Console.WriteLine("  RutaCam GPS — Generador y Administrador de Licencias");
		Console.WriteLine("==========================================================");
		Console.WriteLine();
		Console.WriteLine("Uso:");
		Console.WriteLine("  dotnet run --project tools/keygen -- init-keys");
		Console.WriteLine("      Genera el par de claves ECDSA NIST P-256 y actualiza MasterPublicKey.cs.");
		Console.WriteLine();
		Console.WriteLine("  dotnet run --project tools/keygen -- generate --device <ID> [--days <N> | --lifetime] [--tier <Standard|Pro|Enterprise>]");
		Console.WriteLine("      Genera un código de activación firmado para un dispositivo específico.");
		Console.WriteLine("      Por defecto: 365 días, Tier Pro.");
		Console.WriteLine();
		Console.WriteLine("  dotnet run --project tools/keygen -- verify --device <ID> --code <CODIGO>");
		Console.WriteLine("      Verifica criptográficamente un código de activación.");
		Console.WriteLine();
	}

	private static int InitKeys(string keysPath, string corePublicKeyPath)
	{
		if (File.Exists(keysPath))
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"El archivo de claves ya existe en: {keysPath}");
			Console.WriteLine("Si deseas regenerar las claves, borra ese archivo manualmente.");
			Console.ResetColor();
			return 0;
		}

		using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		byte[] privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
		byte[] publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

		string privBase64 = Convert.ToBase64String(privateKeyBytes);
		string pubBase64 = Convert.ToBase64String(publicKeyBytes);

		var keyData = new KeyStore(privBase64, pubBase64, DateTimeOffset.UtcNow);
		string json = JsonSerializer.Serialize(keyData, new JsonSerializerOptions { WriteIndented = true });
		Directory.CreateDirectory(Path.GetDirectoryName(keysPath)!);
		File.WriteAllText(keysPath, json);

		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine($"Claves maestras generadas exitosamente en:\n  {keysPath}");
		Console.ResetColor();

		UpdateCoreMasterPublicKey(corePublicKeyPath, pubBase64);
		return 0;
	}

	public static void UpdateCoreMasterPublicKey(string targetFile, string pubBase64)
	{
		string content = $@"namespace RutaCam.Core.Licensing;

/// <summary>
/// Clave pública maestra para validación de licencias fuera de línea.
/// Generada automáticamente por tools/keygen.
/// </summary>
public static class MasterPublicKey
{{
	public const string PublicKeyBase64 = ""{pubBase64}"";

	private static readonly Lazy<System.Security.Cryptography.ECDsa> _verifier = new(() =>
	{{
		var ecdsa = System.Security.Cryptography.ECDsa.Create();
		byte[] pubBytes = System.Convert.FromBase64String(PublicKeyBase64);
		ecdsa.ImportSubjectPublicKeyInfo(pubBytes, out _);
		return ecdsa;
	}});

	public static System.Security.Cryptography.ECDsa Instance => _verifier.Value;
}}
";
		Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
		File.WriteAllText(targetFile, content);
		Console.WriteLine($"MasterPublicKey.cs actualizado en:\n  {targetFile}");
	}

	private static int GenerateLicense(string[] args, string keysPath)
	{
		if (!File.Exists(keysPath))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("No se encontraron las claves maestras. Ejecuta primero: init-keys");
			Console.ResetColor();
			return 1;
		}

		string deviceId = "";
		int days = 365;
		bool isLifetime = false;
		LicenseTier tier = LicenseTier.Pro;

		for (int i = 1; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				deviceId = args[++i];
			}
			else if (arg.Equals("--days", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				days = int.Parse(args[++i]);
			}
			else if (arg.Equals("--lifetime", StringComparison.OrdinalIgnoreCase))
			{
				isLifetime = true;
			}
			else if (arg.Equals("--tier", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				tier = Enum.Parse<LicenseTier>(args[++i], ignoreCase: true);
			}
		}

		if (string.IsNullOrWhiteSpace(deviceId))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Error: El parámetro --device <ID> es obligatorio.");
			Console.ResetColor();
			return 1;
		}

		string json = File.ReadAllText(keysPath);
		var keyStore = JsonSerializer.Deserialize<KeyStore>(json);
		if (keyStore == null || string.IsNullOrWhiteSpace(keyStore.PrivateKeyBase64))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Error al leer la clave privada maestra.");
			Console.ResetColor();
			return 1;
		}

		using var ecdsa = ECDsa.Create();
		byte[] privBytes = Convert.FromBase64String(keyStore.PrivateKeyBase64);
		ecdsa.ImportPkcs8PrivateKey(privBytes, out _);

		DateTimeOffset now = DateTimeOffset.UtcNow;
		DateTimeOffset expiry = isLifetime ? DateTimeOffset.MaxValue : now.AddDays(days);

		string code = LicenseCodec.GenerateCode(
			LicenseCodec.CurrentVersion,
			tier,
			deviceId,
			now,
			expiry,
			ecdsa
		);

		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("==========================================================");
		Console.WriteLine("                LICENCIA RUTACAM GPS GENERADA             ");
		Console.WriteLine("==========================================================");
		Console.ResetColor();
		Console.WriteLine($"Dispositivo ID : {Base32Crockford.Normalize(deviceId)}");
		Console.WriteLine($"Nivel          : {tier}");
		Console.WriteLine($"Emitido        : {now.ToLocalTime():dd/MM/yyyy HH:mm}");
		Console.WriteLine($"Expiración     : {(isLifetime ? "PERMANENTE (De por vida)" : expiry.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))}");
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine("CÓDIGO DE ACTIVACIÓN (copiar y enviar al usuario):");
		Console.WriteLine(code);
		Console.ResetColor();
		Console.WriteLine();
		Console.WriteLine("Mensaje de respuesta para WhatsApp:");
		Console.WriteLine("----------------------------------------------------------");
		string whatsappReply = $"¡Hola! Tu licencia para RutaCam GPS está lista.\n\nCódigo de activación:\n{code}\n\nDuración: {(isLifetime ? "Permanente" : $"{days} días (hasta {expiry.ToLocalTime():dd/MM/yyyy})")}\n\nIngresa este código en Ajustes > Licencia > Ingresar código.";
		Console.WriteLine(whatsappReply);
		Console.WriteLine("----------------------------------------------------------");
		return 0;
	}

	private static int VerifyLicense(string[] args, string keysPath)
	{
		string deviceId = "";
		string code = "";

		for (int i = 1; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				deviceId = args[++i];
			}
			else if (arg.Equals("--code", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
			{
				code = args[++i];
			}
		}

		if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(code))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Error: Se requieren --device <ID> y --code <CODIGO>.");
			Console.ResetColor();
			return 1;
		}

		string json = File.ReadAllText(keysPath);
		var keyStore = JsonSerializer.Deserialize<KeyStore>(json);
		if (keyStore == null || string.IsNullOrWhiteSpace(keyStore.PublicKeyBase64))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Error al leer la clave pública maestra.");
			Console.ResetColor();
			return 1;
		}

		using var verifier = ECDsa.Create();
		byte[] pubBytes = Convert.FromBase64String(keyStore.PublicKeyBase64);
		verifier.ImportSubjectPublicKeyInfo(pubBytes, out _);

		bool valid = LicenseCodec.TryParseAndVerify(code, deviceId, verifier, out var payload, out var error);
		if (valid && payload != null)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("VALIDACIÓN EXITOSA:");
			Console.WriteLine($"  Dispositivo : {payload.DeviceId}");
			Console.WriteLine($"  Nivel       : {payload.Tier}");
			Console.WriteLine($"  Emitido     : {payload.IssuedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
			Console.WriteLine($"  Expiración  : {(payload.IsLifetime ? "PERMANENTE" : payload.ExpiresAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))}");
			Console.ResetColor();
			return 0;
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"VALIDACIÓN FALLIDA: {error}");
			Console.ResetColor();
			return 1;
		}
	}

	private static string FindRepoRoot()
	{
		string dir = Directory.GetCurrentDirectory();
		while (!string.IsNullOrEmpty(dir))
		{
			if (Directory.Exists(Path.Combine(dir, "src", "RutaCamGPS")))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir)!;
		}
		dir = AppDomain.CurrentDomain.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			if (Directory.Exists(Path.Combine(dir, "src", "RutaCamGPS")))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir)!;
		}
		throw new InvalidOperationException("No se pudo localizar la raíz del repositorio.");
	}

	private record KeyStore(string PrivateKeyBase64, string PublicKeyBase64, DateTimeOffset GeneratedAtUtc);
}
