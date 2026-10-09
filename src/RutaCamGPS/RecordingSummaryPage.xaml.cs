using System;
using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Graphics;

namespace GPSCamRoute;

public partial class RecordingSummaryPage : ContentPage
{
	private readonly RouteRecord _record;

	private readonly VideoLibraryService _videoLibrary;

	private readonly RouteExportService _routeExport;

	public RecordingSummaryPage(RouteRecord record, VideoLibraryService videoLibrary, RouteExportService routeExport)
	{
		InitializeComponent();
		_record = record;
		_videoLibrary = videoLibrary;
		_routeExport = routeExport;
		SavedDateLabel.Text = record.DateText;
		EngineLabel.Text = "Motor: " + record.RecordingEngine;
		LoopSummaryLabel.Text = record.LoopSummaryText;
		LoopSummaryLabel.IsVisible = !string.IsNullOrWhiteSpace(record.LoopSummaryText);
		DistanceValue.Text = record.DistanceText;
		DurationValue.Text = record.DurationText;
		AverageValue.Text = record.AverageSpeedText;
		MaxValue.Text = record.MaxSpeedText;
		int publishedCount = ((record.VideoSegments.Count > 0) ? record.VideoSegments.Count((VideoSegment x) => x.HasPublicVideo) : (record.HasPublicVideo ? 1 : 0));
		if (record.HasPublicVideo)
		{
			VideoStatusLabel.Text = ((record.VideoSegmentCount > 1) ? $"✓ {publishedCount}/{record.VideoSegmentCount} clips guardados en Galería" : "✓ Video guardado en Galería");
			VideoStatusLabel.TextColor = Color.FromArgb("#43D19E");
			VideoLocationLabel.Text = record.VideoLocationText;
			VideoSizeLabel.Text = record.VideoSizeText;
			VideoErrorLabel.IsVisible = false;
		}
		else
		{
			VideoStatusLabel.Text = "Video conservado de forma segura";
			VideoLocationLabel.Text = "La copia pública no se completó. RutaCam conservará los MP4 internos y volverá a intentar publicarlos al abrir la app.";
			VideoSizeLabel.Text = record.VideoSizeText;
			VideoErrorLabel.IsVisible = false;
		}
		SegmentsPanel.IsVisible = record.VideoSegments.Count > 1;
		SegmentsView.ItemsSource = record.VideoSegments.OrderBy((VideoSegment x) => x.Index).ToList();
		ViewVideoButton.Text = ((record.VideoSegmentCount > 1) ? "VER PRIMER CLIP" : "VER VIDEO");
		ShareVideoButton.Text = ((record.VideoSegmentCount > 1) ? "COMPARTIR TODOS" : "COMPARTIR");
		ViewVideoButton.IsVisible = record.HasPublicVideo;
		ShareVideoButton.IsVisible = record.HasPublicVideo;
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

	private async void OnDoneClicked(object sender, EventArgs e)
	{
		await base.Navigation.PopAsync();
	}

}
