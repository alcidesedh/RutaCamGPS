using System;
using System.IO;
using System.Text.Json.Serialization;

namespace GPSCamRoute.Models;

public sealed class VideoSegment
{
	public int Index { get; set; }

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset EndedAt { get; set; }

	public string VideoPath { get; set; } = string.Empty;

	public string PublicVideoUri { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string RelativePath { get; set; } = string.Empty;

	public long SizeBytes { get; set; }

	public bool IsProtected { get; set; }

	public DateTimeOffset? ProtectedAt { get; set; }

	[JsonIgnore]
	public TimeSpan Duration => (EndedAt > StartedAt) ? (EndedAt - StartedAt) : TimeSpan.Zero;

	[JsonIgnore]
	public bool HasPublicVideo => !string.IsNullOrWhiteSpace(PublicVideoUri);

	[JsonIgnore]
	public bool HasPrivateVideo => !string.IsNullOrWhiteSpace(VideoPath) && File.Exists(VideoPath);

	[JsonIgnore]
	public bool HasVideo => HasPublicVideo || HasPrivateVideo;

	[JsonIgnore]
	public string IndexText => $"CLIP {Math.Max(1, Index):000}";

	[JsonIgnore]
	public string ProtectedText => IsProtected ? "\ud83d\udd12 PROTEGIDO" : string.Empty;

	[JsonIgnore]
	public string ProtectionButtonText => IsProtected ? "DESBLOQ." : "PROTEGER";

	[JsonIgnore]
	public string TimeRangeText => $"{StartedAt.ToLocalTime():HH:mm:ss} – {EndedAt.ToLocalTime():HH:mm:ss}";

	[JsonIgnore]
	public string DurationText => (Duration.TotalHours >= 1.0) ? $"{(int)Duration.TotalHours:00}:{Duration.Minutes:00}:{Duration.Seconds:00}" : $"{Duration.Minutes:00}:{Duration.Seconds:00}";

	[JsonIgnore]
	public string SizeText => (SizeBytes <= 0) ? string.Empty : ((SizeBytes >= 1073741824) ? $"{(double)SizeBytes / 1073741824.0:F2} GB" : $"{(double)SizeBytes / 1048576.0:F1} MB");

	[JsonIgnore]
	public string LocationText => HasPublicVideo ? ((string.IsNullOrWhiteSpace(RelativePath) ? "Movies/RutaCam GPS" : RelativePath) + "/" + DisplayName) : (HasPrivateVideo ? "Pendiente de publicar en Galería" : "Video no disponible");
}
