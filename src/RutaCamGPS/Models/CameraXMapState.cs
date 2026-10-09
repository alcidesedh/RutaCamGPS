using System;
using System.Runtime.CompilerServices;

namespace GPSCamRoute.Models;

public sealed record CameraXMapState(byte[]? BaseMapPng, CameraXMapPoint[] TrackPoints, CameraXMapPoint? CurrentPoint)
{
	public static CameraXMapState Empty { get; } = new CameraXMapState(null, Array.Empty<CameraXMapPoint>(), null);

	[CompilerGenerated]
	private CameraXMapState(CameraXMapState original)
	{
		BaseMapPng = original.BaseMapPng;
		TrackPoints = original.TrackPoints;
		CurrentPoint = original.CurrentPoint;
	}
}
