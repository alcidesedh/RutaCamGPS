using System;
using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Mapsui.UI.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace GPSCamRoute;

public partial class RouteDetailPage : ContentPage
{
	private readonly RouteRecord _record;

	private readonly VideoLibraryService _videoLibrary;

	private readonly RouteExportService _routeExport;

	private readonly RouteStorageService _storage;

	private readonly OsmRouteMapController _mapController;

	public RouteDetailPage(RouteRecord record, VideoLibraryService videoLibrary, RouteExportService routeExport, RouteStorageService storage)
	{
		InitializeComponent();
		_record = record;
		_videoLibrary = videoLibrary;
		_routeExport = routeExport;
		_storage = storage;
		_mapController = new OsmRouteMapController(DetailMap);
		DistanceValue.Text = record.DistanceText;
		AverageValue.Text = record.AverageSpeedText;
		MaxValue.Text = record.MaxSpeedText;
		DateValue.Text = record.DateText;
		DurationValue.Text = $"Duración: {record.DurationText} · {record.Points.Count} puntos GPS";
		VideoValue.Text = record.VideoLocationText;
		RecoverySummaryLabel.Text = record.RecoverySummaryText;
		RecoverySummaryLabel.IsVisible = record.WasRecovered;
		LoopSummaryLabel.Text = record.LoopSummaryText;
		LoopSummaryLabel.IsVisible = !string.IsNullOrWhiteSpace(record.LoopSummaryText);
		RefreshVideoUi();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_record.Points.Count != 0)
		{
			await Task.Delay(250);
			_mapController.FitTrack(_record.Points);
		}
	}

	private async void OnViewVideoClicked(object sender, EventArgs e)
	{
		try
		{
			await _videoLibrary.OpenVideoAsync(_record);
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Video", ex.Message, "OK");
		}
	}

	private async void OnShareVideoClicked(object sender, EventArgs e)
	{
		try
		{
			await _videoLibrary.ShareVideoAsync(_record);
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Compartir", ex.Message, "OK");
		}
	}

	private async void OnViewSegmentClicked(object sender, EventArgs e)
	{
		VideoSegment segment = default(VideoSegment);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			segment = commandParameter as VideoSegment;
			num = ((segment != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			try
			{
				await _videoLibrary.OpenSegmentAsync(segment);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync("Video", ex.Message, "OK");
			}
		}
	}

	private async void OnShareSegmentClicked(object sender, EventArgs e)
	{
		VideoSegment segment = default(VideoSegment);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			segment = commandParameter as VideoSegment;
			num = ((segment != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			try
			{
				await _videoLibrary.ShareSegmentAsync(segment);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync("Compartir", ex.Message, "OK");
			}
		}
	}

	private async void OnToggleProtectionClicked(object sender, EventArgs e)
	{
		VideoSegment segment = default(VideoSegment);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			segment = commandParameter as VideoSegment;
			num = ((segment != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			segment.IsProtected = !segment.IsProtected;
			segment.ProtectedAt = (segment.IsProtected ? new DateTimeOffset?(DateTimeOffset.UtcNow) : ((DateTimeOffset?)null));
			await _storage.SaveAsync(_record);
			RefreshVideoUi();
		}
	}

	private void RefreshVideoUi()
	{
		VideoValue.Text = _record.VideoLocationText;
		VideoActions.IsVisible = _record.HasPublicVideo;
		SegmentsPanel.IsVisible = _record.VideoSegments.Count > 0;
		SegmentsView.ItemsSource = null;
		SegmentsView.ItemsSource = _record.VideoSegments.OrderBy((VideoSegment x) => x.Index).ToList();
		DeleteLegacyVideoButton.IsVisible = _record.VideoSegments.Count == 0 && _record.HasAnyVideo;
		ViewVideoButton.Text = ((_record.VideoSegmentCount > 1) ? "VER PRIMER CLIP" : "VER VIDEO");
		ShareVideoButton.Text = ((_record.VideoSegmentCount > 1) ? "COMPARTIR TODOS" : "COMPARTIR");
		LoopSummaryLabel.Text = _record.LoopSummaryText;
		LoopSummaryLabel.IsVisible = !string.IsNullOrWhiteSpace(_record.LoopSummaryText);
	}

	private async void OnDeleteSegmentClicked(object sender, EventArgs e)
	{
		VideoSegment segment = default(VideoSegment);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			segment = commandParameter as VideoSegment;
			num = ((segment != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0 && await DisplayAlertAsync("Borrar video", string.Concat(str3: segment.IsProtected ? "\n\nEste clip está protegido, pero la eliminación manual es definitiva." : string.Empty, str0: "Se eliminará ", str1: segment.IndexText, str2: " de la Galería y del historial."), "BORRAR", "CANCELAR"))
		{
			if (!(await _videoLibrary.DeleteSegmentVideoAsync(segment)))
			{
				await DisplayAlertAsync("Borrar video", "Android no permitió eliminar completamente este archivo. El registro se conserva para volver a intentarlo.", "OK");
				return;
			}
			_record.VideoSegments.Remove(segment);
			await _storage.SaveAsync(_record);
			RefreshVideoUi();
		}
	}

	private async void OnDeleteLegacyVideoClicked(object sender, EventArgs e)
	{
		if (await DisplayAlertAsync("Borrar video", "Se eliminará definitivamente el video de Galería y su referencia en este recorrido.", "BORRAR", "CANCELAR"))
		{
			if (!(await _videoLibrary.DeleteLegacyVideoAsync(_record)))
			{
				await DisplayAlertAsync("Borrar video", "Android no permitió eliminar completamente este archivo. El registro se conserva para volver a intentarlo.", "OK");
				return;
			}
			await _storage.SaveAsync(_record);
			RefreshVideoUi();
		}
	}

	private async void OnGpxClicked(object sender, EventArgs e)
	{
		try
		{
			await _routeExport.ShareGpxAsync(_record);
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("GPX", ex.Message, "OK");
		}
	}

	private async void OnCsvClicked(object sender, EventArgs e)
	{
		try
		{
			await _routeExport.ShareCsvAsync(_record);
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("CSV", ex.Message, "OK");
		}
	}

	private async void OnDeleteRecordClicked(object sender, EventArgs e)
	{
		if (await DisplayAlertAsync("Borrar registro", "Se eliminará la ruta GPS del historial. El video de Galería se conservará.", "BORRAR", "CANCELAR"))
		{
			await _storage.DeleteAsync(_record.Id);
			await base.Navigation.PopAsync();
		}
	}

}
