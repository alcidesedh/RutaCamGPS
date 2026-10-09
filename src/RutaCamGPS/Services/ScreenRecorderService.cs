using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using GPSCamRoute.Platforms.Android;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class ScreenRecorderService : IScreenRecorderService
{
	public bool IsRecording => ProjectionRecorderForegroundService.IsRecording;

	public async Task StartAsync(string outputPath, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			throw new ArgumentException("La ruta de salida del video no puede estar vacía.", "outputPath");
		}
		MainActivity activity = (Platform.CurrentActivity as MainActivity) ?? throw new InvalidOperationException("No se encontró la actividad Android activa.");
		ScreenCapturePermissionResult permission = await activity.RequestScreenCaptureAsync(cancellationToken);
		if (!permission.Granted || permission.Data == null)
		{
			throw new OperationCanceledException("La captura de pantalla fue cancelada por el usuario.", cancellationToken);
		}
		await activity.WaitUntilVisibleAsync(cancellationToken);
		string quality = Preferences.Default.Get("VideoQuality", "Alta · hasta 1080×1920");
		bool hideBars = Preferences.Default.Get("HideSystemBarsDuringRecording", defaultValue: true);
		bool lockOrientation = Preferences.Default.Get("LockOrientationDuringRecording", defaultValue: true);
		bool recordAudio = Preferences.Default.Get("RecordAudio", defaultValue: true);
		activity.EnterRecordingPresentation(hideBars, lockOrientation);
		try
		{
			await Task.Delay(250, cancellationToken);
			ProjectionPermissionStore.Set(permission.ResultCode, permission.Data);
			await ProjectionRecorderForegroundService.StartRecorderAsync(outputPath, quality, recordAudio, cancellationToken);
		}
		catch
		{
			activity.ExitRecordingPresentation();
			throw;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			await ProjectionRecorderForegroundService.StopRecorderAsync(cancellationToken);
		}
		finally
		{
			Activity currentActivity = Platform.CurrentActivity;
			if (currentActivity is MainActivity activity)
			{
				activity.ExitRecordingPresentation();
			}
		}
	}
}
