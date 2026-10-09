using System;
using System.Collections.Generic;
using System.Text;

namespace GPSCamRoute.Models;

public sealed class CameraDiagnosticReport
{
	public string DeviceName { get; init; } = string.Empty;

	public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

	public IReadOnlyList<CameraDiagnosticEntry> Cameras { get; init; } = Array.Empty<CameraDiagnosticEntry>();

	public string GeneralError { get; init; } = string.Empty;

	public string ToDiagnosticText()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("RUTACAM · DIAGNÓSTICO DE CÁMARAS V3");
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, stringBuilder);
		handler.AppendFormatted(DeviceName);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, stringBuilder);
		handler.AppendFormatted(GeneratedAt, "yyyy-MM-dd HH:mm:ss");
		stringBuilder3.AppendLine(ref handler);
		sb.AppendLine();
		if (!string.IsNullOrWhiteSpace(GeneralError))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder);
			handler.AppendLiteral("Error general: ");
			handler.AppendFormatted(GeneralError);
			stringBuilder4.AppendLine(ref handler);
			return sb.ToString().TrimEnd();
		}
		if (Cameras.Count == 0)
		{
			sb.AppendLine("Android no devolvió cámaras para analizar.");
			return sb.ToString().TrimEnd();
		}
		foreach (CameraDiagnosticEntry camera in Cameras)
		{
			sb.AppendLine(camera.ToDiagnosticText());
			sb.AppendLine();
		}
		return sb.ToString().TrimEnd();
	}
}
