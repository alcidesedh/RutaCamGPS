using System;
using System.Collections.Generic;

namespace GPSCamRoute.Models;

public sealed class ActiveRecordingSession
{
	public Guid RouteId { get; set; } = Guid.NewGuid();

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	public string State { get; set; } = "Recording";

	public double DistanceKm { get; set; }

	public double MaxSpeedKmh { get; set; }

	public double CurrentSpeedKmh { get; set; }

	public string RecordingEngine { get; set; } = string.Empty;

	public string LoopRecordingMode { get; set; } = "Desactivada";

	public int DeletedByLoopCount { get; set; }

	public bool UsesVideoSegments { get; set; }

	public bool SessionSegmentationEnabled { get; set; }

	public string CurrentVideoPath { get; set; } = string.Empty;

	public int CurrentSegmentIndex { get; set; }

	public DateTimeOffset CurrentSegmentStartedAt { get; set; }

	public string SegmentFileStem { get; set; } = string.Empty;

	public List<int> ProtectedSegmentIndices { get; set; } = new List<int>();

	public List<VideoSegment> VideoSegments { get; set; } = new List<VideoSegment>();

	public List<RoutePoint> Points { get; set; } = new List<RoutePoint>();
}
