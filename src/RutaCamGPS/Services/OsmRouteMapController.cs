using System;
using System.Collections.Generic;
using System.Linq;
using GPSCamRoute.Models;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using NetTopologySuite.Geometries;

namespace GPSCamRoute.Services;

public sealed class OsmRouteMapController
{
	private const string OsmUserAgent = "RutaCamGPS/1.9.2.1 (Android; com.elvismao.rutacamgps)";

	private readonly MapControl _mapControl;

	private readonly ILayer _baseLayer;

	private readonly MemoryLayer _routeLayer;

	private readonly MemoryLayer _positionLayer;

	public OsmRouteMapController(MapControl mapControl)
	{
		_mapControl = mapControl;
		_routeLayer = new MemoryLayer("Ruta GPS")
		{
			Style = null,
			Features = Array.Empty<IFeature>()
		};
		_positionLayer = new MemoryLayer("Posición actual")
		{
			Style = null,
			Features = Array.Empty<IFeature>()
		};
		Map map = new Map
		{
			CRS = "EPSG:3857"
		};
		map.Widgets.Clear();
		_baseLayer = OpenStreetMap.CreateTileLayer("RutaCamGPS/1.9.2.1 (Android; com.elvismao.rutacamgps)");
		map.Layers.Add(_baseLayer);
		map.Layers.Add(_routeLayer);
		map.Layers.Add(_positionLayer);
		_mapControl.Map = map;
	}

	public void Clear()
	{
		_routeLayer.Features = Array.Empty<IFeature>();
		_positionLayer.Features = Array.Empty<IFeature>();
		_routeLayer.DataHasChanged();
		_positionLayer.DataHasChanged();
		_mapControl.Refresh();
	}

	public void UpdateTrack(IReadOnlyList<RoutePoint> points, bool followCurrent)
	{
		if (points.Count == 0)
		{
			Clear();
			return;
		}
		if (points.Count >= 2)
		{
			Coordinate[] coordinates = (from p in points
				select Project(p.Longitude, p.Latitude) into p
				select new Coordinate(p.X, p.Y)).ToArray();
			GeometryFeature feature = new GeometryFeature(new LineString(coordinates));
			feature.Styles.Add(new VectorStyle
			{
				Line = new Pen(new Color(34, 197, 94), 2.5)
			});
			_routeLayer.Features = new IFeature[1] { feature };
		}
		else
		{
			_routeLayer.Features = Array.Empty<IFeature>();
		}
		RoutePoint last = points[points.Count - 1];
		MPoint current = Project(last.Longitude, last.Latitude);
		PointFeature pointFeature = new PointFeature(current);
		pointFeature.Styles.Add(new SymbolStyle
		{
			SymbolType = SymbolType.Ellipse,
			SymbolScale = 0.38,
			Fill = new Brush(new Color(34, 197, 94))
		});
		_positionLayer.Features = new IFeature[1] { pointFeature };
		_routeLayer.DataHasChanged();
		_positionLayer.DataHasChanged();
		if (followCurrent)
		{
			if (points.Count == 1)
			{
				double resolution = GetResolution(16, 2.5);
				_mapControl.Map.Navigator.CenterOnAndZoomTo(current, resolution, 0L);
			}
			else
			{
				_mapControl.Map.Navigator.CenterOn(current, 0L);
			}
		}
		_mapControl.Refresh();
	}

	public CameraXMapState CaptureCameraXMapState(IReadOnlyList<RoutePoint> points, bool includeBaseSnapshot)
	{
		byte[] baseMapPng = null;
		if (includeBaseSnapshot)
		{
			try
			{
				baseMapPng = _mapControl.GetSnapshot(new ILayer[1] { _baseLayer });
			}
			catch
			{
			}
		}
		Viewport viewport = _mapControl.Map.Navigator.Viewport;
		if (viewport.Width <= 1.0 || viewport.Height <= 1.0 || points.Count == 0)
		{
			return new CameraXMapState(baseMapPng, Array.Empty<CameraXMapPoint>(), null);
		}
		List<CameraXMapPoint> visible = new List<CameraXMapPoint>(Math.Min(points.Count, 600));
		int startIndex = Math.Max(0, points.Count - 2500);
		for (int i = startIndex; i < points.Count; i++)
		{
			MPoint projected = Project(points[i].Longitude, points[i].Latitude);
			ScreenPosition screen = viewport.WorldToScreen(projected);
			double nx = screen.X / viewport.Width;
			double ny = screen.Y / viewport.Height;
			if (nx >= -0.18 && nx <= 1.18 && ny >= -0.18 && ny <= 1.18)
			{
				visible.Add(new CameraXMapPoint(nx, ny));
				if (visible.Count > 600)
				{
					visible.RemoveAt(0);
				}
			}
		}
		MPoint lastProjected = Project(points[points.Count - 1].Longitude, points[points.Count - 1].Latitude);
		ScreenPosition lastScreen = viewport.WorldToScreen(lastProjected);
		return new CameraXMapState(CurrentPoint: new CameraXMapPoint(lastScreen.X / viewport.Width, lastScreen.Y / viewport.Height), BaseMapPng: baseMapPng, TrackPoints: visible.ToArray());
	}

	public void FitTrack(IReadOnlyList<RoutePoint> points)
	{
		UpdateTrack(points, followCurrent: false);
		MRect extent = _routeLayer.Extent;
		if ((object)extent != null)
		{
			double margin = Math.Max(extent.Width, extent.Height) * 0.12;
			_mapControl.Map.Navigator.ZoomToBox(extent.Grow(Math.Max(margin, 50.0)), MBoxFit.Fit, 0L);
		}
		else if (points.Count == 1)
		{
			MPoint point = Project(points[0].Longitude, points[0].Latitude);
			double resolution = GetResolution(15, 5.0);
			_mapControl.Map.Navigator.CenterOnAndZoomTo(point, resolution, 0L);
		}
		_mapControl.Refresh();
	}

	private double GetResolution(int level, double fallback)
	{
		IReadOnlyList<double> resolutions = _mapControl.Map.Navigator.Resolutions;
		if (resolutions.Count == 0)
		{
			return fallback;
		}
		return resolutions[Math.Min(level, resolutions.Count - 1)];
	}

	private static MPoint Project(double longitude, double latitude)
	{
		(double, double) projected = SphericalMercator.FromLonLat(longitude, latitude);
		return new MPoint(projected.Item1, projected.Item2);
	}
}
