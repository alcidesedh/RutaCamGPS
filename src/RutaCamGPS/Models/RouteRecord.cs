using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace GPSCamRoute.Models;

public sealed class RouteRecord
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset EndedAt { get; set; }

	public double DistanceKm { get; set; }

	public double AverageSpeedKmh { get; set; }

	public double MaxSpeedKmh { get; set; }

	public string RecordingEngine { get; set; } = "Compatibilidad · MediaProjection";

	public string LoopRecordingMode { get; set; } = "Desactivada";

	public int DeletedByLoopCount { get; set; }

	public bool WasRecovered { get; set; }

	public string RecoveryNote { get; set; } = string.Empty;

	public string VideoPath { get; set; } = string.Empty;

	public string PublicVideoUri { get; set; } = string.Empty;

	public string VideoDisplayName { get; set; } = string.Empty;

	public string VideoRelativePath { get; set; } = string.Empty;

	public long VideoSizeBytes { get; set; }

	public List<VideoSegment> VideoSegments { get; set; } = new List<VideoSegment>();

	public List<RoutePoint> Points { get; set; } = new List<RoutePoint>();

	[JsonIgnore]
	public TimeSpan Duration => (EndedAt > StartedAt) ? (EndedAt - StartedAt) : TimeSpan.Zero;

	[JsonIgnore]
	public string DateText => StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

	[JsonIgnore]
	public string DistanceText => $"{DistanceKm:F2} km";

	[JsonIgnore]
	public string AverageSpeedText => $"{AverageSpeedKmh:F1} km/h";

	[JsonIgnore]
	public string MaxSpeedText => $"{MaxSpeedKmh:F1} km/h";

	[JsonIgnore]
	public string DurationText => (Duration.TotalHours >= 1.0) ? $"{(int)Duration.TotalHours:00}:{Duration.Minutes:00}:{Duration.Seconds:00}" : $"{Duration.Minutes:00}:{Duration.Seconds:00}";

	[JsonIgnore]
	public bool HasPublicVideo => VideoSegments.Any((VideoSegment x) => x.HasPublicVideo) || !string.IsNullOrWhiteSpace(PublicVideoUri);

	[JsonIgnore]
	public bool HasPrivateVideo => VideoSegments.Any((VideoSegment x) => x.HasPrivateVideo) || (!string.IsNullOrWhiteSpace(VideoPath) && File.Exists(VideoPath));

	[JsonIgnore]
	public bool HasAnyVideo => HasPublicVideo || HasPrivateVideo;

	[JsonIgnore]
	public int VideoSegmentCount => (VideoSegments.Count > 0) ? VideoSegments.Count : (HasAnyVideo ? 1 : 0);

	[JsonIgnore]
	public string VideoCountText => (VideoSegmentCount <= 1) ? "1 video" : $"{VideoSegmentCount} videos";

	[JsonIgnore]
	public int ProtectedSegmentCount => VideoSegments.Count((VideoSegment x) => x.IsProtected);

	[JsonIgnore]
	public string RecoverySummaryText => WasRecovered ? "RECUPERADA TRAS CIERRE INESPERADO" : string.Empty;

	[JsonIgnore]
	public string LoopSummaryText
	{
		get
		{
			if (string.IsNullOrWhiteSpace(LoopRecordingMode) || LoopRecordingMode.Equals("Desactivada", StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}
			string protectedText = ((ProtectedSegmentCount > 0) ? $" · {ProtectedSegmentCount} protegidos" : string.Empty);
			string deletedText = ((DeletedByLoopCount > 0) ? $" · {DeletedByLoopCount} liberados" : string.Empty);
			return "Bucle " + LoopRecordingMode + protectedText + deletedText;
		}
	}

	[JsonIgnore]
	public string VideoButtonText => (VideoSegmentCount > 1) ? "VIDEOS" : "VIDEO";

	[JsonIgnore]
	public string ShareButtonText => (VideoSegmentCount > 1) ? "TODOS" : "COMPARTIR";

	[JsonIgnore]
	public string VideoLocationText => (VideoSegments.Count > 1) ? $"{VideoSegments.Count} segmentos · {VideoSegments.Count((VideoSegment x) => x.HasPublicVideo)} en Galería" : ((VideoSegments.Count == 1) ? VideoSegments[0].LocationText : (HasPublicVideo ? ((string.IsNullOrWhiteSpace(VideoRelativePath) ? "Movies/RutaCam GPS" : VideoRelativePath) + "/" + VideoDisplayName) : (HasPrivateVideo ? "Video pendiente de publicar en Galería" : "Sin video disponible")));

	[JsonIgnore]
	public long TotalVideoSizeBytes => (VideoSegments.Count > 0) ? VideoSegments.Sum((VideoSegment x) => Math.Max(0L, x.SizeBytes)) : VideoSizeBytes;

	[JsonIgnore]
	public string VideoSizeText => (TotalVideoSizeBytes <= 0) ? string.Empty : ((TotalVideoSizeBytes >= 1073741824) ? $"{(double)TotalVideoSizeBytes / 1073741824.0:F2} GB" : $"{(double)TotalVideoSizeBytes / 1048576.0:F1} MB");
}
