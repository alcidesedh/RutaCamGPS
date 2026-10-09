using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Util;
using Android.Views;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Platforms.Android;

[Service(Name = "com.elvismao.rutacamgps.ProjectionRecorderForegroundService", Exported = false, ForegroundServiceType = (ForegroundService.TypeMediaProjection | ForegroundService.TypeMicrophone))]
public sealed class ProjectionRecorderForegroundService : Service
{
	private sealed class ProjectionCallback(ProjectionRecorderForegroundService owner) : MediaProjection.Callback()
	{
		private readonly ProjectionRecorderForegroundService _owner = owner;

		public override void OnStop()
		{
			owner.HandleProjectionStopped();
		}
	}

	private const int NotificationId = 7301;

	private const string ChannelId = "rutacam_recording_v2";

	private const string ActionStart = "com.elvismao.rutacamgps.START_PROJECTION_RECORDING";

	private const string ActionStop = "com.elvismao.rutacamgps.STOP_PROJECTION_RECORDING";

	private const string ExtraOutputPath = "output_path";

	private const string ExtraVideoQuality = "video_quality";

	private const string ExtraRecordAudio = "record_audio";

	private static TaskCompletionSource<bool>? _startCompletion;

	private static TaskCompletionSource<bool>? _stopCompletion;

	private MediaRecorder? _recorder;

	private MediaProjection? _projection;

	private VirtualDisplay? _virtualDisplay;

	private ProjectionCallback? _projectionCallback;

	private bool _stopping;

	public static bool IsRecording { get; private set; }

	public override IBinder? OnBind(Intent? intent)
	{
		return null;
	}

	public static async Task StartRecorderAsync(string outputPath, string videoQuality, bool recordAudio, CancellationToken cancellationToken)
	{
		if (IsRecording)
		{
			return;
		}
		_startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Context context = global::Android.App.Application.Context;
		Intent intent = new Intent(context, typeof(ProjectionRecorderForegroundService));
		intent.SetAction("com.elvismao.rutacamgps.START_PROJECTION_RECORDING");
		intent.PutExtra("output_path", outputPath);
		intent.PutExtra("video_quality", videoQuality);
		intent.PutExtra("record_audio", recordAudio);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			context.StartForegroundService(intent);
		}
		else
		{
			context.StartService(intent);
		}
		using (cancellationToken.Register(delegate
		{
			_startCompletion.TrySetCanceled(cancellationToken);
		}))
		{
			await _startCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15L), cancellationToken);
		}
	}

	public static async Task StopRecorderAsync(CancellationToken cancellationToken)
	{
		if (!IsRecording)
		{
			return;
		}
		_stopCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Context context = global::Android.App.Application.Context;
		Intent intent = new Intent(context, typeof(ProjectionRecorderForegroundService));
		intent.SetAction("com.elvismao.rutacamgps.STOP_PROJECTION_RECORDING");
		context.StartService(intent);
		using (cancellationToken.Register(delegate
		{
			_stopCompletion.TrySetCanceled(cancellationToken);
		}))
		{
			await _stopCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10L), cancellationToken);
		}
	}

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		string action = intent?.Action;
		if (action == "com.elvismao.rutacamgps.STOP_PROJECTION_RECORDING")
		{
			StopInternal();
			return StartCommandResult.NotSticky;
		}
		if (action != "com.elvismao.rutacamgps.START_PROJECTION_RECORDING")
		{
			return StartCommandResult.NotSticky;
		}
		try
		{
			string outputPath = intent?.GetStringExtra("output_path");
			if (string.IsNullOrWhiteSpace(outputPath))
			{
				throw new InvalidOperationException("No se recibió la ruta de salida para el video.");
			}
			bool recordAudioForForeground = intent?.GetBooleanExtra("record_audio", defaultValue: true) ?? true;
			StartAsForeground(recordAudioForForeground);
			string videoQuality = intent?.GetStringExtra("video_quality") ?? "Alta · hasta 1080×1920";
			bool recordAudio = intent?.GetBooleanExtra("record_audio", defaultValue: true) ?? true;
			StartProjectionRecording(outputPath, videoQuality, recordAudio);
			IsRecording = true;
			_startCompletion?.TrySetResult(result: true);
		}
		catch (Exception ex)
		{
			IsRecording = false;
			TryWriteDiagnostic(ex);
			_startCompletion?.TrySetException(ex);
			StopInternal(stopRecorder: false);
		}
		return StartCommandResult.NotSticky;
	}

	private void StartAsForeground(bool recordAudio)
	{
		CreateNotificationChannel();
		Notification notification = BuildNotification();
		ForegroundService types = (recordAudio ? (ForegroundService.TypeMediaProjection | ForegroundService.TypeMicrophone) : ForegroundService.TypeMediaProjection);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
		{
			StartForeground(7301, notification, types);
		}
		else
		{
			StartForeground(7301, notification);
		}
	}

	private Notification BuildNotification()
	{
		Intent launchIntent = new Intent(this, typeof(MainActivity));
		launchIntent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
		PendingIntentFlags pendingFlags = PendingIntentFlags.UpdateCurrent;
		if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
		{
			pendingFlags |= PendingIntentFlags.Immutable;
		}
		PendingIntent pendingIntent = PendingIntent.GetActivity(this, 0, launchIntent, pendingFlags);
		int icon = Resources?.GetIdentifier("ic_stat_rutacam", "drawable", PackageName) ?? 0;
		if (icon == 0)
		{
			throw new InvalidOperationException("No se encontro el icono Android ic_stat_rutacam para la notificacion de grabacion.");
		}
		Notification.Builder builder = ((Build.VERSION.SdkInt >= BuildVersionCodes.O) ? new Notification.Builder(this, "rutacam_recording_v2") : new Notification.Builder(this));
		builder.SetSmallIcon(icon).SetContentTitle("RutaCam GPS").SetContentText("Grabando camara y telemetria")
			.SetOngoing(ongoing: true)
			.SetOnlyAlertOnce(onlyAlertOnce: true)
			.SetShowWhen(show: false)
			.SetCategory("service")
			.SetVisibility(NotificationVisibility.Public)
			.SetContentIntent(pendingIntent);
		if (Build.VERSION.SdkInt < BuildVersionCodes.O)
		{
			builder.SetPriority(-1);
		}
		return builder.Build();
	}

	private void CreateNotificationChannel()
	{
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			NotificationManager manager = (NotificationManager)GetSystemService("notification");
			if (manager?.GetNotificationChannel("rutacam_recording_v2") == null)
			{
				NotificationChannel channel = new NotificationChannel("rutacam_recording_v2", "Grabación RutaCam", NotificationImportance.Low)
				{
					Description = "Mantiene activa la grabación del recorrido."
				};
				manager?.CreateNotificationChannel(channel);
			}
		}
	}

	private void StartProjectionRecording(string outputPath, string videoQuality, bool recordAudio)
	{
		(int, Intent) permission = ProjectionPermissionStore.Take() ?? throw new InvalidOperationException("No se encontró el permiso de captura de pantalla.");
		string outputDirectory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}
		DisplayMetrics metrics = Resources?.DisplayMetrics ?? throw new InvalidOperationException("No fue posible leer las métricas de pantalla.");
		int nativeWidth = Math.Max(2, metrics.WidthPixels);
		int nativeHeight = Math.Max(2, metrics.HeightPixels);
		bool isLightQuality = videoQuality.StartsWith("Ligera", StringComparison.OrdinalIgnoreCase);
		int maxShortSide = (isLightQuality ? 720 : 1080);
		int maxLongSide = (isLightQuality ? 1280 : 1920);
		int videoBitRate = (isLightQuality ? 6000000 : 12000000);
		int shortSide = Math.Min(nativeWidth, nativeHeight);
		int longSide = Math.Max(nativeWidth, nativeHeight);
		double scaleShort = ((shortSide > maxShortSide) ? ((double)maxShortSide / (double)shortSide) : 1.0);
		double scaleLong = ((longSide > maxLongSide) ? ((double)maxLongSide / (double)longSide) : 1.0);
		double scale = Math.Min(scaleShort, scaleLong);
		int width = MakeEven((int)Math.Round((double)nativeWidth * scale));
		int height = MakeEven((int)Math.Round((double)nativeHeight * scale));
		int density = Math.Max(1, (int)metrics.DensityDpi);
		_recorder = ((Build.VERSION.SdkInt >= BuildVersionCodes.S) ? new MediaRecorder(this) : new MediaRecorder());
		if (recordAudio)
		{
			_recorder.SetAudioSource(AudioSource.Mic);
		}
		_recorder.SetVideoSource(VideoSource.Surface);
		_recorder.SetOutputFormat(OutputFormat.Mpeg4);
		_recorder.SetOutputFile(outputPath);
		_recorder.SetVideoEncoder(VideoEncoder.H264);
		if (recordAudio)
		{
			_recorder.SetAudioEncoder(AudioEncoder.Aac);
		}
		_recorder.SetVideoSize(width, height);
		_recorder.SetVideoFrameRate(30);
		_recorder.SetVideoEncodingBitRate(videoBitRate);
		if (recordAudio)
		{
			_recorder.SetAudioEncodingBitRate(128000);
			_recorder.SetAudioSamplingRate(44100);
		}
		_recorder.Prepare();
		MediaProjectionManager projectionManager = ((MediaProjectionManager)GetSystemService("media_projection")) ?? throw new InvalidOperationException("MediaProjection no está disponible en este dispositivo.");
		_projection = projectionManager.GetMediaProjection(permission.Item1, permission.Item2) ?? throw new InvalidOperationException("Android no concedió la sesión de captura.");
		_projectionCallback = new ProjectionCallback(this);
		_projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper));
		DisplayFlags autoMirror = DisplayFlags.Round;
		_virtualDisplay = _projection.CreateVirtualDisplay("RutaCamGPS", width, height, density, autoMirror, _recorder.Surface, null, null);
		if (_virtualDisplay == null)
		{
			throw new InvalidOperationException("Android no pudo crear la pantalla virtual de grabación.");
		}
		_recorder.Start();
	}

	internal void HandleProjectionStopped()
	{
		if (!_stopping)
		{
			StopInternal();
		}
	}

	private void StopInternal(bool stopRecorder = true)
	{
		if (_stopping)
		{
			return;
		}
		_stopping = true;
		try
		{
			if (stopRecorder && _recorder != null)
			{
				try
				{
					_recorder.Stop();
				}
				catch
				{
				}
			}
			try
			{
				_recorder?.Reset();
			}
			catch
			{
			}
			try
			{
				_recorder?.Release();
			}
			catch
			{
			}
			_recorder?.Dispose();
			_recorder = null;
			try
			{
				_virtualDisplay?.Release();
			}
			catch
			{
			}
			_virtualDisplay?.Dispose();
			_virtualDisplay = null;
			if (_projection != null && _projectionCallback != null)
			{
				try
				{
					_projection.UnregisterCallback(_projectionCallback);
				}
				catch
				{
				}
			}
			try
			{
				_projection?.Stop();
			}
			catch
			{
			}
			_projection?.Dispose();
			_projection = null;
			_projectionCallback?.Dispose();
			_projectionCallback = null;
			IsRecording = false;
			_stopCompletion?.TrySetResult(result: true);
		}
		finally
		{
			StopForeground(StopForegroundFlags.Remove);
			StopSelf();
			_stopping = false;
		}
	}

	public override void OnDestroy()
	{
		if (IsRecording || _recorder != null || _projection != null)
		{
			StopInternal();
		}
		base.OnDestroy();
	}

	private static void TryWriteDiagnostic(Exception ex)
	{
		try
		{
			string path = Path.Combine(FileSystem.AppDataDirectory, "rutacam_recorder_error.txt");
			File.WriteAllText(path, $"{DateTimeOffset.Now:O}\n{ex}");
		}
		catch
		{
		}
	}

	private static int MakeEven(int value)
	{
		return Math.Max(2, value - value % 2);
	}
}
