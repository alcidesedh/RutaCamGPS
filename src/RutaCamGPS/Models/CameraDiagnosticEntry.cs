using System;
using System.Collections.Generic;
using System.Text;

namespace GPSCamRoute.Models;

public sealed class CameraDiagnosticEntry
{
	public string CameraId { get; init; } = string.Empty;

	public string Kind { get; init; } = "Lógica";

	public string Facing { get; init; } = "Desconocida";

	public string HardwareLevel { get; init; } = "Desconocido";

	public string ParentLogicalCameraId { get; init; } = string.Empty;

	public string FocalLengths { get; init; } = "No publicadas";

	public IReadOnlyList<string> PhysicalCameraIds { get; init; } = Array.Empty<string>();

	public bool OisAvailable { get; init; }

	public bool EisAvailable { get; init; }

	public bool PreviewStabilizationAvailable { get; init; }

	public string CropTypeText { get; init; } = "Desconocido";

	public bool? CameraXVideoStabilizationAvailable { get; init; }

	public bool? CameraXPreviewStabilizationAvailable { get; init; }

	public string CameraXStatus { get; init; } = "No consultado";

	public string Error { get; init; } = string.Empty;

	public string ToDiagnosticText()
	{
		StringBuilder sb = new StringBuilder();
		string parent = (string.IsNullOrWhiteSpace(ParentLogicalCameraId) ? string.Empty : (" · padre " + ParentLogicalCameraId));
		StringBuilder stringBuilder = sb;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 5, stringBuilder);
		handler.AppendFormatted(Kind);
		handler.AppendLiteral(" ");
		handler.AppendFormatted(CameraId);
		handler.AppendLiteral(" · ");
		handler.AppendFormatted(Facing);
		handler.AppendLiteral(" · ");
		handler.AppendFormatted(HardwareLevel);
		handler.AppendFormatted(parent);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
		handler.AppendLiteral("  Focal: ");
		handler.AppendFormatted(FocalLengths);
		stringBuilder3.AppendLine(ref handler);
		if (PhysicalCameraIds.Count > 0)
		{
			stringBuilder = sb;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
			handler.AppendLiteral("  Físicas publicadas: ");
			handler.AppendFormatted(string.Join(", ", PhysicalCameraIds));
			stringBuilder4.AppendLine(ref handler);
		}
		stringBuilder = sb;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(37, 3, stringBuilder);
		handler.AppendLiteral("  Camera2 → OIS: ");
		handler.AppendFormatted(OisAvailable ? "Sí" : "No");
		handler.AppendLiteral(" · ");
		handler.AppendLiteral("EIS: ");
		handler.AppendFormatted(EisAvailable ? "Sí" : "No");
		handler.AppendLiteral(" · ");
		handler.AppendLiteral("Preview: ");
		handler.AppendFormatted(PreviewStabilizationAvailable ? "Sí" : "No");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder);
		handler.AppendLiteral("  Crop Camera2: ");
		handler.AppendFormatted(CropTypeText);
		stringBuilder6.AppendLine(ref handler);
		stringBuilder = sb;
		StringBuilder stringBuilder7 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(41, 2, stringBuilder);
		handler.AppendLiteral("  CameraX → Video stab: ");
		handler.AppendFormatted(FormatNullable(CameraXVideoStabilizationAvailable));
		handler.AppendLiteral(" · ");
		handler.AppendLiteral("Preview stab: ");
		handler.AppendFormatted(FormatNullable(CameraXPreviewStabilizationAvailable));
		stringBuilder7.AppendLine(ref handler);
		if (!string.IsNullOrWhiteSpace(CameraXStatus))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder8 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
			handler.AppendLiteral("  CameraX: ");
			handler.AppendFormatted(CameraXStatus);
			stringBuilder8.AppendLine(ref handler);
		}
		if (!string.IsNullOrWhiteSpace(Error))
		{
			stringBuilder = sb;
			StringBuilder stringBuilder9 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
			handler.AppendLiteral("  Error: ");
			handler.AppendFormatted(Error);
			stringBuilder9.AppendLine(ref handler);
		}
		return sb.ToString().TrimEnd();
	}

	private static string FormatNullable(bool? value)
	{
		return (!value.HasValue) ? "No determinado" : (value.Value ? "Sí" : "No");
	}
}
