using CommunityToolkit.Maui;
using GPSCamRoute.Models;
using GPSCamRoute.Platforms.Android;
using GPSCamRoute.Platforms.Android.Recording;
using GPSCamRoute.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace GPSCamRoute;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>().UseSkiaSharp().UseMauiCommunityToolkitCamera();
		builder.Services.AddSingleton<RouteStorageService>();
		builder.Services.AddSingleton<RecordingSessionJournalService>();
		builder.Services.AddSingleton<ActivationService>();
		builder.Services.AddSingleton<OverlaySettingsService>();
		builder.Services.AddSingleton<VideoLibraryService>();
		builder.Services.AddSingleton<RouteExportService>();
		builder.Services.AddSingleton<DeviceHealthService>();
		builder.Services.AddSingleton<IScreenRecorderService, ScreenRecorderService>();
		builder.Services.AddSingleton<ICameraStabilizationService, CameraStabilizationService>();
		builder.Services.AddSingleton<ICameraXOverlayRecorderService, CameraXOverlayRecorderService>();
		builder.Services.AddSingleton<ICameraLensService, CameraLensService>();
		builder.Services.AddSingleton<IAudioInputService, AudioInputService>();
		builder.Services.AddSingleton<IGnssSpeedService, GnssSpeedService>();
		builder.Services.AddSingleton<TelemetryOverlayState>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<HistoryPage>();
		builder.Services.AddTransient<OverlaySettingsPage>();
		return builder.Build();
	}
}
