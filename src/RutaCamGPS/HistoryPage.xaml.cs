using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace GPSCamRoute;

public partial class HistoryPage : ContentPage
{
	private readonly RouteStorageService _storage;

	private readonly VideoLibraryService _videoLibrary;

	private readonly RouteExportService _routeExport;

	public HistoryPage(RouteStorageService storage, VideoLibraryService videoLibrary, RouteExportService routeExport)
	{
		InitializeComponent();
		_storage = storage;
		_videoLibrary = videoLibrary;
		_routeExport = routeExport;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			await _videoLibrary.MigrateLegacyVideosAsync(_storage, CancellationToken.None);
		}
		catch
		{
		}
		await ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		CollectionView routesView = RoutesView;
		routesView.ItemsSource = await _storage.GetAllAsync();
	}

	private async void OnRouteClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			await base.Navigation.PushAsync(new RouteDetailPage(record, _videoLibrary, _routeExport, _storage));
		}
	}

	private async void OnVideoClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num == 0)
		{
			return;
		}
		if (record.VideoSegmentCount > 1)
		{
			await base.Navigation.PushAsync(new RouteDetailPage(record, _videoLibrary, _routeExport, _storage));
			return;
		}
		try
		{
			await _videoLibrary.OpenVideoAsync(record);
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Video", ex.Message, "OK");
		}
	}

	private async void OnShareClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			try
			{
				await _videoLibrary.ShareVideoAsync(record);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync("Compartir", ex.Message, "OK");
			}
		}
	}

	private async void OnDeleteVideoClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num == 0)
		{
			return;
		}
		int videoCount = Math.Max(1, record.VideoSegmentCount);
		string description = ((videoCount == 1) ? "el video y toda la información de este recorrido" : $"los {videoCount} videos y toda la información de este recorrido");
		if (await DisplayAlertAsync("Eliminar registro completo", "Se eliminarán definitivamente " + description + ": ruta GPS, puntos, estadísticas y registro del historial. Esta acción no se puede deshacer.", "ELIMINAR TODO", "CANCELAR"))
		{
			(int Deleted, int Failed) result = await _videoLibrary.DeleteAllRouteVideosAsync(record);
			if (result.Failed > 0)
			{
				await _storage.SaveAsync(record);
				await ReloadAsync();
				await DisplayAlertAsync("Eliminación incompleta", $"Android no permitió borrar {result.Failed} video(s). El registro se conserva para evitar dejar archivos sin referencia.", "OK");
			}
			else
			{
				await _storage.DeleteAsync(record.Id);
				await ReloadAsync();
				await DisplayAlertAsync("Registro eliminado", $"Se eliminó el recorrido completo y {result.Deleted} video(s).", "OK");
			}
		}
	}

	private async void OnGpxClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			try
			{
				await _routeExport.ShareGpxAsync(record);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync("GPX", ex.Message, "OK");
			}
		}
	}

	private async void OnCsvClicked(object sender, EventArgs e)
	{
		RouteRecord record = default(RouteRecord);
		int num;
		if (sender is Button { CommandParameter: var commandParameter })
		{
			record = commandParameter as RouteRecord;
			num = ((record != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			try
			{
				await _routeExport.ShareCsvAsync(record);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync("CSV", ex.Message, "OK");
			}
		}
	}

	private async void OnDeleteAllClicked(object sender, EventArgs e)
	{
		if (!(await DisplayAlertAsync("Limpiar historial completo", "Se eliminarán todos los recorridos, puntos GPS y videos asociados de RutaCam. Esta acción no se puede deshacer.", "ELIMINAR TODO", "CANCELAR")))
		{
			return;
		}
		IReadOnlyList<RouteRecord> records = await _storage.GetAllAsync();
		int deletedVideos = 0;
		int failedVideos = 0;
		foreach (RouteRecord record in records)
		{
			(int Deleted, int Failed) result = await _videoLibrary.DeleteAllRouteVideosAsync(record);
			deletedVideos += result.Deleted;
			failedVideos += result.Failed;
		}
		await _storage.DeleteAllAsync();
		await ReloadAsync();
		string message = ((failedVideos == 0) ? $"Historial eliminado. Videos borrados: {deletedVideos}." : $"El historial se eliminó, pero Android no permitió borrar {failedVideos} video(s). Videos borrados: {deletedVideos}.");
		await DisplayAlertAsync("Historial", message, "OK");
	}

}
