using System.Text;

namespace RutaCam.Core.Licensing;

/// <summary>Base32 Crockford (sin I, L, O, U): legible y tolerante a errores al copiar/dictar.</summary>
public static class Base32Crockford
{
	private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

	public static string Encode(ReadOnlySpan<byte> data)
	{
		var sb = new StringBuilder((data.Length * 8 + 4) / 5);
		int buffer = 0, bits = 0;
		foreach (byte b in data)
		{
			buffer = (buffer << 8) | b;
			bits += 8;
			while (bits >= 5)
			{
				sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
				bits -= 5;
			}
		}
		if (bits > 0)
		{
			sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
		}
		return sb.ToString();
	}

	public static bool TryDecode(string text, out byte[] data)
	{
		data = Array.Empty<byte>();
		var output = new List<byte>(text.Length * 5 / 8);
		int buffer = 0, bits = 0;
		foreach (char raw in text)
		{
			int value = Map(raw);
			if (value == -2)
			{
				continue; // separador ignorado
			}
			if (value < 0)
			{
				return false;
			}
			buffer = (buffer << 5) | value;
			bits += 5;
			if (bits >= 8)
			{
				output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
				bits -= 8;
			}
		}
		data = output.ToArray();
		return true;
	}

	/// <summary>Normaliza a mayúsculas, sustituye O→0, I/L→1 y elimina separadores.</summary>
	public static string Normalize(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		var sb = new StringBuilder(text.Length);
		foreach (char raw in text)
		{
			int v = Map(raw);
			if (v >= 0)
			{
				sb.Append(Alphabet[v]);
			}
		}
		return sb.ToString();
	}

	private static int Map(char c)
	{
		c = char.ToUpperInvariant(c);
		switch (c)
		{
			case '-': case ' ': case '\n': case '\r': case '\t': case '.': case '_':
				return -2;
			case 'O': return 0;
			case 'I': case 'L': return 1;
		}
		int idx = Alphabet.IndexOf(c);
		return idx;
	}
}
