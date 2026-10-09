using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.Arch.Core.Util;
using AndroidX.Camera.Core;
using AndroidX.Camera.Effects;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Core.Util;
using AndroidX.Lifecycle;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Google.Common.Util.Concurrent;
using Java.IO;
using Java.Interop;
using Java.Lang;
using Java.Util.Concurrent;
using Microsoft.Maui.ApplicationModel;

namespace GPSCamRoute.Platforms.Android.Recording;

public sealed class CameraXOverlayRecorderService : ICameraXOverlayRecorderService, IDisposable
{
	private sealed class OverlayDrawFunction(CameraXOverlayRecorderService owner) : Java.Lang.Object(), AndroidX.Arch.Core.Util.IFunction, IJavaObject, IDisposable, IJavaPeerable
	{
		public Java.Lang.Object? Apply(Java.Lang.Object? input)
		{
			if (input is Frame frame)
			{
				return owner.DrawOverlay(frame) ? Java.Lang.Boolean.True : Java.Lang.Boolean.False;
			}
			return Java.Lang.Boolean.True;
		}

	}

	private sealed class GpuEffectErrorConsumer(CameraXOverlayRecorderService owner) : Java.Lang.Object(), IConsumer, IJavaObject, IDisposable, IJavaPeerable
	{
		public void Accept(Java.Lang.Object? error)
		{
			owner.LastError = "GPU Stabilizer V2 FIX27: " + (error?.ToString() ?? "error desconocido");
		}

	}

	private sealed class OverlayErrorConsumer(CameraXOverlayRecorderService owner) : Java.Lang.Object(), IConsumer, IJavaObject, IDisposable, IJavaPeerable
	{
		public void Accept(Java.Lang.Object? error)
		{
			owner.LastError = error?.ToString() ?? "Error desconocido de OverlayEffect.";
		}

	}

	private sealed class RecordingEventConsumer(CameraXOverlayRecorderService owner, TaskCompletionSource finalizeTcs) : Java.Lang.Object(), IConsumer, IJavaObject, IDisposable, IJavaPeerable
	{
		public void Accept(Java.Lang.Object? videoRecordEvent)
		{
			if (!(videoRecordEvent is VideoRecordEvent.Finalize finalize))
			{
				return;
			}
			try
			{
				if (finalize.Error != 0)
				{
					owner.LastError = $"CameraX finalizó con código {finalize.Error}.";
				}
			}
			catch
			{
			}
			finalizeTcs.TrySetResult();
		}

	}

	private const float GpuHudContentScale = 1f;

	private readonly object _sync = new object();

	private readonly ICameraStabilizationService _cameraStabilizationService;

	private readonly RutaCamSoftwareStabilizer _rutaCamSoftwareStabilizer = new RutaCamSoftwareStabilizer();

	private RutaCamGpuMotionTracker? _gpuMotionTracker;

	private RutaCamGpuSurfaceProcessor? _gpuSurfaceProcessor;

	private RutaCamGpuCameraEffect? _gpuCameraEffect;

	private ProcessCameraProvider? _provider;

	private Preview? _preview;

	private Recorder? _recorder;

	private VideoCapture? _videoCapture;

	private AndroidX.Camera.Video.Recording? _recording;

	private ICamera? _camera;

	private ICameraControl? _cameraControl;

	private IExecutorService? _cameraExecutor;

	private HandlerThread? _overlayThread;

	private Handler? _overlayHandler;

	private OverlayEffect? _overlayEffect;

	private TaskCompletionSource? _finalizeTcs;

	private Bitmap? _logoBitmap;

	private Bitmap? _mapBitmap;

	private Bitmap? _gpuHudBitmap;

	private byte[]? _pendingMapSnapshotPng;

	private int _mapSnapshotVersion;

	private int _decodedMapVersion;

	private CameraXMapPoint[] _mapTrackPoints = Array.Empty<CameraXMapPoint>();

	private CameraXMapPoint? _mapCurrentPoint;

	private CameraXHudOptions _hudOptions = new CameraXHudOptions();

	private CameraXHudSnapshot _snapshot = new CameraXHudSnapshot(0.0, 0.0, TimeSpan.Zero, 0.0, 0.0, "GPS --", "COORD --", "ALT --", "BAT --", "ESPACIO --", IsRecording: false);

	private DateTimeOffset _sessionStartedAtUtc;

	private TimeSpan _sessionElapsedOffset;

	private bool _disposed;

	private BluetoothProcessedAudioRecorder? _processedAudioRecorder;

	private bool _usingProcessedBluetoothAudio;

	private string? _requestedOutputPath;

	private string? _cameraTempOutputPath;

	private string? _audioTempOutputPath;

	public bool IsRecording => _recording != null;

	public string? LastError { get; private set; }

	public string? LastStabilizationStatus { get; private set; }

	public CameraXOverlayRecorderService(ICameraStabilizationService cameraStabilizationService)
	{
		_cameraStabilizationService = cameraStabilizationService;
	}

	public void UpdateHud(CameraXHudSnapshot snapshot)
	{
		lock (_sync)
		{
			_snapshot = snapshot;
		}
	}

	public void UpdateMapState(CameraXMapState state)
	{
		if ((object)state == null)
		{
			return;
		}
		lock (_sync)
		{
			byte[] baseMapPng = state.BaseMapPng;
			if (baseMapPng != null && baseMapPng.Length > 0)
			{
				_pendingMapSnapshotPng = state.BaseMapPng;
				_mapSnapshotVersion++;
			}
			_mapTrackPoints = state.TrackPoints ?? Array.Empty<CameraXMapPoint>();
			_mapCurrentPoint = state.CurrentPoint;
		}
	}

	public void SetZoom(float zoomFactor)
	{
		if (zoomFactor <= 0f || _rutaCamSoftwareStabilizer.IsRunning)
		{
			return;
		}
		try
		{
			_cameraControl?.SetZoomRatio(zoomFactor);
		}
		catch
		{
		}
	}

	public void SetTorch(bool enabled)
	{
		try
		{
			_cameraControl?.EnableTorch(enabled);
		}
		catch
		{
		}
	}

	public async Task StartAsync(object previewPlatformView, string outputPath, bool useFrontCamera, bool recordAudio, string audioInputDeviceId, CameraXRecordingOptions recordingOptions, CameraXHudOptions hudOptions, CameraXHudSnapshot initialSnapshot, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("CameraXOverlayRecorderService");
		}
		if (_recording != null)
		{
			throw new InvalidOperationException("CameraX ya está grabando.");
		}
		if (!(previewPlatformView is PreviewView previewView))
		{
			throw new InvalidOperationException("No se pudo obtener el PreviewView nativo de CameraX.");
		}
		LastError = null;
		LastStabilizationStatus = null;
		_hudOptions = hudOptions;
		lock (_sync)
		{
			_pendingMapSnapshotPng = null;
			_mapSnapshotVersion = 0;
			_decodedMapVersion = 0;
			_mapTrackPoints = Array.Empty<CameraXMapPoint>();
			_mapCurrentPoint = null;
		}
		_mapBitmap?.Dispose();
		_mapBitmap = null;
		_gpuHudBitmap?.Dispose();
		_gpuHudBitmap = null;
		_sessionStartedAtUtc = DateTimeOffset.UtcNow;
		_sessionElapsedOffset = initialSnapshot.Elapsed;
		UpdateHud(initialSnapshot);
		try
		{
			global::Android.App.Activity activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No se encontró la actividad Android activa.");
			if (!(activity is ILifecycleOwner lifecycleOwner))
			{
				throw new InvalidOperationException("La actividad Android no expone un LifecycleOwner para CameraX.");
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
			_requestedOutputPath = outputPath;
			_usingProcessedBluetoothAudio = false;
			_cameraTempOutputPath = null;
			_audioTempOutputPath = null;
			bool wantsProcessedBluetoothAudio = recordAudio && BluetoothProcessedAudioRecorder.IsBluetoothSelection(audioInputDeviceId);
			string cameraOutputPath = outputPath;
			bool cameraXRecordAudio = recordAudio;
			if (wantsProcessedBluetoothAudio)
			{
				_cameraTempOutputPath = outputPath + ".video.fix45.tmp.mp4";
				_audioTempOutputPath = outputPath + ".audio.fix45.tmp.m4a";
				TryDelete(_cameraTempOutputPath);
				TryDelete(_audioTempOutputPath);
				cameraOutputPath = _cameraTempOutputPath;
				cameraXRecordAudio = false;
			}
			_cameraExecutor = Executors.NewSingleThreadExecutor() ?? throw new InvalidOperationException("No se pudo crear el ejecutor de CameraX.");
			_overlayThread = new HandlerThread("RutaCam-CameraX-HUD");
			_overlayThread.Start();
			_overlayHandler = new Handler(_overlayThread.Looper ?? throw new InvalidOperationException("No se pudo iniciar el hilo del HUD CameraX."));
			_provider = await GetCameraProviderAsync(activity, cancellationToken);
			_provider.UnbindAll();
			CameraStabilizationCapabilities stabilizationCapabilities = await _cameraStabilizationService.GetCapabilitiesAsync(useFrontCamera, recordingOptions.PhysicalCameraId, cancellationToken);
			CameraStabilizationDecision stabilizationDecision = recordingOptions.ResolveStabilization(useFrontCamera, stabilizationCapabilities);
			LastStabilizationStatus = stabilizationDecision.ShortStatus;
			System.Diagnostics.Debug.WriteLine($"RutaCam estabilización: {stabilizationDecision.ShortStatus} · {stabilizationDecision.Detail} · {stabilizationCapabilities.Summary.Replace(System.Environment.NewLine, " · ")}");
			Preview.Builder previewBuilder = new Preview.Builder();
			if (stabilizationDecision.EnablePreviewStabilization.HasValue)
			{
				previewBuilder.SetPreviewStabilizationEnabled(stabilizationDecision.EnablePreviewStabilization.Value);
			}
			_preview = previewBuilder.Build();
			_preview.SetSurfaceProvider(_cameraExecutor, previewView.SurfaceProvider);
			Recorder.Builder recorderBuilder = new Recorder.Builder().SetExecutor(_cameraExecutor);
			Quality requestedQuality = ResolveRequestedQuality(recordingOptions.EffectiveResolution);
			recorderBuilder = recorderBuilder.SetQualitySelector(QualitySelector.From(requestedQuality));
			_recorder = recorderBuilder.Build();
			VideoCapture.Builder videoCaptureBuilder = new VideoCapture.Builder(_recorder);
			int targetFps = ((recordingOptions.EffectiveTargetFps >= 60) ? 60 : 30);
			global::Android.Util.Range fpsRange = new global::Android.Util.Range(Integer.ValueOf(targetFps), Integer.ValueOf(targetFps));
			videoCaptureBuilder.SetTargetFrameRate(fpsRange);
			if (stabilizationDecision.EnableVideoStabilization.HasValue)
			{
				videoCaptureBuilder.SetVideoStabilizationEnabled(stabilizationDecision.EnableVideoStabilization.Value);
			}
			_videoCapture = (VideoCapture)videoCaptureBuilder.Build();
			int targetRotation = (int)(previewView.Display?.Rotation ?? SurfaceOrientation.Rotation0);
			_preview.TargetRotation = targetRotation;
			_videoCapture.TargetRotation = targetRotation;
			bool gpuEnabled = recordingOptions.IsRutaCamGpuStabilizationEnabled;
			if (gpuEnabled)
			{
				_gpuMotionTracker = new RutaCamGpuMotionTracker();
				if (!_gpuMotionTracker.Start(recordingOptions.RutaCamGpuStabilization, targetRotation, useFrontCamera))
				{
					gpuEnabled = false;
					_gpuMotionTracker.Dispose();
					_gpuMotionTracker = null;
					LastError = "GPU Stabilizer FIX27 no pudo iniciar sensores. Se grabará sin GPU.";
				}
				else
				{
					if (hudOptions.Enabled)
					{
						LoadLogoBitmap(activity, hudOptions);
					}
					_gpuSurfaceProcessor = new RutaCamGpuSurfaceProcessor(_gpuMotionTracker, _cameraExecutor, hudOptions.Enabled ? new Func<int, int, int, Bitmap>(RenderGpuHudBitmap) : null);
					_gpuCameraEffect = new RutaCamGpuCameraEffect(_cameraExecutor, _gpuSurfaceProcessor, new GpuEffectErrorConsumer(this));
					LastStabilizationStatus = "GPU Stabilizer V2 · FIX35 HUD PREVIEW SCALE · " + recordingOptions.RutaCamGpuStabilization;
					System.Diagnostics.Debug.WriteLine("RutaCam GPU V2 FIX35 activo: HUD MP4 con escala lógica equivalente al Preview. Preview CameraX normal + HUD MAUI, sin duplicación visual.");
				}
			}
			if (hudOptions.Enabled && !gpuEnabled)
			{
				_overlayEffect = new OverlayEffect(2, 0, _overlayHandler, new OverlayErrorConsumer(this));
				_overlayEffect.SetOnDrawListener(new OverlayDrawFunction(this));
				LoadLogoBitmap(activity, hudOptions);
			}
			UseCaseGroup.Builder groupBuilder = new UseCaseGroup.Builder();
			groupBuilder.AddUseCase(_preview);
			groupBuilder.AddUseCase(_videoCapture);
			if (_gpuCameraEffect != null)
			{
				groupBuilder.AddEffect(_gpuCameraEffect);
			}
			else if (_overlayEffect != null)
			{
				groupBuilder.AddEffect(_overlayEffect);
			}
			UseCaseGroup useCaseGroup = groupBuilder.Build();
			CameraSelector selector = BuildCameraSelector(useFrontCamera, recordingOptions.PhysicalCameraId);
			try
			{
				_camera = _provider.BindToLifecycle(lifecycleOwner, selector, useCaseGroup);
			}
			catch (System.Exception ex) when (!useFrontCamera && !string.IsNullOrWhiteSpace(recordingOptions.PhysicalCameraId))
			{
				LastError = "La lente física " + recordingOptions.PhysicalCameraId + " no pudo abrirse; se usó la cámara automática. " + ex.Message;
				_provider.UnbindAll();
				_camera = _provider.BindToLifecycle(lifecycleOwner, CameraSelector.DefaultBackCamera, useCaseGroup);
			}
			_cameraControl = _camera.CameraControl;
			string text = ((!recordingOptions.IsRutaCamGpuStabilizationEnabled) ? (await _rutaCamSoftwareStabilizer.StartAsync(_cameraControl, useFrontCamera, recordingOptions.PhysicalCameraId, recordingOptions.RutaCamSoftwareStabilization, targetRotation, cancellationToken)) : "RutaCam Stabilizer V1: omitido por GPU V2");
			string softwareStabilizationStatus = text;
			if (!string.IsNullOrWhiteSpace(softwareStabilizationStatus) && !softwareStabilizationStatus.EndsWith("OFF", StringComparison.OrdinalIgnoreCase))
			{
				LastStabilizationStatus = (string.IsNullOrWhiteSpace(LastStabilizationStatus) ? softwareStabilizationStatus : (LastStabilizationStatus + " · " + softwareStabilizationStatus));
			}
			System.Diagnostics.Debug.WriteLine("RutaCam software stabilization: " + softwareStabilizationStatus);
			if (wantsProcessedBluetoothAudio)
			{
				_processedAudioRecorder = new BluetoothProcessedAudioRecorder();
				if (await _processedAudioRecorder.StartAsync(_audioTempOutputPath, audioInputDeviceId, cancellationToken))
				{
					_usingProcessedBluetoothAudio = true;
					System.Diagnostics.Debug.WriteLine("RutaCam AUDIO FIX45 · Bluetooth procesado activo · -7.5 dB · de-esser dinámico · AAC mono 16 kHz.");
				}
				else
				{
					LastError = "Audio FIX45 no pudo iniciar; se usará audio CameraX normal. " + _processedAudioRecorder.LastError;
					_processedAudioRecorder.Dispose();
					_processedAudioRecorder = null;
					_usingProcessedBluetoothAudio = false;
					cameraXRecordAudio = recordAudio;
					cameraOutputPath = outputPath;
					TryDelete(_cameraTempOutputPath);
					TryDelete(_audioTempOutputPath);
					_cameraTempOutputPath = null;
					_audioTempOutputPath = null;
				}
			}
			Java.IO.File outputFile = new Java.IO.File(cameraOutputPath);
			if (outputFile.Exists())
			{
				outputFile.Delete();
			}
			outputFile.CreateNewFile();
			FileOutputOptions options = new FileOutputOptions.Builder(outputFile).Build();
			PendingRecording pendingRecording = _recorder.PrepareRecording(activity, options) ?? throw new InvalidOperationException("CameraX no pudo preparar la grabación.");
			if (cameraXRecordAudio)
			{
				pendingRecording = pendingRecording.WithAudioEnabled();
			}
			_finalizeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			IExecutor executor = ContextCompat.GetMainExecutor(activity) ?? throw new InvalidOperationException("No se pudo obtener el ejecutor principal de Android.");
			_recording = pendingRecording.Start(executor, new RecordingEventConsumer(this, _finalizeTcs));
		}
		catch
		{
			try
			{
				if (_processedAudioRecorder != null)
				{
					await _processedAudioRecorder.StopAsync(CancellationToken.None);
				}
			}
			catch
			{
			}
			CleanupCameraGraph();
			TryDelete(_cameraTempOutputPath);
			TryDelete(_audioTempOutputPath);
			ResetProcessedAudioState();
			throw;
		}
	}

	private static CameraSelector BuildCameraSelector(bool useFrontCamera, string? physicalCameraId)
	{
		if (useFrontCamera || string.IsNullOrWhiteSpace(physicalCameraId) || !OperatingSystem.IsAndroidVersionAtLeast(28))
		{
			return useFrontCamera ? CameraSelector.DefaultFrontCamera : CameraSelector.DefaultBackCamera;
		}
		CameraSelector.Builder builder = new CameraSelector.Builder();
		builder.RequireLensFacing(1);
		builder.SetPhysicalCameraId(physicalCameraId);
		return builder.Build();
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		AndroidX.Camera.Video.Recording recording = _recording;
		if (recording == null)
		{
			return;
		}
		try
		{
			recording.Stop();
			if (_finalizeTcs != null)
			{
				await _finalizeTcs.Task.WaitAsync(cancellationToken);
			}
			if (!_usingProcessedBluetoothAudio)
			{
				return;
			}
			if (_processedAudioRecorder != null)
			{
				await _processedAudioRecorder.StopAsync(cancellationToken);
			}
			string requested = _requestedOutputPath;
			string videoTemp = _cameraTempOutputPath;
			string audioTemp = _audioTempOutputPath;
			if (!string.IsNullOrWhiteSpace(requested) && !string.IsNullOrWhiteSpace(videoTemp) && System.IO.File.Exists(videoTemp) && !string.IsNullOrWhiteSpace(audioTemp) && System.IO.File.Exists(audioTemp) && new FileInfo(audioTemp).Length > 1024)
			{
				try
				{
					MediaTrackMuxer.MergeVideoAndAudio(videoTemp, audioTemp, requested);
					System.Diagnostics.Debug.WriteLine("RutaCam AUDIO FIX45 · MP4 final remuxeado con audio Bluetooth procesado.");
				}
				catch (System.Exception ex)
				{
					System.Exception ex2 = ex;
					LastError = "Audio FIX45: no se pudo unir audio procesado; se conservará video. " + ex2.Message;
					TryDelete(requested);
					System.IO.File.Move(videoTemp, requested, overwrite: true);
				}
			}
			else if (!string.IsNullOrWhiteSpace(requested) && !string.IsNullOrWhiteSpace(videoTemp) && System.IO.File.Exists(videoTemp))
			{
				LastError = "Audio FIX45 terminó sin un track AAC válido; se guardó el video sin audio.";
				TryDelete(requested);
				System.IO.File.Move(videoTemp, requested, overwrite: true);
			}
			TryDelete(videoTemp);
			TryDelete(audioTemp);
		}
		finally
		{
			CleanupCameraGraph();
			ResetProcessedAudioState();
		}
	}

	private static void TryDelete(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			if (System.IO.File.Exists(path))
			{
				System.IO.File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private void ResetProcessedAudioState()
	{
		try
		{
			_processedAudioRecorder?.Dispose();
		}
		catch
		{
		}
		_processedAudioRecorder = null;
		_usingProcessedBluetoothAudio = false;
		_requestedOutputPath = null;
		_cameraTempOutputPath = null;
		_audioTempOutputPath = null;
	}

	private static Quality ResolveRequestedQuality(string? resolution)
	{
		if (!string.IsNullOrWhiteSpace(resolution))
		{
			if (resolution.StartsWith("4K", StringComparison.OrdinalIgnoreCase))
			{
				return Quality.Uhd;
			}
			if (resolution.StartsWith("720", StringComparison.OrdinalIgnoreCase))
			{
				return Quality.Hd;
			}
		}
		return Quality.Fhd;
	}

	private static async Task<ProcessCameraProvider> GetCameraProviderAsync(Context context, CancellationToken cancellationToken)
	{
		IListenableFuture future = ProcessCameraProvider.GetInstance(context) ?? throw new InvalidOperationException("CameraX no devolvió ProcessCameraProvider.");
		TaskCompletionSource<ProcessCameraProvider> tcs = new TaskCompletionSource<ProcessCameraProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
		IExecutor executor = ContextCompat.GetMainExecutor(context) ?? throw new InvalidOperationException("No se pudo obtener el ejecutor principal de Android.");
		future.AddListener(new Runnable(delegate
		{
			try
			{
				ProcessCameraProvider result = (ProcessCameraProvider)(future.Get() ?? throw new InvalidOperationException("CameraX devolvió un proveedor no válido."));
				tcs.TrySetResult(result);
			}
			catch (System.Exception exception)
			{
				tcs.TrySetException(exception);
			}
		}), executor);
		return await tcs.Task.WaitAsync(cancellationToken);
	}

	private void LoadLogoBitmap(Context context, CameraXHudOptions options)
	{
		_logoBitmap?.Dispose();
		_logoBitmap = null;
		if (!options.ShowLogo)
		{
			return;
		}
		try
		{
			if (!string.IsNullOrWhiteSpace(options.CustomLogoPath) && System.IO.File.Exists(options.CustomLogoPath))
			{
				_logoBitmap = BitmapFactory.DecodeFile(options.CustomLogoPath);
				if (_logoBitmap != null)
				{
					return;
				}
			}
			int resourceId = context.Resources?.GetIdentifier("personal_logo", "drawable", context.PackageName) ?? 0;
			if (resourceId != 0)
			{
				_logoBitmap = BitmapFactory.DecodeResource(context.Resources, resourceId);
			}
		}
		catch
		{
			_logoBitmap = null;
		}
	}

	private bool DrawOverlay(Frame frame)
	{
		return DrawHudCanvas(frame.OverlayCanvas, frame.RotationDegrees);
	}

	private bool DrawHudCanvas(Canvas canvas, int rotationDegrees, float contentScale = 1f, bool matchPreviewGeometry = false)
	{
		try
		{
			CameraXHudSnapshot snapshot;
			CameraXHudOptions options;
			CameraXMapPoint[] mapTrackPoints;
			CameraXMapPoint? mapCurrentPoint;
			lock (_sync)
			{
				snapshot = _snapshot;
				options = _hudOptions;
				mapTrackPoints = _mapTrackPoints;
				mapCurrentPoint = _mapCurrentPoint;
			}
			canvas.DrawColor(Color.Transparent, PorterDuff.Mode.Clear);
			if (!options.Enabled)
			{
				return true;
			}
			int rawWidth = System.Math.Max(1, canvas.Width);
			int rawHeight = System.Math.Max(1, canvas.Height);
			rotationDegrees = NormalizeRightAngle(rotationDegrees);
			bool flag = ((rotationDegrees == 90 || rotationDegrees == 270) ? true : false);
			bool portraitSwap = flag;
			int width = (portraitSwap ? rawHeight : rawWidth);
			int height = (portraitSwap ? rawWidth : rawHeight);
			int canvasSave = canvas.Save();
			ApplyInverseOutputRotation(canvas, rotationDegrees, rawWidth, rawHeight);
			contentScale = System.Math.Clamp(contentScale, 0.6f, 1f);
			if (contentScale < 0.999f)
			{
				float insetX = (float)width * (1f - contentScale) * 0.5f;
				float insetY = (float)height * (1f - contentScale) * 0.5f;
				canvas.Translate(insetX, insetY);
				canvas.Scale(contentScale, contentScale);
			}
			int shortEdge = System.Math.Min(width, height);
			float unit = (float)shortEdge / 1080f;
			float dpScale = (float)shortEdge / 411f;
			float margin = (matchPreviewGeometry ? (18f * dpScale) : (36f * unit));
			float radius = (matchPreviewGeometry ? (8f * dpScale) : (18f * unit));
			using Paint panelPaint = new Paint(PaintFlags.AntiAlias)
			{
				Color = Color.Argb(165, 7, 17, 31)
			};
			using (CreateTextPaint(Color.White, matchPreviewGeometry ? (23f * dpScale) : (46f * unit), bold: true))
			{
				using (CreateTextPaint(Color.Rgb(202, 214, 228), matchPreviewGeometry ? (12f * dpScale) : (23f * unit), bold: false))
				{
					using Paint label = CreateTextPaint(Color.Rgb(174, 190, 209), matchPreviewGeometry ? (8f * dpScale) : (18f * unit), bold: true);
					RectF mapRect = null;
					if (options.ShowMap)
					{
						RefreshMapBitmapIfNeeded();
						float mapSize = (float)(System.Math.Clamp(options.MapWidthDp, 110.0, 220.0) * (double)dpScale);
						mapSize = System.Math.Clamp(mapSize, (float)shortEdge * 0.22f, (float)shortEdge * 0.58f);
						mapRect = ComputeMapRect(options.MapCorner, mapSize, width, height, margin, unit);
						DrawMap(canvas, mapRect, options, unit, mapTrackPoints, mapCurrentPoint);
					}
					RectF logoRect = null;
					if (options.ShowLogo && _logoBitmap != null && !_logoBitmap.IsRecycled)
					{
						float requested = (float)System.Math.Clamp(options.LogoWidthDp, 55.0, 180.0);
						float logoWidth = requested * dpScale;
						logoWidth = System.Math.Clamp(logoWidth, (float)shortEdge * 0.12f, (float)shortEdge * 0.48f);
						float ratio = ((_logoBitmap.Height <= 0) ? 0.68f : ((float)_logoBitmap.Height / (float)_logoBitmap.Width));
						float logoHeight = logoWidth * ratio;
						using Paint logoPaint = new Paint(PaintFlags.AntiAlias)
						{
							Alpha = (int)System.Math.Clamp(options.LogoOpacity * 255.0, 0.0, 255.0)
						};
						float logoTop = margin;
						if (mapRect != null && IsTopLeft(options.MapCorner))
						{
							logoTop = mapRect.Bottom + 16f * unit;
						}
						logoRect = new RectF(margin, logoTop, margin + logoWidth, logoTop + logoHeight);
						canvas.DrawBitmap(_logoBitmap, null, logoRect, logoPaint);
					}
					RectF dateTimeRect = null;
					if (options.ShowDateTime)
					{
						float dateWidth = 360f * unit;
						float dateHeight = 58f * unit;
						dateTimeRect = ComputeCornerRect(options.DateTimeCorner, dateWidth, dateHeight, width, height, margin);
						if (mapRect != null && SameCorner(options.DateTimeCorner, options.MapCorner))
						{
							dateTimeRect = ShiftRectAwayFromObstacle(dateTimeRect, mapRect, options.DateTimeCorner, 16f * unit);
						}
						if (logoRect != null && IsTopLeft(options.DateTimeCorner))
						{
							dateTimeRect = ShiftRectAwayFromObstacle(dateTimeRect, logoRect, options.DateTimeCorner, 16f * unit);
						}
						DrawDateTimeCard(canvas, dateTimeRect, radius, panelPaint, label, unit, DateTimeOffset.Now);
					}
					if (options.ShowRec && snapshot.IsRecording)
					{
						string recText = "REC";
						float recWidth = 130f * unit;
						float recHeight = 58f * unit;
						float left = (float)width - margin - recWidth;
						float top = margin;
						if (mapRect != null && IsTopRight(options.MapCorner))
						{
							left = mapRect.Left - 14f * unit - recWidth;
							if (left < margin)
							{
								left = (float)width - margin - recWidth;
								top = mapRect.Bottom + 14f * unit;
							}
						}
						canvas.DrawRoundRect(new RectF(left, top, left + recWidth, top + recHeight), radius, radius, panelPaint);
						using Paint red = new Paint(PaintFlags.AntiAlias)
						{
							Color = Color.Rgb(251, 75, 85)
						};
						canvas.DrawCircle(left + 25f * unit, top + recHeight / 2f, 8f * unit, red);
						using Paint recPaint = CreateTextPaint(Color.White, 22f * unit, bold: true);
						canvas.DrawText(recText, left + 44f * unit, top + 38f * unit, recPaint);
					}
					float bottom = (float)height - (matchPreviewGeometry ? (36f * dpScale) : (80f * unit));
					float x = margin;
					bool hasAdvancedHud = options.ShowAverageSpeed || options.ShowMaxSpeed || options.ShowGpsStatus || options.ShowCoordinates || options.ShowAltitude || options.ShowBatteryStatus || options.ShowStorageStatus;
					if (dateTimeRect != null && IsBottomLeft(options.DateTimeCorner))
					{
						bottom = System.Math.Min(bottom, dateTimeRect.Top - 18f * unit);
					}
					if (mapRect != null && IsBottomLeft(options.MapCorner))
					{
						float shiftedX = mapRect.Right + 18f * unit;
						float neededWidth = 484f * unit;
						if (hasAdvancedHud && shiftedX + neededWidth <= (float)width - margin)
						{
							x = shiftedX;
						}
						else
						{
							bottom = System.Math.Min(bottom, mapRect.Top - 18f * unit);
						}
					}
					float cardWidth = (matchPreviewGeometry ? (96f * dpScale) : (235f * unit));
					float cardGap = (matchPreviewGeometry ? (7f * dpScale) : (14f * unit));
					float pairWidth = cardWidth * 2f + cardGap;
					float cardHeight = (matchPreviewGeometry ? (50f * dpScale) : (92f * unit));
					if (options.ShowDistance || options.ShowTimer)
					{
						float top2 = bottom - cardHeight;
						float cursor = x;
						if (options.ShowDistance)
						{
							DrawMetricCard(canvas, cursor, top2, cardWidth, cardHeight, radius, panelPaint, label, "DISTANCIA", $"{snapshot.DistanceKm:F2} km", matchPreviewGeometry ? (16f * dpScale) : (31f * unit));
							cursor += cardWidth + cardGap;
						}
						if (options.ShowTimer)
						{
							TimeSpan liveElapsed = ((snapshot.IsRecording && _sessionStartedAtUtc != default(DateTimeOffset)) ? (_sessionElapsedOffset + (DateTimeOffset.UtcNow - _sessionStartedAtUtc)) : snapshot.Elapsed);
							DrawMetricCard(canvas, cursor, top2, cardWidth, cardHeight, radius, panelPaint, label, "TIEMPO", FormatDuration(liveElapsed), matchPreviewGeometry ? (16f * dpScale) : (31f * unit));
						}
						bottom = top2 - 14f * unit;
					}
					if (options.ShowAverageSpeed || options.ShowMaxSpeed)
					{
						float top3 = bottom - cardHeight;
						float cursor2 = x;
						if (options.ShowAverageSpeed)
						{
							DrawMetricCard(canvas, cursor2, top3, cardWidth, cardHeight, radius, panelPaint, label, "PROMEDIO", $"{System.Math.Max(0.0, snapshot.AverageSpeedKmh):F0} km/h", matchPreviewGeometry ? (14f * dpScale) : (28f * unit));
							cursor2 += cardWidth + cardGap;
						}
						if (options.ShowMaxSpeed)
						{
							DrawMetricCard(canvas, cursor2, top3, cardWidth, cardHeight, radius, panelPaint, label, "MÁXIMA", $"{System.Math.Max(0.0, snapshot.MaxSpeedKmh):F0} km/h", matchPreviewGeometry ? (14f * dpScale) : (28f * unit));
						}
						bottom = top3 - 14f * unit;
					}
					if (options.ShowGpsStatus || options.ShowAltitude)
					{
						float top4 = bottom - cardHeight;
						float cursor3 = x;
						if (options.ShowGpsStatus)
						{
							string gpsValue = (snapshot.GpsStatusText.StartsWith("GPS ", StringComparison.OrdinalIgnoreCase) ? snapshot.GpsStatusText.Substring(4) : snapshot.GpsStatusText);
							DrawMetricCard(canvas, cursor3, top4, cardWidth, cardHeight, radius, panelPaint, label, "GPS", gpsValue, matchPreviewGeometry ? (13f * dpScale) : (26f * unit));
							cursor3 += cardWidth + cardGap;
						}
						if (options.ShowAltitude)
						{
							string altitudeValue = (snapshot.AltitudeText.StartsWith("ALT ", StringComparison.OrdinalIgnoreCase) ? snapshot.AltitudeText.Substring(4) : snapshot.AltitudeText);
							DrawMetricCard(canvas, cursor3, top4, cardWidth, cardHeight, radius, panelPaint, label, "ALTITUD", altitudeValue, matchPreviewGeometry ? (13f * dpScale) : (26f * unit));
						}
						bottom = top4 - 14f * unit;
					}
					if (options.ShowCoordinates)
					{
						float compactHeight = (matchPreviewGeometry ? (42f * dpScale) : (78f * unit));
						float top5 = bottom - compactHeight;
						DrawMetricCard(canvas, x, top5, pairWidth, compactHeight, radius, panelPaint, label, "COORDENADAS", snapshot.CoordinatesText, matchPreviewGeometry ? (11f * dpScale) : (23f * unit));
						bottom = top5 - 14f * unit;
					}
					if (options.ShowBatteryStatus || options.ShowStorageStatus)
					{
						float top6 = bottom - cardHeight;
						float cursor4 = x;
						if (options.ShowBatteryStatus)
						{
							string batteryValue = (snapshot.BatteryText.StartsWith("BAT ", StringComparison.OrdinalIgnoreCase) ? snapshot.BatteryText.Substring(4) : snapshot.BatteryText);
							DrawMetricCard(canvas, cursor4, top6, cardWidth, cardHeight, radius, panelPaint, label, "BATERÍA", batteryValue, matchPreviewGeometry ? (13f * dpScale) : (26f * unit));
							cursor4 += cardWidth + cardGap;
						}
						if (options.ShowStorageStatus)
						{
							string storageValue = (snapshot.StorageText.StartsWith("LIBRE ", StringComparison.OrdinalIgnoreCase) ? snapshot.StorageText.Substring(6) : snapshot.StorageText);
							DrawMetricCard(canvas, cursor4, top6, cardWidth, cardHeight, radius, panelPaint, label, "ESPACIO", storageValue, matchPreviewGeometry ? (12f * dpScale) : (24f * unit));
						}
						bottom = top6 - 14f * unit;
					}
					if (options.ShowSpeed)
					{
						string speedText = System.Math.Max(0.0, snapshot.SpeedKmh).ToString("F0", CultureInfo.InvariantCulture);
						float speedBoxWidth = (matchPreviewGeometry ? (145f * dpScale) : (360f * unit));
						float speedBoxHeight = (matchPreviewGeometry ? (78f * dpScale) : (154f * unit));
						float top7 = bottom - speedBoxHeight;
						canvas.DrawRoundRect(new RectF(x, top7, x + speedBoxWidth, bottom), radius, radius, panelPaint);
						using Paint speedPaint = CreateTextPaint(Color.White, matchPreviewGeometry ? (60f * dpScale) : (98f * unit), bold: true);
						using Paint speedUnitPaint = CreateTextPaint(Color.Rgb(202, 214, 228), matchPreviewGeometry ? (17f * dpScale) : (28f * unit), bold: false);
						float valueX = x + (matchPreviewGeometry ? (14f * dpScale) : (20f * unit));
						float baseline = top7 + (matchPreviewGeometry ? (61f * dpScale) : (112f * unit));
						canvas.DrawText(speedText, valueX, baseline, speedPaint);
						float unitX = valueX + speedPaint.MeasureText(speedText) + (matchPreviewGeometry ? (9f * dpScale) : (14f * unit));
						canvas.DrawText("km/h", unitX, baseline - (matchPreviewGeometry ? (4f * dpScale) : (5f * unit)), speedUnitPaint);
					}
					canvas.RestoreToCount(canvasSave);
					return true;
				}
			}
		}
		catch (System.Exception ex)
		{
			LastError = ex.Message;
			return true;
		}
	}

	private Bitmap? RenderGpuHudBitmap(int outputWidth, int outputHeight, int rotationDegrees)
	{
		CameraXHudOptions options;
		lock (_sync)
		{
			options = _hudOptions;
		}
		if (!options.Enabled || outputWidth <= 0 || outputHeight <= 0)
		{
			return null;
		}
		try
		{
			int width = System.Math.Max(1, outputWidth / 2);
			int height = System.Math.Max(1, outputHeight / 2);
			if (_gpuHudBitmap == null || _gpuHudBitmap.IsRecycled || _gpuHudBitmap.Width != width || _gpuHudBitmap.Height != height)
			{
				_gpuHudBitmap?.Dispose();
				_gpuHudBitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888);
			}
			using Canvas canvas = new Canvas(_gpuHudBitmap);
			DrawHudCanvas(canvas, 0, 1f, matchPreviewGeometry: true);
			return _gpuHudBitmap;
		}
		catch (System.Exception ex)
		{
			LastError = "HUD GPU: " + ex.Message;
			return null;
		}
	}

	private static RectF ComputeCornerRect(string? corner, float width, float height, int canvasWidth, int canvasHeight, float margin)
	{
		string normalized = corner ?? "Inferior derecha";
		float left = (normalized.Contains("izquierda", StringComparison.OrdinalIgnoreCase) ? margin : ((float)canvasWidth - margin - width));
		float top = (normalized.Contains("Inferior", StringComparison.OrdinalIgnoreCase) ? ((float)canvasHeight - margin - height) : margin);
		return new RectF(left, top, left + width, top + height);
	}

	private static RectF ShiftRectAwayFromObstacle(RectF rect, RectF obstacle, string? corner, float gap)
	{
		if ((corner ?? string.Empty).Contains("Inferior", StringComparison.OrdinalIgnoreCase))
		{
			float bottom = obstacle.Top - gap;
			return new RectF(rect.Left, bottom - rect.Height(), rect.Right, bottom);
		}
		float top = obstacle.Bottom + gap;
		return new RectF(rect.Left, top, rect.Right, top + rect.Height());
	}

	private static bool SameCorner(string? first, string? second)
	{
		return string.Equals((first ?? string.Empty).Trim(), (second ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static void DrawDateTimeCard(Canvas canvas, RectF rect, float radius, Paint panelPaint, Paint labelPaint, float unit, DateTimeOffset current)
	{
		canvas.DrawRoundRect(rect, radius, radius, panelPaint);
		using Paint valuePaint = CreateTextPaint(Color.White, 26f * unit, bold: true);
		canvas.DrawText(current.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), rect.Left + 14f * unit, rect.Bottom - 16f * unit, valuePaint);
	}

	private static void DrawMetricCard(Canvas canvas, float left, float top, float width, float height, float radius, Paint panelPaint, Paint labelPaint, string title, string value, float valueTextSize)
	{
		RectF rect = new RectF(left, top, left + width, top + height);
		canvas.DrawRoundRect(rect, radius, radius, panelPaint);
		canvas.DrawText(title, left + 14f * (height / 92f), top + 28f * (height / 92f), labelPaint);
		using Paint valuePaint = CreateTextPaint(Color.White, valueTextSize, bold: true);
		canvas.DrawText(value ?? string.Empty, left + 14f * (height / 92f), top + height - 20f * (height / 92f), valuePaint);
	}

	private void RefreshMapBitmapIfNeeded()
	{
		byte[] png = null;
		int version;
		lock (_sync)
		{
			version = _mapSnapshotVersion;
			if (version == _decodedMapVersion || _pendingMapSnapshotPng == null)
			{
				return;
			}
			png = _pendingMapSnapshotPng;
		}
		try
		{
			Bitmap decoded = BitmapFactory.DecodeByteArray(png, 0, png.Length);
			if (decoded != null)
			{
				_mapBitmap?.Dispose();
				_mapBitmap = decoded;
				_decodedMapVersion = version;
			}
		}
		catch
		{
		}
	}

	private static RectF ComputeMapRect(string? corner, float size, int width, int height, float margin, float unit)
	{
		string normalized = corner ?? "Superior derecha";
		float left = (normalized.Contains("izquierda", StringComparison.OrdinalIgnoreCase) ? margin : ((float)width - margin - size));
		float top = (normalized.Contains("Inferior", StringComparison.OrdinalIgnoreCase) ? ((float)height - margin - size) : margin);
		if (normalized.Contains("Inferior", StringComparison.OrdinalIgnoreCase))
		{
			top -= 18f * unit;
		}
		return new RectF(left, top, left + size, top + size);
	}

	private void DrawMap(Canvas canvas, RectF rect, CameraXHudOptions options, float unit, CameraXMapPoint[] trackPoints, CameraXMapPoint? currentPoint)
	{
		int alpha = (int)System.Math.Clamp(options.MapOpacity * 255.0, 0.0, 255.0);
		bool circular = options.MapShape.Contains("Circular", StringComparison.OrdinalIgnoreCase);
		using Paint fallbackPaint = new Paint(PaintFlags.AntiAlias)
		{
			Color = Color.Argb(System.Math.Max(90, alpha), 7, 17, 31)
		};
		int save = canvas.Save();
		if (circular)
		{
			using global::Android.Graphics.Path clip = new global::Android.Graphics.Path();
			clip.AddCircle(rect.CenterX(), rect.CenterY(), rect.Width() / 2f, global::Android.Graphics.Path.Direction.Cw);
			canvas.ClipPath(clip);
		}
		else
		{
			canvas.ClipRect(rect);
		}
		if (_mapBitmap != null && !_mapBitmap.IsRecycled)
		{
			using Paint mapPaint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap)
			{
				Alpha = alpha
			};
			canvas.DrawBitmap(_mapBitmap, null, rect, mapPaint);
		}
		else
		{
			canvas.DrawRect(rect, fallbackPaint);
			using Paint waiting = CreateTextPaint(Color.Rgb(202, 214, 228), 18f * unit, bold: true);
			waiting.TextAlign = Paint.Align.Center;
			canvas.DrawText("MAPA · GPS", rect.CenterX(), rect.CenterY(), waiting);
		}
		if (trackPoints.Length >= 2)
		{
			using global::Android.Graphics.Path routePath = new global::Android.Graphics.Path();
			bool first = true;
			for (int i = 0; i < trackPoints.Length; i++)
			{
				CameraXMapPoint point = trackPoints[i];
				float px = rect.Left + (float)(point.X * (double)rect.Width());
				float py = rect.Top + (float)(point.Y * (double)rect.Height());
				if (first)
				{
					routePath.MoveTo(px, py);
					first = false;
				}
				else
				{
					routePath.LineTo(px, py);
				}
			}
			using Paint routePaint = new Paint(PaintFlags.AntiAlias)
			{
				Color = Color.Rgb(34, 197, 94),
				StrokeWidth = System.Math.Max(2.4f * unit, 1.8f)
			};
			routePaint.SetStyle(Paint.Style.Stroke);
			routePaint.StrokeCap = Paint.Cap.Round;
			routePaint.StrokeJoin = Paint.Join.Round;
			canvas.DrawPath(routePath, routePaint);
		}
		if (currentPoint.HasValue)
		{
			CameraXMapPoint current = currentPoint.GetValueOrDefault();
			if (true)
			{
				float px2 = rect.Left + (float)(current.X * (double)rect.Width());
				float py2 = rect.Top + (float)(current.Y * (double)rect.Height());
				using Paint marker = new Paint(PaintFlags.AntiAlias)
				{
					Color = Color.Rgb(34, 197, 94)
				};
				canvas.DrawCircle(px2, py2, System.Math.Max(4.8f * unit, 3.5f), marker);
			}
		}
		canvas.RestoreToCount(save);
		using Paint border = new Paint(PaintFlags.AntiAlias)
		{
			Color = Color.Argb(180, 255, 255, 255),
			StrokeWidth = System.Math.Max(1.5f * unit, 2f)
		};
		border.SetStyle(Paint.Style.Stroke);
		if (circular)
		{
			canvas.DrawCircle(rect.CenterX(), rect.CenterY(), rect.Width() / 2f, border);
		}
		else
		{
			canvas.DrawRoundRect(rect, 18f * unit, 18f * unit, border);
		}
		float attribWidth = System.Math.Min(rect.Width() * 0.78f, 154f * unit);
		float attribHeight = 28f * unit;
		float attribLeft = rect.CenterX() - attribWidth / 2f;
		float attribTop = rect.Bottom - attribHeight - 12f * unit;
		using Paint attribBg = new Paint(PaintFlags.AntiAlias)
		{
			Color = Color.Argb(155, 7, 17, 31)
		};
		canvas.DrawRoundRect(new RectF(attribLeft, attribTop, attribLeft + attribWidth, attribTop + attribHeight), 8f * unit, 8f * unit, attribBg);
		using Paint attrib = CreateTextPaint(Color.White, 12f * unit, bold: false);
		attrib.TextAlign = Paint.Align.Center;
		canvas.DrawText("© OpenStreetMap", rect.CenterX(), attribTop + 19f * unit, attrib);
	}

	private static bool IsTopRight(string? corner)
	{
		return (corner ?? string.Empty).Contains("Superior", StringComparison.OrdinalIgnoreCase) && (corner ?? string.Empty).Contains("derecha", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTopLeft(string? corner)
	{
		return (corner ?? string.Empty).Contains("Superior", StringComparison.OrdinalIgnoreCase) && (corner ?? string.Empty).Contains("izquierda", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsBottomLeft(string? corner)
	{
		return (corner ?? string.Empty).Contains("Inferior", StringComparison.OrdinalIgnoreCase) && (corner ?? string.Empty).Contains("izquierda", StringComparison.OrdinalIgnoreCase);
	}

	private static void ApplyInverseOutputRotation(Canvas canvas, int rotationDegrees, int rawWidth, int rawHeight)
	{
		switch (rotationDegrees)
		{
		case 90:
			canvas.Translate(0f, rawHeight);
			canvas.Rotate(-90f);
			break;
		case 180:
			canvas.Translate(rawWidth, rawHeight);
			canvas.Rotate(180f);
			break;
		case 270:
			canvas.Translate(rawWidth, 0f);
			canvas.Rotate(90f);
			break;
		}
	}

	private static int NormalizeRightAngle(int degrees)
	{
		int normalized = (degrees % 360 + 360) % 360;
		if (1 == 0)
		{
		}
		int result = ((normalized < 225) ? ((normalized >= 45) ? ((normalized >= 135) ? 180 : 90) : 0) : ((normalized < 315) ? 270 : 0));
		if (1 == 0)
		{
		}
		return result;
	}

	private static Paint CreateTextPaint(Color color, float size, bool bold)
	{
		Paint paint = new Paint(PaintFlags.AntiAlias)
		{
			Color = color,
			TextSize = size
		};
		Typeface typeface = Typeface.Create(Typeface.Default, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
		if (typeface != null)
		{
			paint.SetTypeface(typeface);
		}
		paint.SetShadowLayer(3f, 0f, 1f, Color.Black);
		return paint;
	}

	private static string FormatDuration(TimeSpan elapsed)
	{
		if (!(elapsed.TotalHours >= 1.0))
		{
			return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
		}
		return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
	}

	private void CleanupCameraGraph()
	{
		try
		{
			_rutaCamSoftwareStabilizer.Stop();
		}
		catch
		{
		}
		RutaCamGpuSurfaceProcessor gpuProcessor = _gpuSurfaceProcessor;
		RutaCamGpuMotionTracker gpuMotion = _gpuMotionTracker;
		_gpuSurfaceProcessor = null;
		_gpuMotionTracker = null;
		_gpuCameraEffect = null;
		try
		{
			_provider?.UnbindAll();
		}
		catch
		{
		}
		if (gpuProcessor != null || gpuMotion != null)
		{
			try
			{
				if (_cameraExecutor != null)
				{
					_cameraExecutor.Execute(new Runnable(delegate
					{
						try
						{
							gpuProcessor?.Dispose();
						}
						catch
						{
						}
						try
						{
							gpuMotion?.Dispose();
						}
						catch
						{
						}
					}));
				}
				else
				{
					try
					{
						gpuProcessor?.Dispose();
					}
					catch
					{
					}
					try
					{
						gpuMotion?.Dispose();
					}
					catch
					{
					}
				}
			}
			catch
			{
				try
				{
					gpuProcessor?.Dispose();
				}
				catch
				{
				}
				try
				{
					gpuMotion?.Dispose();
				}
				catch
				{
				}
			}
		}
		_recording?.Dispose();
		_recording = null;
		_finalizeTcs = null;
		_cameraControl?.Dispose();
		_cameraControl = null;
		_camera?.Dispose();
		_camera = null;
		_videoCapture?.Dispose();
		_videoCapture = null;
		_recorder?.Dispose();
		_recorder = null;
		_preview?.Dispose();
		_preview = null;
		if (_overlayEffect != null)
		{
			try
			{
				_overlayEffect.ClearOnDrawListener();
			}
			catch
			{
			}
			try
			{
				_overlayEffect.Close();
			}
			catch
			{
			}
			_overlayEffect.Dispose();
			_overlayEffect = null;
		}
		_logoBitmap?.Dispose();
		_logoBitmap = null;
		_mapBitmap?.Dispose();
		_mapBitmap = null;
		_gpuHudBitmap?.Dispose();
		_gpuHudBitmap = null;
		lock (_sync)
		{
			_pendingMapSnapshotPng = null;
			_mapSnapshotVersion = 0;
			_decodedMapVersion = 0;
			_mapTrackPoints = Array.Empty<CameraXMapPoint>();
			_mapCurrentPoint = null;
		}
		_sessionStartedAtUtc = default(DateTimeOffset);
		_sessionElapsedOffset = TimeSpan.Zero;
		_overlayHandler?.Dispose();
		_overlayHandler = null;
		if (_overlayThread != null)
		{
			try
			{
				_overlayThread.QuitSafely();
			}
			catch
			{
			}
			_overlayThread.Dispose();
			_overlayThread = null;
		}
		if (_cameraExecutor != null)
		{
			try
			{
				_cameraExecutor.Shutdown();
				_cameraExecutor.AwaitTermination(1500L, TimeUnit.Milliseconds);
			}
			catch
			{
			}
			_cameraExecutor.Dispose();
			_cameraExecutor = null;
		}
		_provider?.Dispose();
		_provider = null;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			CleanupCameraGraph();
			_rutaCamSoftwareStabilizer.Dispose();
		}
	}
}
