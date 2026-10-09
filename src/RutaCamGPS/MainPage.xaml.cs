using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using AndroidX.Camera.View;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Mapsui.UI.Maui;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace GPSCamRoute;

public partial class MainPage : ContentPage
{
	private enum GpsMotionState
	{
		Unknown,
		Stationary,
		Moving
	}

	private readonly record struct SpeedEstimate(double FinalSpeedKmh, double? ReportedSpeedKmh, double? DisplacementSpeedKmh, bool IsStationary);

	private readonly record struct GpsFilterConfig(double MaxAccuracyMeters, double MaxWeakAccuracyMeters, int InitialFixes, double MinimumMovementMeters, double MaximumNoiseMeters, double AccuracyNoiseFactor, double MaxVehicleSpeedKmh, TimeSpan MaximumAge, TimeSpan SignalHold, TimeSpan SignalTimeout, TimeSpan ResetAfter, TimeSpan StationaryConfirmation, double MaxAccelerationKmhPerSecond, double MaxDecelerationKmhPerSecond, double MaxVerticalAccuracyMeters);

	private CameraView _cameraView = null;

	private readonly RouteStorageService _storage;

	private readonly RecordingSessionJournalService _sessionJournal;

	private readonly OverlaySettingsService _overlaySettingsService;

	private readonly TelemetryOverlayState _telemetryState;

	private readonly IScreenRecorderService _screenRecorder;

	private readonly ICameraXOverlayRecorderService _cameraXOverlayRecorder;

	private readonly VideoLibraryService _videoLibrary;

	private readonly RouteExportService _routeExport;

	private readonly DeviceHealthService _deviceHealth;

	private readonly IAudioInputService _audioInputService;

	private readonly ICameraStabilizationService _cameraStabilizationService;

	private readonly IGnssSpeedService _gnssSpeedService;

	private readonly ActivationService _activationService;

	private readonly OsmRouteMapController _mapController;

	private readonly List<RoutePoint> _points = new List<RoutePoint>();

	private readonly object _pointsStateLock = new object();

	private readonly object _gpsStateLock = new object();

	private IReadOnlyList<CameraInfo> _availableCameras = Array.Empty<CameraInfo>();

	private float _pinchStartZoom = 1f;

	private float _currentZoomFactor = 1f;

	private float _minimumZoomFactor = 1f;

	private float _maximumZoomFactor = 4f;

	private CancellationTokenSource? _trackingCts;

	private Task? _trackingTask;

	private CancellationTokenSource? _healthMonitorCts;

	private Task? _healthMonitorTask;

	private FileStream? _videoStream;

	private Location? _lastLocation;

	private Location? _lastRawLocation;

	private Location? _latestLocation;

	private DateTimeOffset _lastGpsObservedAt;

	private DateTimeOffset _lastGpsReceivedAt;

	private DateTimeOffset _lastSpeedVisualUpdateAt;

	private double _lastRawFixIntervalMilliseconds;

	private double _displaySpeedKmh;

	private double _lastRawReportedSpeedKmh = double.NaN;

	private double _lastDisplacementSpeedKmh = double.NaN;

	private string _lastSpeedSource = "SIN DATOS";

	private DateTimeOffset _lastDirectGnssSpeedAt;

	private double _lastDirectGnssSpeedKmh = double.NaN;

	private double? _lastDirectGnssIntervalMilliseconds;

	private string _lastDirectGnssProvider = "SIN DATOS";

	private Location? _stationaryConfirmedLocation;

	private int _gpsRawFixCount;

	private int _gpsAcceptedFixCount;

	private int _gpsAcceptedSpeedOnlyFixCount;

	private int _gpsRejectedFixCount;

	private int _initialValidFixCount;

	private GpsMotionState _gpsMotionState = GpsMotionState.Unknown;

	private int _stationaryCandidateFixes;

	private int _movingCandidateFixes;

	private double _lastTrustedMovingSpeedKmh;

	private DateTimeOffset _lastTrustedSpeedAt;

	private double _stationaryDurationSeconds;

	private Location? _stationaryAnchorLocation;

	private double? _filteredAltitudeMeters;

	private double? _latestAltitudeMeters;

	private int _altitudeStableFixes;

	private DateTimeOffset _lastAltitudeTimestamp;

	private DateTimeOffset _startedAt;

	private string _videoPath = string.Empty;

	private double _distanceKm;

	private double _maxSpeedKmh;

	private double _currentSpeedKmh;

	private bool _isRecording;

	private readonly SemaphoreSlim _recordCommandGate = new SemaphoreSlim(1, 1);

	private bool _permissionsInitialized;

	private bool _legacyMigrationAttempted;

	private OverlaySettings _overlaySettings = new OverlaySettings();

	private CancellationTokenSource? _mapSnapshotRefreshCts;

	private bool _criticalStorageStopRequested;

	private bool _cameraPreviewNeedsRestart;

	private long _lastAvailableStorageBytes = -1L;

	private string _activeAudioInputName = "Automática · Android decide";

	private bool _audioFallbackAnnounced;

	private Guid _activeRouteId;

	private DateTimeOffset _lastSessionCheckpointAt;

	private int _lastCheckpointPointCount;

	private int _periodicCheckpointInProgress;

	private readonly SemaphoreSlim _sessionCheckpointGate = new SemaphoreSlim(1, 1);

	private bool _interruptedRecoveryAttempted;

	private bool _recoveryBlockedLegacyMigration;

	private readonly List<VideoSegment> _videoSegments = new List<VideoSegment>();

	private readonly List<Task> _segmentPublishTasks = new List<Task>();

	private readonly SemaphoreSlim _segmentSwitchGate = new SemaphoreSlim(1, 1);

	private CancellationTokenSource? _segmentRotationCts;

	private Task? _segmentRotationTask;

	private int _segmentIndex;

	private DateTimeOffset _segmentStartedAt;

	private string _segmentFileStem = string.Empty;

	private bool _sessionSegmentationEnabled;

	private readonly HashSet<int> _protectedSegmentIndices = new HashSet<int>();

	private readonly SemaphoreSlim _loopCleanupGate = new SemaphoreSlim(1, 1);

	private readonly object _segmentStateLock = new object();

	private TimeSpan? _sessionLoopRetention;

	private int _deletedByLoopCount;

	private CameraView CameraView => _cameraView;

	public MainPage(RouteStorageService storage, RecordingSessionJournalService sessionJournal, OverlaySettingsService overlaySettingsService, TelemetryOverlayState telemetryState, IScreenRecorderService screenRecorder, ICameraXOverlayRecorderService cameraXOverlayRecorder, VideoLibraryService videoLibrary, RouteExportService routeExport, DeviceHealthService deviceHealth, IAudioInputService audioInputService, ICameraStabilizationService cameraStabilizationService, IGnssSpeedService gnssSpeedService, ActivationService activationService)
	{
		InitializeComponent();
		_storage = storage;
		_sessionJournal = sessionJournal;
		_overlaySettingsService = overlaySettingsService;
		_telemetryState = telemetryState;
		_screenRecorder = screenRecorder;
		_cameraXOverlayRecorder = cameraXOverlayRecorder;
		_videoLibrary = videoLibrary;
		_routeExport = routeExport;
		_deviceHealth = deviceHealth;
		_audioInputService = audioInputService;
		_cameraStabilizationService = cameraStabilizationService;
		_gnssSpeedService = gnssSpeedService;
		_activationService = activationService;
		_mapController = new OsmRouteMapController(RouteMap);
		CreateFreshCameraView();
	}

	private void OnCameraViewHandlerChanged(object? sender, EventArgs e)
	{
		ApplyCameraPreviewMode();
	}

	private void CreateFreshCameraView()
	{
		CameraView previous = _cameraView;
		if (previous != null)
		{
			try
			{
				previous.StopCameraPreview();
			}
			catch
			{
			}
			previous.HandlerChanged -= OnCameraViewHandlerChanged;
			CameraHost.Content = null;
			try
			{
				previous.Handler?.DisconnectHandler();
			}
			catch
			{
			}
		}
		CameraView fresh = new CameraView
		{
			HorizontalOptions = LayoutOptions.Fill,
			VerticalOptions = LayoutOptions.Fill
		};
		fresh.HandlerChanged += OnCameraViewHandlerChanged;
		_cameraView = fresh;
		CameraHost.Content = fresh;
	}

	private async Task WaitForFreshCameraHandlerAsync(CancellationToken cancellationToken)
	{
		for (int i = 0; i < 50; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (CameraView.Handler != null && CameraView.SelectedCamera != null)
			{
				break;
			}
			await Task.Delay(100, cancellationToken);
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		ApplyOverlaySettings();
		if (_activationService.ShouldShowInitialRequest && !_isRecording)
		{
			await ShowInitialActivationRequestAsync();
		}
		if (!_interruptedRecoveryAttempted && !_isRecording)
		{
			_interruptedRecoveryAttempted = true;
			await TryRecoverInterruptedSessionAsync();
		}
		if (!_permissionsInitialized)
		{
			_permissionsInitialized = true;
			await PrepareCameraAsync();
		}
		else if (!_isRecording && _cameraPreviewNeedsRestart)
		{
			await RestoreCameraPreviewAfterCameraXAsync();
		}
		else if (!_isRecording)
		{
			await RefreshCameraSelectionFromSettingsAsync();
		}
		RefreshCameraControlsUi();
		UpdateSystemHealthUi();
		if (!_legacyMigrationAttempted && !_isRecording && !_recoveryBlockedLegacyMigration)
		{
			_legacyMigrationAttempted = true;
			await TryMigrateLegacyVideosAsync();
		}
	}

	private async Task ShowInitialActivationRequestAsync()
	{
		_activationService.MarkInitialRequestShown();
		if (!(await DisplayAlertAsync("RutaCam en modo limitado", "La instalación nueva funciona con opciones limitadas. Puedes solicitar por WhatsApp el código de activación completa por solo 5€ EUR, válido durante un año.", "SOLICITAR", "MÁS TARDE")))
		{
			return;
		}
		if (!(await _activationService.OpenWhatsAppRequestAsync()))
		{
			await DisplayAlertAsync("WhatsApp", "No fue posible abrir WhatsApp. Verifica que WhatsApp o WhatsApp Business estén instalados y actualizados.", "OK");
			return;
		}
		await DisplayAlertAsync("Solicitud preparada", "Envía el mensaje. Al volver a RutaCam se abrirá el cuadro para introducir el código.", "CONTINUAR");
		string code = await DisplayPromptAsync("Código de activación", "Introduce el código valido para la activacion.", "ACTIVAR", "CANCELAR", "Código numérico", 18, Keyboard.Numeric);
		if (!string.IsNullOrWhiteSpace(code))
		{
			ActivationResult result = _activationService.TryActivate(code);
			await DisplayAlertAsync(result.Success ? "Activación completa" : "Activación", result.Message, "OK");
			if (result.Success)
			{
				ApplyOverlaySettings();
			}
		}
	}

	private void ApplyPersonalLogoSource()
	{
		string customPath = _overlaySettings.CustomLogoPath;
		if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
		{
			try
			{
				PersonalLogo.Source = ImageSource.FromStream(() => File.OpenRead(customPath));
				return;
			}
			catch
			{
			}
		}
		PersonalLogo.Source = "personal_logo.png";
	}

	private void ApplyOverlaySettings()
	{
		_overlaySettings = _overlaySettingsService.Load();
		PersonalLogo.IsVisible = _overlaySettings.ShowLogo;
		ApplyPersonalLogoSource();
		PersonalLogo.Opacity = _overlaySettings.LogoOpacity;
		PersonalLogo.WidthRequest = _overlaySettings.LogoWidth;
		PersonalLogo.HeightRequest = Math.Max(44.0, _overlaySettings.LogoWidth * 0.68);
		MapCard.IsVisible = _overlaySettings.ShowMap;
		MapCard.Opacity = _overlaySettings.MapOpacity;
		MapCard.WidthRequest = _overlaySettings.MapWidth;
		MapCard.HeightRequest = _overlaySettings.MapWidth;
		ApplyMapShape();
		ApplyCameraPreviewMode();
		SpeedCard.IsVisible = _overlaySettings.ShowSpeed;
		DistanceCard.IsVisible = _overlaySettings.ShowDistance;
		TimerCard.IsVisible = _overlaySettings.ShowTimer;
		AverageSpeedCard.IsVisible = _overlaySettings.ShowAverageSpeed;
		MaxSpeedCard.IsVisible = _overlaySettings.ShowMaxSpeed;
		ExtraSpeedRow.IsVisible = _overlaySettings.ShowAverageSpeed || _overlaySettings.ShowMaxSpeed;
		RecordBadge.IsVisible = false;
		GpsInfoCard.IsVisible = _overlaySettings.ShowGpsStatus || _overlaySettings.ShowCoordinates || _overlaySettings.ShowAltitude;
		TelemetryPanel.IsVisible = _overlaySettings.ShowSpeed || _overlaySettings.ShowDistance || _overlaySettings.ShowTimer || _overlaySettings.ShowAverageSpeed || _overlaySettings.ShowMaxSpeed || _overlaySettings.ShowGpsStatus || _overlaySettings.ShowCoordinates || _overlaySettings.ShowAltitude;
		GpsLabel.IsVisible = _overlaySettings.ShowGpsStatus;
		CoordinatesLabel.IsVisible = _overlaySettings.ShowCoordinates;
		AltitudeLabel.IsVisible = _overlaySettings.ShowAltitude;
		DateTimeCard.IsVisible = _overlaySettings.ShowDateTime;
		DateTimeLabel.Text = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss");
		ApplyDateTimeCorner();
		SystemInfoCard.IsVisible = false;
		BatteryLabel.IsVisible = false;
		StorageLabel.IsVisible = false;
		UpdateSystemHealthUi();
		RefreshCameraControlsUi();
		switch (_overlaySettings.MapCorner)
		{
		case "Superior izquierda":
			MapCard.HorizontalOptions = LayoutOptions.Start;
			MapCard.VerticalOptions = LayoutOptions.Start;
			MapCard.Margin = new Thickness(16.0, 62.0, 16.0, 0.0);
			break;
		case "Inferior derecha":
			MapCard.HorizontalOptions = LayoutOptions.End;
			MapCard.VerticalOptions = LayoutOptions.End;
			MapCard.Margin = new Thickness(16.0, 0.0, 16.0, 250.0);
			break;
		case "Inferior izquierda":
			MapCard.HorizontalOptions = LayoutOptions.Start;
			MapCard.VerticalOptions = LayoutOptions.End;
			MapCard.Margin = new Thickness(16.0, 0.0, 16.0, 250.0);
			break;
		default:
			MapCard.HorizontalOptions = LayoutOptions.End;
			MapCard.VerticalOptions = LayoutOptions.Start;
			MapCard.Margin = new Thickness(16.0, 62.0, 16.0, 0.0);
			break;
		}
	}

	private void ApplyDateTimeCorner()
	{
		string corner = _overlaySettings.DateTimeCorner ?? "Inferior derecha";
		bool left = corner.Contains("izquierda", StringComparison.OrdinalIgnoreCase);
		bool bottom = corner.Contains("Inferior", StringComparison.OrdinalIgnoreCase);
		DateTimeCard.HorizontalOptions = (left ? LayoutOptions.Start : LayoutOptions.End);
		DateTimeCard.VerticalOptions = (bottom ? LayoutOptions.End : LayoutOptions.Start);
		double horizontal = 18.0;
		double top = 18.0;
		double bottomMargin = 110.0;
		if (!bottom)
		{
			if (left && _overlaySettings.ShowLogo)
			{
				top = Math.Max(top, _overlaySettings.LogoWidth * 0.68 + 42.0);
			}
			if (_overlaySettings.ShowMap && string.Equals(_overlaySettings.MapCorner, corner, StringComparison.OrdinalIgnoreCase))
			{
				top = Math.Max(top, _overlaySettings.MapWidth + 78.0);
			}
			DateTimeCard.Margin = new Thickness(horizontal, top, horizontal, 0.0);
		}
		else
		{
			if (_overlaySettings.ShowMap && string.Equals(_overlaySettings.MapCorner, corner, StringComparison.OrdinalIgnoreCase))
			{
				bottomMargin = Math.Max(bottomMargin, _overlaySettings.MapWidth + 128.0);
			}
			if (left && TelemetryPanel.IsVisible)
			{
				bottomMargin = Math.Max(bottomMargin, 420.0);
			}
			DateTimeCard.Margin = new Thickness(horizontal, 0.0, horizontal, bottomMargin);
		}
	}

	private void ApplyMapShape()
	{
		double size = Math.Max(1.0, _overlaySettings.MapWidth);
		if (string.Equals(_overlaySettings.MapShape, "Circular", StringComparison.OrdinalIgnoreCase))
		{
			MapCard.StrokeShape = new Ellipse();
			MapCard.Clip = new EllipseGeometry(new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);
		}
		else
		{
			MapCard.StrokeShape = new Rectangle();
			MapCard.Clip = null;
		}
	}

	private void ApplyCameraPreviewMode()
	{
		if (CameraView.Handler?.PlatformView is PreviewView previewView)
		{
			PreviewView.ScaleType scaleType = (string.Equals(_overlaySettings.CameraPreviewMode, "Ver encuadre completo", StringComparison.OrdinalIgnoreCase) ? PreviewView.ScaleType.FitCenter : PreviewView.ScaleType.FillCenter);
			if (scaleType != null)
			{
				previewView.SetScaleType(scaleType);
			}
		}
	}

	private async Task<bool> EnsurePermissionsAsync()
	{
		PermissionStatus camera = await Permissions.RequestAsync<Permissions.Camera>();
		PermissionStatus microphone = await Permissions.RequestAsync<Permissions.Microphone>();
		PermissionStatus location = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
		return camera == PermissionStatus.Granted && microphone == PermissionStatus.Granted && location == PermissionStatus.Granted;
	}

	private async Task PrepareCameraAsync()
	{
		try
		{
			if (!(await EnsurePermissionsAsync()))
			{
				RecordStatusLabel.Text = "PERMISOS REQUERIDOS";
				return;
			}
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(8L));
			_availableCameras = await CameraView.GetAvailableCameras(cts.Token);
			ApplyPreferredCameraSelection();
			ApplyZoomFactor(_overlaySettings.DefaultZoomFactor);
			await CameraView.StartCameraPreview(cts.Token);
			ApplyCameraPreviewMode();
			RefreshCameraControlsUi();
			RecordStatusLabel.Text = "LISTO";
		}
		catch
		{
			RecordStatusLabel.Text = "CÁMARA NO DISPONIBLE";
		}
	}

	public async Task ToggleRecordingFromHardwareAsync()
	{
		if (!(await _recordCommandGate.WaitAsync(0)))
		{
			return;
		}
		try
		{
			if (_isRecording)
			{
				await StopRecordingCoreAsync(saveRecord: true);
			}
			else
			{
				await StartRecordingCoreAsync();
			}
		}
		finally
		{
			_recordCommandGate.Release();
		}
	}

	private async void OnStartClicked(object sender, EventArgs e)
	{
		await StartRecordingAsync();
	}

	private async Task StartRecordingAsync()
	{
		await _recordCommandGate.WaitAsync();
		try
		{
			await StartRecordingCoreAsync();
		}
		finally
		{
			_recordCommandGate.Release();
		}
	}

	private async Task StartRecordingCoreAsync()
	{
		if (_isRecording)
		{
			return;
		}
		try
		{
			if (!(await EnsurePermissionsAsync()))
			{
				await DisplayAlertAsync("Permisos", "La cámara, el micrófono y la ubicación son necesarios para iniciar un recorrido.", "OK");
				return;
			}
			if (OperatingSystem.IsAndroidVersionAtLeast(33) && IsBurnInRecordingMode() && await Permissions.RequestAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
			{
				await DisplayAlertAsync("Notificaciones", "Permite las notificaciones de RutaCam GPS para iniciar la grabación con telemetría.", "OK");
				return;
			}
			long availableBytes = _deviceHealth.GetAvailableStorageBytes();
			Interlocked.Exchange(ref _lastAvailableStorageBytes, availableBytes);
			double minimumBytes = _overlaySettings.MinimumFreeStorageGb * 1024.0 * 1024.0 * 1024.0;
			if (availableBytes >= 0 && (double)availableBytes < minimumBytes)
			{
				await DisplayAlertAsync("Almacenamiento insuficiente", $"RutaCam necesita al menos {_overlaySettings.MinimumFreeStorageGb:F2} GB libres para iniciar. Actualmente hay {DeviceHealthService.FormatStorage(availableBytes).Replace("LIBRE ", string.Empty)}.", "OK");
				return;
			}
			double batteryPercent = _deviceHealth.BatteryPercent;
			if (_overlaySettings.WarnOnLowBattery && batteryPercent >= 0.0 && batteryPercent <= 15.0 && !_deviceHealth.IsCharging && !(await DisplayAlertAsync("Batería baja", $"La batería está en {batteryPercent:F0}%. Una grabación larga usa cámara, GPS, mapa y codificador al mismo tiempo.", "Continuar", "Cancelar")))
			{
				return;
			}
			if (_overlaySettings.RecordAudio)
			{
				AudioRouteResult audioRoute = await _audioInputService.ActivateAsync(_overlaySettings.AudioInputDeviceId, CancellationToken.None);
				_activeAudioInputName = audioRoute.ActiveDisplayName;
				_audioFallbackAnnounced = audioRoute.UsedFallback;
				if (!audioRoute.Success && audioRoute.UsedFallback)
				{
					await DisplayAlertAsync("Fuente de audio", audioRoute.Message, "Continuar");
				}
			}
			else
			{
				await _audioInputService.ReleaseAsync();
				_activeAudioInputName = "Sin audio";
			}
			if (IsCameraXOverlayMode())
			{
				string noticeKey = "CameraXHudV193NoticeShown";
				if (!Preferences.Default.Get(noticeKey, defaultValue: false))
				{
					await DisplayAlertAsync("CameraX · HUD nativo", "RutaCam dispone de HUD Limpio, Básico y Completo dentro del MP4 CameraX. El perfil Completo añade promedio, máxima, GPS y altitud según tus interruptores de telemetría.", "Entendido");
					Preferences.Default.Set(noticeKey, value: true);
				}
			}
			ResetSession();
			CameraTools.IsVisible = false;
			ZoomPanel.IsVisible = false;
			string videosDirectory = System.IO.Path.Combine(FileSystem.AppDataDirectory, "Videos");
			Directory.CreateDirectory(videosDirectory);
			bool burnInMode = IsBurnInRecordingMode();
			bool cameraXNativeMode = IsCameraXNativeRecorderMode();
			_startedAt = DateTimeOffset.UtcNow;
			_segmentStartedAt = _startedAt;
			_segmentIndex = 1;
			_segmentFileStem = $"ruta_{DateTime.Now:yyyyMMdd_HHmmss}";
			_sessionLoopRetention = (cameraXNativeMode ? ResolveLoopRetention() : ((TimeSpan?)null));
			_sessionSegmentationEnabled = cameraXNativeMode && ResolveSegmentDuration().HasValue;
			_videoPath = BuildSegmentVideoPath(_segmentIndex);
			if (burnInMode)
			{
				ActionBar.IsVisible = false;
				RecordDot.Color = Color.FromArgb("#FB4B55");
				RecordStatusLabel.Text = "REC";
				await _screenRecorder.StartAsync(_videoPath, CancellationToken.None);
			}
			else if (cameraXNativeMode)
			{
				CameraView.StopCameraPreview();
				await StartCameraXSegmentAsync(_videoPath, TimeSpan.Zero, CancellationToken.None);
				if (CameraXHudShowsMap())
				{
					_cameraXOverlayRecorder.UpdateMapState(CameraXMapState.Empty);
				}
				Android.App.Activity? currentActivity = Platform.CurrentActivity;
				if (currentActivity is MainActivity activity)
				{
					activity.EnterRecordingPresentation(_overlaySettings.HideSystemBarsDuringRecording, _overlaySettings.LockOrientationDuringRecording);
				}
			}
			else
			{
				_videoStream = new FileStream(_videoPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
				await CameraView.StartVideoRecording(_videoStream, CancellationToken.None);
			}
			_activeRouteId = Guid.NewGuid();
			_isRecording = true;
			await SaveSessionCheckpointAsync("Recording", force: true, CancellationToken.None);
			ProtectClipButton.IsVisible = cameraXNativeMode && _sessionSegmentationEnabled;
			ProtectClipButton.Text = "\ud83d\udd12 GUARDAR CLIP";
			DeviceDisplay.Current.KeepScreenOn = true;
			StartHealthMonitor();
			StartButton.IsEnabled = false;
			StopButton.IsEnabled = true;
			HistoryButton.IsEnabled = false;
			SettingsButton.IsEnabled = false;
			RecordDot.Color = Color.FromArgb("#FB4B55");
			RecordStatusLabel.Text = (_sessionSegmentationEnabled ? $"REC · CAMERAX · CLIP {_segmentIndex:000}" : (IsCameraXOverlayMode() ? "REC · CAMERAX HUD" : (IsCameraXNativeRecorderMode() ? "REC · CAMERAX" : "REC")));
			UpdateTelemetryState();
			StartDirectGnssSpeedEngine();
			_trackingCts = new CancellationTokenSource();
			_trackingTask = TrackRouteAsync(_trackingCts.Token);
			StartSegmentRotationIfNeeded();
		}
		catch (Exception ex)
		{
			_isRecording = false;
			StopDirectGnssSpeedEngine();
			if (IsCameraXNativeRecorderMode())
			{
				try
				{
					await _cameraXOverlayRecorder.StopAsync(CancellationToken.None);
				}
				catch
				{
				}
				_cameraPreviewNeedsRestart = true;
				await RestoreCameraPreviewAfterCameraXAsync();
			}
			else
			{
				try
				{
					if (!IsBurnInRecordingMode())
					{
						await CameraView.StopVideoRecording(CancellationToken.None);
					}
					else
					{
						await _screenRecorder.StopAsync(CancellationToken.None);
					}
				}
				catch
				{
				}
			}
			await _audioInputService.ReleaseAsync();
			await SafeDisposeVideoAsync();
			if (_activeRouteId != Guid.Empty)
			{
				try
				{
					await CloseSessionJournalAsync();
				}
				catch
				{
				}
			}
			await DisplayAlertAsync("No se pudo iniciar", ex.Message, "OK");
			RestoreIdleUi();
		}
	}

	private async void OnStopClicked(object sender, EventArgs e)
	{
		await StopRecordingAsync(saveRecord: true);
	}

	private async Task StopRecordingAsync(bool saveRecord)
	{
		await _recordCommandGate.WaitAsync();
		try
		{
			await StopRecordingCoreAsync(saveRecord);
		}
		finally
		{
			_recordCommandGate.Release();
		}
	}

	private async Task StopRecordingCoreAsync(bool saveRecord)
	{
		if (!_isRecording)
		{
			return;
		}
		bool wasCameraXNative = IsCameraXNativeRecorderMode();
		try
		{
			await SaveSessionCheckpointAsync("Stopping", force: true, CancellationToken.None);
		}
		catch
		{
		}
		_isRecording = false;
		DeviceDisplay.Current.KeepScreenOn = false;
		_trackingCts?.Cancel();
		StopDirectGnssSpeedEngine();
		_healthMonitorCts?.Cancel();
		_mapSnapshotRefreshCts?.Cancel();
		_segmentRotationCts?.Cancel();
		UpdateTelemetryState();
		RecordStatusLabel.Text = "GUARDANDO...";
		VideoPublishResult legacyPublishResult = null;
		if (wasCameraXNative)
		{
			await _segmentSwitchGate.WaitAsync();
			try
			{
				try
				{
					await _cameraXOverlayRecorder.StopAsync(CancellationToken.None);
				}
				catch
				{
				}
				QueueCompletedSegmentForPublish(endedAt: DateTimeOffset.UtcNow, videoPath: _videoPath, index: _segmentIndex, startedAt: _segmentStartedAt);
			}
			finally
			{
				_segmentSwitchGate.Release();
			}
			if (_segmentPublishTasks.Count > 0)
			{
				try
				{
					await Task.WhenAll(_segmentPublishTasks.ToArray());
				}
				catch
				{
				}
			}
			_cameraPreviewNeedsRestart = true;
			if (!saveRecord)
			{
				await RestoreCameraPreviewAfterCameraXAsync();
			}
		}
		else
		{
			try
			{
				if (!IsBurnInRecordingMode())
				{
					await CameraView.StopVideoRecording(CancellationToken.None);
				}
				else
				{
					await _screenRecorder.StopAsync(CancellationToken.None);
				}
			}
			catch
			{
			}
		}
		await SafeDisposeVideoAsync();
		await _audioInputService.ReleaseAsync();
		if (_trackingTask != null)
		{
			try
			{
				await _trackingTask;
			}
			catch (OperationCanceledException)
			{
			}
			catch
			{
			}
		}
		DateTimeOffset endedAt = DateTimeOffset.UtcNow;
		TimeSpan duration = endedAt - _startedAt;
		double averageSpeed = ((duration.TotalHours > 0.0) ? (_distanceKm / duration.TotalHours) : 0.0);
		RouteRecord obj7;
		object loopRecordingMode;
		if (saveRecord)
		{
			try
			{
				await SaveSessionCheckpointAsync("Stopping", force: true, CancellationToken.None);
			}
			catch
			{
			}
			if (!wasCameraXNative)
			{
				legacyPublishResult = await _videoLibrary.PublishAsync(_videoPath, _startedAt, CancellationToken.None);
			}
			obj7 = new RouteRecord
			{
				Id = ((_activeRouteId == Guid.Empty) ? Guid.NewGuid() : _activeRouteId),
				StartedAt = _startedAt,
				EndedAt = endedAt,
				DistanceKm = _distanceKm,
				AverageSpeedKmh = averageSpeed,
				MaxSpeedKmh = _maxSpeedKmh,
				RecordingEngine = (wasCameraXNative ? GetCameraXRecordingEngineLabel() : _overlaySettings.RecordingEngine)
			};
			if (wasCameraXNative)
			{
				TimeSpan? sessionLoopRetention = _sessionLoopRetention;
				if (sessionLoopRetention.HasValue)
				{
					loopRecordingMode = _overlaySettings.LoopRecording;
					goto IL_09a4;
				}
			}
			loopRecordingMode = "Desactivada";
			goto IL_09a4;
		}
		try
		{
			await CloseSessionJournalAsync();
		}
		catch
		{
		}
		RestoreIdleUi();
		return;
		IL_09a4:
		obj7.LoopRecordingMode = (string)loopRecordingMode;
		obj7.DeletedByLoopCount = _deletedByLoopCount;
		obj7.VideoPath = ((wasCameraXNative || (legacyPublishResult?.Success ?? false)) ? string.Empty : _videoPath);
		obj7.VideoSegments = (wasCameraXNative ? SnapshotVideoSegments() : new List<VideoSegment>());
		obj7.Points = SnapshotPoints();
		RouteRecord record = obj7;
		if ((object)legacyPublishResult != null)
		{
			VideoLibraryService.ApplyPublishResult(record, legacyPublishResult);
		}
		await _storage.SaveAsync(record);
		await CloseSessionJournalAsync();
		RestoreIdleUi();
		await base.Navigation.PushAsync(new RecordingSummaryPage(record, _videoLibrary, _routeExport));
	}

	private void QueueCompletedSegmentForPublish(string videoPath, int index, DateTimeOffset startedAt, DateTimeOffset endedAt)
	{
		if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
		{
			return;
		}
		int normalizedIndex = Math.Max(1, index);
		lock (_segmentStateLock)
		{
			if (_videoSegments.Any((VideoSegment x) => x.Index == normalizedIndex || string.Equals(x.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
		}
		try
		{
			if (new FileInfo(videoPath).Length <= 0)
			{
				return;
			}
		}
		catch
		{
			return;
		}
		bool isProtected;
		lock (_segmentStateLock)
		{
			isProtected = _protectedSegmentIndices.Contains(normalizedIndex);
		}
		VideoSegment segment = new VideoSegment
		{
			Index = normalizedIndex,
			StartedAt = startedAt,
			EndedAt = endedAt,
			VideoPath = videoPath,
			IsProtected = isProtected,
			ProtectedAt = (isProtected ? new DateTimeOffset?(DateTimeOffset.UtcNow) : ((DateTimeOffset?)null))
		};
		lock (_segmentStateLock)
		{
			_videoSegments.Add(segment);
		}
		QueuePeriodicSessionCheckpoint(force: true);
		Task task = PublishSegmentAsync(segment);
		_segmentPublishTasks.Add(task);
	}

	private async Task PublishSegmentAsync(VideoSegment segment)
	{
		try
		{
			VideoLibraryService.ApplyPublishResult(segment, await _videoLibrary.PublishAsync(segment.VideoPath, _startedAt, _sessionSegmentationEnabled ? new int?(segment.Index) : ((int?)null), CancellationToken.None));
			await SaveSessionCheckpointAsync("Recording", force: true, CancellationToken.None);
		}
		catch
		{
		}
		await EnforceLoopRetentionAsync();
	}

	private async Task EnforceLoopRetentionAsync()
	{
		TimeSpan? retention = _sessionLoopRetention;
		if (!retention.HasValue)
		{
			return;
		}
		await _loopCleanupGate.WaitAsync();
		try
		{
			DateTimeOffset cutoff = DateTimeOffset.UtcNow - retention.Value;
			List<VideoSegment> candidates;
			lock (_segmentStateLock)
			{
				candidates = (from x in _videoSegments
					where !x.IsProtected && x.HasPublicVideo && x.EndedAt <= cutoff
					orderby x.EndedAt
					select x).ToList();
			}
			foreach (VideoSegment segment in candidates)
			{
				if (!segment.IsProtected && await _videoLibrary.DeleteSegmentVideoAsync(segment))
				{
					lock (_segmentStateLock)
					{
						_videoSegments.Remove(segment);
					}
					_deletedByLoopCount++;
				}
			}
			if (candidates.Count > 0)
			{
				await SaveSessionCheckpointAsync("Recording", force: true, CancellationToken.None);
			}
		}
		finally
		{
			_loopCleanupGate.Release();
		}
	}

	private async void OnProtectClipClicked(object sender, EventArgs e)
	{
		if (!_isRecording || !IsCameraXNativeRecorderMode() || !_sessionSegmentationEnabled)
		{
			return;
		}
		int current = Math.Max(1, _segmentIndex);
		int first = (_overlaySettings.ProtectEventContext ? Math.Max(1, current - 1) : current);
		int last = (_overlaySettings.ProtectEventContext ? (current + 1) : current);
		DateTimeOffset now = DateTimeOffset.UtcNow;
		lock (_segmentStateLock)
		{
			for (int i = first; i <= last; i++)
			{
				_protectedSegmentIndices.Add(i);
			}
			foreach (VideoSegment segment in _videoSegments.Where((VideoSegment x) => x.Index >= first && x.Index <= last))
			{
				segment.IsProtected = true;
				VideoSegment videoSegment = segment;
				DateTimeOffset? protectedAt = videoSegment.ProtectedAt;
				protectedAt.GetValueOrDefault();
				if (!protectedAt.HasValue)
				{
					videoSegment.ProtectedAt = now;
				}
			}
		}
		ProtectClipButton.Text = ((first == last) ? $"\ud83d\udd12 CLIP {current:000}" : $"\ud83d\udd12 CLIPS {first:000}–{last:000}");
		QueuePeriodicSessionCheckpoint(force: true);
		await Task.Delay(1600);
		if (_isRecording)
		{
			ProtectClipButton.Text = "\ud83d\udd12 GUARDAR CLIP";
		}
	}

	private string BuildSegmentVideoPath(int index)
	{
		string videosDirectory = System.IO.Path.Combine(FileSystem.AppDataDirectory, "Videos");
		Directory.CreateDirectory(videosDirectory);
		string suffix = (_sessionSegmentationEnabled ? $"_{Math.Max(1, index):000}" : string.Empty);
		return System.IO.Path.Combine(videosDirectory, _segmentFileStem + suffix + ".mp4");
	}

	private TimeSpan? ResolveLoopRetention()
	{
		if (!IsCameraXNativeRecorderMode())
		{
			return null;
		}
		string loopRecording = _overlaySettings.LoopRecording;
		if (1 == 0)
		{
		}
		TimeSpan? result = loopRecording switch
		{
			"1 hora" => TimeSpan.FromHours(1), 
			"2 horas" => TimeSpan.FromHours(2), 
			"4 horas" => TimeSpan.FromHours(4), 
			"8 horas" => TimeSpan.FromHours(8), 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private TimeSpan? ResolveSegmentDuration()
	{
		if (!IsCameraXNativeRecorderMode())
		{
			return null;
		}
		string videoSegmentation = _overlaySettings.VideoSegmentation;
		if (1 == 0)
		{
		}
		TimeSpan? timeSpan = videoSegmentation switch
		{
			"5 minutos" => TimeSpan.FromMinutes(5L), 
			"10 minutos" => TimeSpan.FromMinutes(10L), 
			"20 minutos" => TimeSpan.FromMinutes(20L), 
			"30 minutos" => TimeSpan.FromMinutes(30L), 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		TimeSpan? configured = timeSpan;
		if (configured.HasValue)
		{
			return configured;
		}
		TimeSpan? loopRetention = ResolveLoopRetention();
		if (!loopRetention.HasValue)
		{
			return null;
		}
		return (loopRetention <= TimeSpan.FromMinutes(2L)) ? TimeSpan.FromMinutes(1L) : TimeSpan.FromMinutes(5L);
	}

	private void StartSegmentRotationIfNeeded()
	{
		TimeSpan? duration = ResolveSegmentDuration();
		if (_isRecording && duration.HasValue && IsCameraXNativeRecorderMode())
		{
			_segmentRotationCts?.Cancel();
			_segmentRotationCts?.Dispose();
			_segmentRotationCts = new CancellationTokenSource();
			_segmentRotationTask = SegmentRotationLoopAsync(duration.Value, _segmentRotationCts.Token);
		}
	}

	private async Task SegmentRotationLoopAsync(TimeSpan duration, CancellationToken token)
	{
		while (!token.IsCancellationRequested && _isRecording)
		{
			try
			{
				await Task.Delay(duration, token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			if (token.IsCancellationRequested || !_isRecording)
			{
				break;
			}
			if (await RotateCameraXSegmentAsync(token))
			{
				continue;
			}
			MainThread.BeginInvokeOnMainThread(async delegate
			{
				if (_isRecording)
				{
					RecordStatusLabel.Text = "ERROR DE SEGMENTO";
					await StopRecordingAsync(saveRecord: true);
				}
			});
			break;
		}
	}

	private async Task<bool> RotateCameraXSegmentAsync(CancellationToken token)
	{
		await _segmentSwitchGate.WaitAsync(token);
		try
		{
			if (!_isRecording || !IsCameraXNativeRecorderMode())
			{
				return true;
			}
			string previousPath = _videoPath;
			int previousIndex = _segmentIndex;
			DateTimeOffset previousStartedAt = _segmentStartedAt;
			MainThread.BeginInvokeOnMainThread(delegate
			{
				RecordStatusLabel.Text = $"GUARDANDO CLIP {previousIndex:000}...";
			});
			await _cameraXOverlayRecorder.StopAsync(CancellationToken.None);
			QueueCompletedSegmentForPublish(endedAt: DateTimeOffset.UtcNow, videoPath: previousPath, index: previousIndex, startedAt: previousStartedAt);
			if (!_isRecording)
			{
				return true;
			}
			_segmentIndex++;
			_segmentStartedAt = DateTimeOffset.UtcNow;
			_videoPath = BuildSegmentVideoPath(_segmentIndex);
			await StartCameraXSegmentAsync(elapsed: _segmentStartedAt - _startedAt, outputPath: _videoPath, cancellationToken: CancellationToken.None);
			if (CameraXHudShowsMap())
			{
				await PushCurrentCameraXMapStateAsync();
			}
			MainThread.BeginInvokeOnMainThread(delegate
			{
				RecordStatusLabel.Text = $"REC · CAMERAX · CLIP {_segmentIndex:000}";
			});
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			_segmentSwitchGate.Release();
		}
	}

	private async Task StartCameraXSegmentAsync(string outputPath, TimeSpan elapsed, CancellationToken cancellationToken)
	{
		PreviewView previewView = (CameraView.Handler?.PlatformView as PreviewView) ?? throw new InvalidOperationException("No se pudo obtener la vista nativa de CameraX.");
		CameraInfo? selectedCamera = CameraView.SelectedCamera;
		bool useFrontCamera = selectedCamera != null && selectedCamera.Position == CameraPosition.Front;
		float currentZoom = ((_currentZoomFactor <= 0f) ? 1f : _currentZoomFactor);
		bool torchRequested = CameraView.IsTorchOn;
		await _cameraXOverlayRecorder.StartAsync(previewView, outputPath, useFrontCamera, _overlaySettings.RecordAudio, _overlaySettings.AudioInputDeviceId, BuildCameraXRecordingOptions(), BuildCameraXHudOptions(), BuildCameraXHudSnapshot(elapsed, true), cancellationToken);
		_cameraXOverlayRecorder.SetZoom(currentZoom);
		_cameraXOverlayRecorder.SetTorch(torchRequested);
	}

	private async Task PushCurrentCameraXMapStateAsync()
	{
		if (!CameraXHudShowsMap())
		{
			return;
		}
		try
		{
			await MainThread.InvokeOnMainThreadAsync(delegate
			{
				CameraXMapState cameraXMapState = _mapController.CaptureCameraXMapState(SnapshotPoints(), includeBaseSnapshot: true);
				_cameraXOverlayRecorder.UpdateMapState(cameraXMapState);
				byte[] baseMapPng = cameraXMapState.BaseMapPng;
				if (baseMapPng != null && baseMapPng.Length > 0)
				{
					_telemetryState.SetMapSnapshot(cameraXMapState.BaseMapPng);
				}
			});
		}
		catch
		{
		}
	}

	private async Task TryMigrateLegacyVideosAsync()
	{
		try
		{
			await _videoLibrary.MigrateLegacyVideosAsync(_storage, CancellationToken.None);
		}
		catch
		{
		}
	}

	private async Task TrackRouteAsync(CancellationToken token)
	{
		IGeolocation geolocation = Geolocation.Default;
		TaskCompletionSource<GeolocationError> listeningFailed = new TaskCompletionSource<GeolocationError>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool listenerStarted = false;
		bool usePollingFallback = false;
		EventHandler<GeolocationLocationChangedEventArgs> locationChanged = delegate(object? _, GeolocationLocationChangedEventArgs args)
		{
			if (token.IsCancellationRequested)
			{
				return;
			}
			try
			{
				ProcessLocation(args.Location);
			}
			catch
			{
				MainThread.BeginInvokeOnMainThread(delegate
				{
					GpsLabel.Text = "GPS: lectura inválida";
				});
			}
		};
		EventHandler<GeolocationListeningFailedEventArgs> listenerFailed = delegate(object? _, GeolocationListeningFailedEventArgs args)
		{
			listeningFailed.TrySetResult(args.Error);
		};
		try
		{
			if (geolocation.IsListeningForeground)
			{
				geolocation.StopListeningForeground();
			}
			geolocation.LocationChanged += locationChanged;
			geolocation.ListeningFailed += listenerFailed;
			GeolocationListeningRequest request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(250L));
			listenerStarted = await geolocation.StartListeningForegroundAsync(request);
			usePollingFallback = !listenerStarted;
			while (listenerStarted && !listeningFailed.Task.IsCompleted && !token.IsCancellationRequested)
			{
				UpdateTrackingClockAndSignalState();
				await Task.Delay(100, token);
			}
			if (listeningFailed.Task.IsCompleted && !token.IsCancellationRequested)
			{
				usePollingFallback = true;
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (FeatureNotSupportedException)
		{
			usePollingFallback = true;
		}
		catch (InvalidOperationException)
		{
			usePollingFallback = true;
		}
		catch
		{
			usePollingFallback = true;
		}
		finally
		{
			geolocation.LocationChanged -= locationChanged;
			geolocation.ListeningFailed -= listenerFailed;
			if (listenerStarted && geolocation.IsListeningForeground)
			{
				try
				{
					geolocation.StopListeningForeground();
				}
				catch
				{
				}
			}
		}
		if (usePollingFallback && !token.IsCancellationRequested)
		{
			MainThread.BeginInvokeOnMainThread(delegate
			{
				GpsLabel.Text = "GPS: modo de compatibilidad";
			});
			await TrackRoutePollingFallbackAsync(token);
		}
	}

	private async Task TrackRoutePollingFallbackAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				using CancellationTokenSource requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
				requestCts.CancelAfter(TimeSpan.FromMilliseconds(1200L));
				GeolocationRequest request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromMilliseconds(1200L));
				Location location = await Geolocation.Default.GetLocationAsync(request, requestCts.Token);
				if ((object)location != null)
				{
					ProcessLocation(location);
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch
			{
				MainThread.BeginInvokeOnMainThread(delegate
				{
					GpsLabel.Text = "GPS: sin señal";
				});
			}
			UpdateTrackingClockAndSignalState();
			try
			{
				await Task.Delay(100, token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void UpdateTrackingClockAndSignalState()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		TimeSpan elapsed = now - _startedAt;
		double averageSpeed = ((elapsed.TotalHours > 0.0) ? (_distanceKm / elapsed.TotalHours) : 0.0);
		string gpsStatus = null;
		double displayedSpeed;
		lock (_gpsStateLock)
		{
			_lastSpeedVisualUpdateAt = now;
			if (_gnssSpeedService.IsRunning && double.IsFinite(_lastDirectGnssSpeedKmh))
			{
				_currentSpeedKmh = Math.Max(0.0, _lastDirectGnssSpeedKmh);
				_displaySpeedKmh = _currentSpeedKmh;
				displayedSpeed = _displaySpeedKmh;
				TimeSpan speedAge = ((_lastDirectGnssSpeedAt == default(DateTimeOffset)) ? TimeSpan.MaxValue : (now - _lastDirectGnssSpeedAt));
				if (speedAge > TimeSpan.FromSeconds(1.5))
				{
					gpsStatus = $"GPS: esperando nueva velocidad · {speedAge.TotalSeconds:F1} s · {_lastDirectGnssProvider}";
				}
			}
			else
			{
				if (_lastGpsReceivedAt == default(DateTimeOffset))
				{
					_currentSpeedKmh = 0.0;
					_displaySpeedKmh = 0.0;
					gpsStatus = ((_lastGpsObservedAt == default(DateTimeOffset)) ? "GPS: buscando señal" : "GPS: buscando mejor precisión");
				}
				else
				{
					_displaySpeedKmh = Math.Max(0.0, _currentSpeedKmh);
				}
				displayedSpeed = _displaySpeedKmh;
			}
		}
		MainThread.BeginInvokeOnMainThread(delegate
		{
			TimeLabel.Text = FormatDuration(elapsed);
			if (_overlaySettings.ShowDateTime)
			{
				DateTimeLabel.Text = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss");
			}
			AverageSpeedLabel.Text = $"{averageSpeed:F0} km/h";
			MaxSpeedLabel.Text = $"{_maxSpeedKmh:F0} km/h";
			SpeedLabel.Text = displayedSpeed.ToString("F0");
			if (gpsStatus != null)
			{
				GpsLabel.Text = gpsStatus;
			}
		});
		UpdateTelemetryState(elapsed);
	}

	private void StartDirectGnssSpeedEngine()
	{
		StopDirectGnssSpeedEngine();
		lock (_gpsStateLock)
		{
			_lastDirectGnssSpeedAt = default(DateTimeOffset);
			_lastDirectGnssSpeedKmh = double.NaN;
			_lastDirectGnssIntervalMilliseconds = null;
			_lastDirectGnssProvider = "SIN DATOS";
		}
		_gnssSpeedService.SpeedChanged += OnDirectGnssSpeedChanged;
		_gnssSpeedService.StatusChanged += OnDirectGnssStatusChanged;
		try
		{
			_gnssSpeedService.Start();
		}
		catch (Exception value)
		{
			Debug.WriteLine($"RutaCam GNSS FIX43 · no pudo iniciar motor directo: {value}");
		}
	}

	private void StopDirectGnssSpeedEngine()
	{
		_gnssSpeedService.SpeedChanged -= OnDirectGnssSpeedChanged;
		_gnssSpeedService.StatusChanged -= OnDirectGnssStatusChanged;
		try
		{
			_gnssSpeedService.Stop();
		}
		catch
		{
		}
	}

	private void OnDirectGnssStatusChanged(object? sender, string status)
	{
		Debug.WriteLine("RutaCam GNSS FIX43 · " + status);
	}

	private void OnDirectGnssSpeedChanged(object? sender, GnssSpeedSample sample)
	{
		if (!_isRecording || !sample.HasSpeed || !double.IsFinite(sample.SpeedKmh) || sample.SpeedKmh < 0.0)
		{
			return;
		}
		double speed = Math.Max(0.0, sample.SpeedKmh);
		lock (_gpsStateLock)
		{
			_lastDirectGnssSpeedAt = sample.ReceivedAt;
			_lastDirectGnssSpeedKmh = speed;
			_lastDirectGnssIntervalMilliseconds = sample.IntervalMilliseconds;
			_lastDirectGnssProvider = sample.Provider;
			_lastRawReportedSpeedKmh = speed;
			double? intervalMilliseconds = sample.IntervalMilliseconds;
			if (intervalMilliseconds.HasValue)
			{
				double interval = intervalMilliseconds.GetValueOrDefault();
				if (interval > 0.0)
				{
					_lastRawFixIntervalMilliseconds = interval;
				}
			}
			_lastSpeedSource = sample.Provider;
			_currentSpeedKmh = speed;
			_displaySpeedKmh = speed;
			_maxSpeedKmh = Math.Max(_maxSpeedKmh, speed);
		}
		if (speed >= 4.0)
		{
			MarkGpsMoving(speed, sample.ReceivedAt);
		}
		MainThread.BeginInvokeOnMainThread(delegate
		{
			SpeedLabel.Text = speed.ToString("F0");
			MaxSpeedLabel.Text = $"{_maxSpeedKmh:F0} km/h";
		});
		UpdateTelemetryState();
		Debug.WriteLine($"RutaCam GNSS FIX43 · {sample.Provider} · {speed:F1} km/h · interval={sample.IntervalMilliseconds?.ToString("F0") ?? "--"}ms");
	}

	private SpeedEstimate PreferDirectGnssSpeed(SpeedEstimate fallback)
	{
		lock (_gpsStateLock)
		{
			if (!_gnssSpeedService.IsRunning || !double.IsFinite(_lastDirectGnssSpeedKmh))
			{
				return fallback;
			}
			double speed = Math.Max(0.0, _lastDirectGnssSpeedKmh);
			bool stationary = fallback.IsStationary && speed <= 0.5;
			return new SpeedEstimate(speed, speed, fallback.DisplacementSpeedKmh, stationary);
		}
	}

	private void ProcessLocation(Location rawLocation)
	{
		lock (_gpsStateLock)
		{
			ProcessLocationCore(rawLocation);
		}
	}

	private void ProcessLocationCore(Location rawLocation)
	{
		GpsFilterConfig filter = GetGpsFilterConfig();
		DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
		DateTimeOffset previousObservedAt = _lastGpsObservedAt;
		_lastGpsObservedAt = receivedAt;
		_gpsRawFixCount++;
		if (previousObservedAt != default(DateTimeOffset))
		{
			_lastRawFixIntervalMilliseconds = Math.Clamp((receivedAt - previousObservedAt).TotalMilliseconds, 0.0, 60000.0);
		}
		if (rawLocation.Timestamp == default(DateTimeOffset))
		{
			rawLocation.Timestamp = receivedAt;
		}
		TimeSpan fixAge = receivedAt - rawLocation.Timestamp;
		if (fixAge < TimeSpan.Zero)
		{
			fixAge = TimeSpan.Zero;
		}
		double accuracy = rawLocation.Accuracy ?? double.MaxValue;
		if (fixAge > filter.MaximumAge)
		{
			RejectGpsFix("posición antigua");
			return;
		}
		if (rawLocation.IsFromMockProvider)
		{
			RejectGpsFix("ubicación simulada");
			return;
		}
		double? speed = rawLocation.Speed;
		double rawReportedSpeedKmh = (_lastRawReportedSpeedKmh = ((speed.HasValue && speed.GetValueOrDefault() >= 0.0) ? (rawLocation.Speed.Value * 3.6) : double.NaN));
		bool weakMovingFix = !rawLocation.ReducedAccuracy && accuracy <= filter.MaxWeakAccuracyMeters && fixAge <= TimeSpan.FromSeconds(2.5) && double.IsFinite(rawReportedSpeedKmh) && rawReportedSpeedKmh >= 3.0 && rawReportedSpeedKmh <= filter.MaxVehicleSpeedKmh + 25.0;
		bool acceptedWithWeakAccuracy = accuracy > filter.MaxAccuracyMeters && weakMovingFix;
		if (rawLocation.ReducedAccuracy)
		{
			RejectGpsFix($"precisión reducida ±{accuracy:F0} m");
			return;
		}
		if (accuracy > filter.MaxAccuracyMeters && !weakMovingFix)
		{
			if (!TryUseSpeedOnlyFix(rawReportedSpeedKmh, receivedAt, fixAge, accuracy, filter))
			{
				RejectGpsFix($"precisión débil ±{accuracy:F0} m");
			}
			return;
		}
		Location previousRaw = _lastRawLocation;
		if ((object)previousRaw != null && rawLocation.Timestamp <= previousRaw.Timestamp)
		{
			RejectGpsFix("lectura repetida");
			return;
		}
		bool resetAfterGap = (object)previousRaw == null && (object)_lastLocation != null;
		double seconds = 0.0;
		double rawSegmentMeters = 0.0;
		double displacementSpeedKmh = double.NaN;
		double previousAccuracy = (previousRaw?.Accuracy).GetValueOrDefault(accuracy);
		double adaptiveNoiseMeters = Math.Clamp((accuracy + previousAccuracy) * filter.AccuracyNoiseFactor, filter.MinimumMovementMeters, filter.MaximumNoiseMeters);
		if ((object)previousRaw != null)
		{
			seconds = (rawLocation.Timestamp - previousRaw.Timestamp).TotalSeconds;
			resetAfterGap = seconds > filter.ResetAfter.TotalSeconds;
			if (!resetAfterGap && seconds > 0.0)
			{
				rawSegmentMeters = Location.CalculateDistance(previousRaw, rawLocation, DistanceUnits.Kilometers) * 1000.0;
				displacementSpeedKmh = (_lastDisplacementSpeedKmh = rawSegmentMeters / seconds * 3.6);
				double accuracyAllowance = Math.Min(20.0, (accuracy + previousAccuracy) * 0.3);
				double maximumSegmentMeters = Math.Max(15.0, seconds * (filter.MaxVehicleSpeedKmh / 3.6) + accuracyAllowance);
				if (rawSegmentMeters > maximumSegmentMeters)
				{
					RejectGpsFix("salto descartado");
					return;
				}
			}
		}
		_lastGpsReceivedAt = receivedAt;
		_gpsAcceptedFixCount++;
		if (resetAfterGap)
		{
			_stationaryDurationSeconds = 0.0;
			_stationaryAnchorLocation = null;
			_filteredAltitudeMeters = null;
			_latestAltitudeMeters = null;
			_altitudeStableFixes = 0;
			_lastAltitudeTimestamp = default(DateTimeOffset);
			previousRaw = null;
			seconds = 0.0;
			rawSegmentMeters = 0.0;
			displacementSpeedKmh = double.NaN;
		}
		_lastRawLocation = new Location(rawLocation);
		_initialValidFixCount++;
		if ((object)_lastLocation == null && _initialValidFixCount < filter.InitialFixes)
		{
			_latestLocation = new Location(rawLocation);
			MainThread.BeginInvokeOnMainThread(delegate
			{
				GpsLabel.Text = $"GPS: estabilizando {_initialValidFixCount}/{filter.InitialFixes}";
			});
			UpdateTelemetryState();
			return;
		}
		SpeedEstimate estimate = ResolveSpeedEstimate(rawLocation, previousRaw, seconds, rawSegmentMeters, displacementSpeedKmh, adaptiveNoiseMeters, resetAfterGap, fixAge, filter);
		estimate = PreferDirectGnssSpeed(estimate);
		Debug.WriteLine($"RutaCam SPEED FIX43 · raw={rawReportedSpeedKmh:F1} · disp={displacementSpeedKmh:F1} · hud={estimate.FinalSpeedKmh:F1} · interval={_lastRawFixIntervalMilliseconds:F0}ms · age={fixAge.TotalMilliseconds:F0}ms · state={_gpsMotionState} · source={_lastSpeedSource}");
		Location location = BuildResponsiveLocation(rawLocation, estimate.FinalSpeedKmh, estimate.IsStationary, resetAfterGap);
		double? altitude = ResolveReliableAltitude(rawLocation, resetAfterGap, filter);
		_latestAltitudeMeters = altitude;
		double previousDisplayedSpeed = _currentSpeedKmh;
		bool addDistance = false;
		double segmentKm = 0.0;
		if ((object)_lastLocation != null)
		{
			double filteredSeconds = Math.Max(0.1, (location.Timestamp - _lastLocation.Timestamp).TotalSeconds);
			segmentKm = Location.CalculateDistance(_lastLocation, location, DistanceUnits.Kilometers);
			double segmentMeters = segmentKm * 1000.0;
			double filteredDisplacementSpeed = segmentMeters / filteredSeconds * 3.6;
			if (!resetAfterGap && filteredDisplacementSpeed > filter.MaxVehicleSpeedKmh)
			{
				RejectGpsFix("velocidad imposible");
				return;
			}
			addDistance = !resetAfterGap && !estimate.IsStationary && (segmentMeters >= Math.Max(1.5, adaptiveNoiseMeters * 0.35) || estimate.FinalSpeedKmh >= 3.0);
		}
		_currentSpeedKmh = estimate.FinalSpeedKmh;
		_latestLocation = location;
		if (estimate.IsStationary)
		{
			_displaySpeedKmh = 0.0;
			if (previousDisplayedSpeed > 2.0 || (object)_lastLocation == null)
			{
				_lastLocation = location;
				AddRoutePoint(location, altitude, estimate, fixAge, rawLocation);
			}
			MainThread.BeginInvokeOnMainThread(delegate
			{
				SpeedLabel.Text = "0";
				GpsLabel.Text = BuildGpsStatusText(accuracy, fixAge, acceptedWithWeakAccuracy);
				CoordinatesLabel.Text = $"{location.Latitude:F5}, {location.Longitude:F5}";
				AltitudeLabel.Text = ((!altitude.HasValue) ? string.Empty : $"ALT {altitude:F0} m");
			});
			UpdateTelemetryState();
			return;
		}
		if (addDistance)
		{
			_distanceKm += segmentKm;
		}
		_maxSpeedKmh = Math.Max(_maxSpeedKmh, estimate.FinalSpeedKmh);
		_lastLocation = location;
		AddRoutePoint(location, altitude, estimate, fixAge, rawLocation);
		MainThread.BeginInvokeOnMainThread(delegate
		{
			SpeedLabel.Text = _displaySpeedKmh.ToString("F0");
			DistanceLabel.Text = $"{_distanceKm:F2} km";
			MaxSpeedLabel.Text = $"{_maxSpeedKmh:F0} km/h";
			GpsLabel.Text = BuildGpsStatusText(accuracy, fixAge, acceptedWithWeakAccuracy);
			CoordinatesLabel.Text = $"{location.Latitude:F5}, {location.Longitude:F5}";
			AltitudeLabel.Text = ((!altitude.HasValue) ? string.Empty : $"ALT {altitude:F0} m");
			_mapController.UpdateTrack(SnapshotPoints(), followCurrent: true);
			TryRefreshMapSnapshot();
		});
		UpdateTelemetryState();
	}

	private void AddRoutePoint(Location location, double? altitude, SpeedEstimate estimate, TimeSpan fixAge, Location rawLocation)
	{
		RoutePoint point = new RoutePoint
		{
			Latitude = location.Latitude,
			Longitude = location.Longitude,
			SpeedKmh = estimate.FinalSpeedKmh,
			ReportedSpeedKmh = estimate.ReportedSpeedKmh,
			DisplacementSpeedKmh = estimate.DisplacementSpeedKmh,
			AccuracyMeters = location.Accuracy,
			AltitudeMeters = altitude,
			VerticalAccuracyMeters = rawLocation.VerticalAccuracy,
			CourseDegrees = rawLocation.Course,
			FixAgeMilliseconds = Math.Max(0.0, fixAge.TotalMilliseconds),
			ReducedAccuracy = rawLocation.ReducedAccuracy,
			IsFromMockProvider = rawLocation.IsFromMockProvider,
			Timestamp = location.Timestamp
		};
		lock (_pointsStateLock)
		{
			_points.Add(point);
		}
		QueuePeriodicSessionCheckpoint(force: false);
	}

	private Location BuildResponsiveLocation(Location rawLocation, double speedKmh, bool stationary, bool resetAfterGap)
	{
		if ((object)_lastLocation == null || resetAfterGap)
		{
			return new Location(rawLocation);
		}
		if (stationary)
		{
			return CopyLocation(rawLocation, _lastLocation.Latitude, _lastLocation.Longitude);
		}
		double seconds = Math.Clamp((rawLocation.Timestamp - _lastLocation.Timestamp).TotalSeconds, 0.2, 3.0);
		if (1 == 0)
		{
		}
		double num = ((speedKmh >= 25.0) ? 0.08 : ((speedKmh >= 10.0) ? 0.16 : ((!(speedKmh >= 4.0)) ? 0.55 : 0.3)));
		if (1 == 0)
		{
		}
		double timeConstant = num;
		double timeAlpha = 1.0 - Math.Exp((0.0 - seconds) / timeConstant);
		double accuracyFactor = Math.Clamp(14.0 / Math.Max(5.0, rawLocation.Accuracy ?? 14.0), 0.72, 1.0);
		double alpha = Math.Clamp(timeAlpha * accuracyFactor, 0.55, 1.0);
		double latitude = _lastLocation.Latitude + (rawLocation.Latitude - _lastLocation.Latitude) * alpha;
		double longitude = _lastLocation.Longitude + (rawLocation.Longitude - _lastLocation.Longitude) * alpha;
		return CopyLocation(rawLocation, latitude, longitude);
	}

	private static Location CopyLocation(Location source, double latitude, double longitude)
	{
		return new Location(source)
		{
			Latitude = latitude,
			Longitude = longitude
		};
	}

	private SpeedEstimate ResolveSpeedEstimate(Location rawLocation, Location? previousRaw, double seconds, double segmentMeters, double displacementSpeedKmh, double adaptiveNoiseMeters, bool resetAfterGap, TimeSpan fixAge, GpsFilterConfig filter)
	{
		double? speed = rawLocation.Speed;
		double reportedSpeedKmh = ((speed.HasValue && speed.GetValueOrDefault() >= 0.0) ? (rawLocation.Speed.Value * 3.6) : double.NaN);
		if (double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh > filter.MaxVehicleSpeedKmh + 25.0)
		{
			reportedSpeedKmh = double.NaN;
		}
		if ((object)previousRaw == null || resetAfterGap || seconds <= 0.0)
		{
			_stationaryDurationSeconds = 0.0;
			_stationaryAnchorLocation = null;
			if (double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 8.0)
			{
				MarkGpsMoving(reportedSpeedKmh, rawLocation.Timestamp);
				return new SpeedEstimate(Math.Clamp(reportedSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh), reportedSpeedKmh, null, IsStationary: false);
			}
			if (_gpsMotionState == GpsMotionState.Stationary && double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 4.0)
			{
				_movingCandidateFixes++;
				if (_movingCandidateFixes >= 2)
				{
					MarkGpsMoving(reportedSpeedKmh, rawLocation.Timestamp);
					return new SpeedEstimate(Math.Clamp(reportedSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh), reportedSpeedKmh, null, IsStationary: false);
				}
				return new SpeedEstimate(0.0, reportedSpeedKmh, null, IsStationary: true);
			}
			if (_gpsMotionState == GpsMotionState.Moving && resetAfterGap)
			{
				double recovered = ((double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 3.0) ? Math.Clamp(reportedSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh) : Math.Max(2.5, _currentSpeedKmh * 0.85));
				return new SpeedEstimate(recovered, double.IsFinite(reportedSpeedKmh) ? new double?(reportedSpeedKmh) : ((double?)null), null, IsStationary: false);
			}
			_stationaryCandidateFixes++;
			if (_stationaryCandidateFixes >= 2)
			{
				_gpsMotionState = GpsMotionState.Stationary;
			}
			return new SpeedEstimate(0.0, double.IsFinite(reportedSpeedKmh) ? new double?(reportedSpeedKmh) : ((double?)null), null, _gpsMotionState == GpsMotionState.Stationary);
		}
		bool lowMovement = segmentMeters <= adaptiveNoiseMeters && (!double.IsFinite(displacementSpeedKmh) || displacementSpeedKmh < 7.0);
		if (lowMovement)
		{
			if ((object)_stationaryAnchorLocation == null)
			{
				_stationaryAnchorLocation = new Location(previousRaw);
			}
			_stationaryDurationSeconds += Math.Min(seconds, 3.0);
		}
		else
		{
			_stationaryAnchorLocation = null;
			_stationaryDurationSeconds = 0.0;
		}
		double anchorDistanceMeters = (((object)_stationaryAnchorLocation == null) ? double.MaxValue : (Location.CalculateDistance(_stationaryAnchorLocation, rawLocation, DistanceUnits.Kilometers) * 1000.0));
		double stationaryOriginDistanceMeters = (((object)_stationaryConfirmedLocation == null) ? 0.0 : (Location.CalculateDistance(_stationaryConfirmedLocation, rawLocation, DistanceUnits.Kilometers) * 1000.0));
		if (_gpsMotionState == GpsMotionState.Stationary && double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 8.0 && fixAge <= TimeSpan.FromSeconds(2.0))
		{
			double immediate = Math.Clamp(reportedSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh);
			_lastSpeedSource = "GNSS DIRECTO";
			MarkGpsMoving(immediate, rawLocation.Timestamp);
			_stationaryConfirmedLocation = null;
			_stationaryDurationSeconds = 0.0;
			_stationaryAnchorLocation = null;
			return new SpeedEstimate(immediate, reportedSpeedKmh, double.IsFinite(displacementSpeedKmh) ? new double?(displacementSpeedKmh) : ((double?)null), IsStationary: false);
		}
		double stopSpeedThreshold = ((_gpsMotionState == GpsMotionState.Moving) ? 4.0 : 7.0);
		bool stationaryExitStrong = _gpsMotionState == GpsMotionState.Stationary && ((double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 8.0) || (double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 8.0 && segmentMeters > Math.Max(1.5, adaptiveNoiseMeters * 0.55)) || ((object)_stationaryAnchorLocation != null && anchorDistanceMeters >= Math.Max(6.0, adaptiveNoiseMeters * 1.35)) || ((object)_stationaryConfirmedLocation != null && stationaryOriginDistanceMeters >= Math.Max(7.0, adaptiveNoiseMeters * 1.2)));
		bool stationaryExitModerate = _gpsMotionState == GpsMotionState.Stationary && !stationaryExitStrong && ((double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 4.0 && reportedSpeedKmh < 8.0) || (double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 4.0 && segmentMeters > Math.Max(1.25, adaptiveNoiseMeters * 0.45)));
		if (stationaryExitStrong)
		{
			_movingCandidateFixes = 2;
			double exitSpeed = ((double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 4.0) ? reportedSpeedKmh : ((double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 4.0) ? displacementSpeedKmh : 4.0));
			MarkGpsMoving(exitSpeed, rawLocation.Timestamp);
			_stationaryDurationSeconds = 0.0;
			_stationaryAnchorLocation = null;
		}
		else if (stationaryExitModerate)
		{
			_movingCandidateFixes++;
			if (_movingCandidateFixes >= 2)
			{
				double exitSpeed2 = ((double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 4.0) ? reportedSpeedKmh : ((double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 4.0) ? displacementSpeedKmh : 4.0));
				MarkGpsMoving(exitSpeed2, rawLocation.Timestamp);
				_stationaryDurationSeconds = 0.0;
				_stationaryAnchorLocation = null;
			}
		}
		bool stopEvidence = !stationaryExitStrong && !stationaryExitModerate && lowMovement && anchorDistanceMeters <= Math.Max(4.0, adaptiveNoiseMeters) && (!double.IsFinite(reportedSpeedKmh) || reportedSpeedKmh <= stopSpeedThreshold);
		if (stopEvidence)
		{
			_stationaryCandidateFixes++;
			if (_gpsMotionState != GpsMotionState.Stationary)
			{
				_movingCandidateFixes = 0;
			}
		}
		else
		{
			_stationaryCandidateFixes = 0;
		}
		bool strongMovingEvidence = (double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 10.0) || (double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 10.0 && segmentMeters > adaptiveNoiseMeters);
		bool moderateMovingEvidence = (double.IsFinite(reportedSpeedKmh) && reportedSpeedKmh >= 5.0) || (double.IsFinite(displacementSpeedKmh) && displacementSpeedKmh >= 5.0 && segmentMeters > Math.Max(1.5, adaptiveNoiseMeters * 0.7));
		if (!stationaryExitStrong && !stationaryExitModerate)
		{
			if (strongMovingEvidence)
			{
				_movingCandidateFixes = 2;
				MarkGpsMoving(double.IsFinite(reportedSpeedKmh) ? reportedSpeedKmh : displacementSpeedKmh, rawLocation.Timestamp);
			}
			else if (moderateMovingEvidence)
			{
				_movingCandidateFixes++;
				if (_movingCandidateFixes >= 2)
				{
					MarkGpsMoving(double.IsFinite(reportedSpeedKmh) ? reportedSpeedKmh : displacementSpeedKmh, rawLocation.Timestamp);
				}
			}
			else if (!stopEvidence)
			{
				_movingCandidateFixes = 0;
			}
		}
		int requiredStopFixes = ((_gpsMotionState == GpsMotionState.Moving) ? 3 : 2);
		double requiredStopSeconds = ((_gpsMotionState == GpsMotionState.Moving) ? Math.Max(2.2, filter.StationaryConfirmation.TotalSeconds) : Math.Min(1.5, filter.StationaryConfirmation.TotalSeconds));
		if (stopEvidence && _stationaryCandidateFixes >= requiredStopFixes && _stationaryDurationSeconds >= requiredStopSeconds)
		{
			_gpsMotionState = GpsMotionState.Stationary;
			_stationaryCandidateFixes = requiredStopFixes;
			_movingCandidateFixes = 0;
			_lastTrustedMovingSpeedKmh = 0.0;
			_lastTrustedSpeedAt = rawLocation.Timestamp;
			_stationaryConfirmedLocation = new Location(rawLocation);
			_lastSpeedSource = "DETENIDO";
			return new SpeedEstimate(0.0, double.IsFinite(reportedSpeedKmh) ? new double?(reportedSpeedKmh) : ((double?)null), double.IsFinite(displacementSpeedKmh) ? new double?(displacementSpeedKmh) : ((double?)null), IsStationary: true);
		}
		bool displacementReliable = double.IsFinite(displacementSpeedKmh) && seconds <= 4.0 && segmentMeters >= Math.Max(2.0, adaptiveNoiseMeters * 0.6);
		double effectiveReportedSpeedKmh = reportedSpeedKmh;
		if (lowMovement && anchorDistanceMeters <= Math.Max(4.0, adaptiveNoiseMeters) && double.IsFinite(effectiveReportedSpeedKmh) && effectiveReportedSpeedKmh > 10.0)
		{
			double retention = Math.Clamp(1.0 - _stationaryDurationSeconds / 4.5, 0.0, 1.0);
			effectiveReportedSpeedKmh *= retention;
		}
		double candidate;
		if (double.IsFinite(effectiveReportedSpeedKmh) && fixAge <= TimeSpan.FromSeconds(2.0) && effectiveReportedSpeedKmh >= 3.0)
		{
			if (displacementReliable)
			{
				candidate = effectiveReportedSpeedKmh * 0.95 + displacementSpeedKmh * 0.05;
				_lastSpeedSource = "GNSS 95%";
			}
			else
			{
				candidate = effectiveReportedSpeedKmh;
				_lastSpeedSource = "GNSS";
			}
		}
		else if (!double.IsFinite(effectiveReportedSpeedKmh))
		{
			if (displacementReliable)
			{
				candidate = displacementSpeedKmh;
				_lastSpeedSource = "POSICIÓN";
			}
			else if (_gpsMotionState == GpsMotionState.Moving)
			{
				candidate = Math.Max(2.5, Math.Max(_currentSpeedKmh, _lastTrustedMovingSpeedKmh) * 0.92);
				_lastSpeedSource = "HOLD";
			}
			else
			{
				candidate = 0.0;
				_lastSpeedSource = "SIN SPEED";
			}
		}
		else if (displacementReliable && effectiveReportedSpeedKmh < 3.0 && displacementSpeedKmh >= 4.0)
		{
			candidate = displacementSpeedKmh;
			_lastSpeedSource = "POSICIÓN RESPALDO";
		}
		else
		{
			candidate = Math.Max(0.0, effectiveReportedSpeedKmh);
			_lastSpeedSource = "GNSS BAJO";
		}
		candidate = Math.Clamp(candidate, 0.0, filter.MaxVehicleSpeedKmh);
		double finalSpeed = ApplySpeedResponse(candidate, seconds, filter);
		if (finalSpeed >= 4.0)
		{
			MarkGpsMoving(finalSpeed, rawLocation.Timestamp);
		}
		return new SpeedEstimate(finalSpeed, double.IsFinite(reportedSpeedKmh) ? new double?(reportedSpeedKmh) : ((double?)null), double.IsFinite(displacementSpeedKmh) ? new double?(displacementSpeedKmh) : ((double?)null), IsStationary: false);
	}

	private double ApplySpeedResponse(double candidateSpeedKmh, double seconds, GpsFilterConfig filter)
	{
		double dt = Math.Clamp(seconds, 0.2, 3.0);
		if (candidateSpeedKmh < 1.2)
		{
			if (_gpsMotionState == GpsMotionState.Moving && _currentSpeedKmh > 2.0)
			{
				return Math.Max(2.0, _currentSpeedKmh - filter.MaxDecelerationKmhPerSecond * dt);
			}
			return 0.0;
		}
		if (_currentSpeedKmh <= 0.0)
		{
			return candidateSpeedKmh;
		}
		double minimum = Math.Max(0.0, _currentSpeedKmh - filter.MaxDecelerationKmhPerSecond * dt);
		double maximum = _currentSpeedKmh + filter.MaxAccelerationKmhPerSecond * dt;
		double bounded = Math.Clamp(candidateSpeedKmh, minimum, maximum);
		double delta = bounded - _currentSpeedKmh;
		if (1 == 0)
		{
		}
		double num = ((delta <= -12.0) ? 0.08 : ((delta < 0.0) ? 0.11 : ((!(delta >= 12.0)) ? 0.14 : 0.08)));
		if (1 == 0)
		{
		}
		double timeConstant = num;
		double alpha = 1.0 - Math.Exp((0.0 - dt) / timeConstant);
		double result = _currentSpeedKmh + delta * alpha;
		if (_gpsMotionState == GpsMotionState.Moving && result < 2.0 && candidateSpeedKmh > 0.0)
		{
			return 2.0;
		}
		return (result < 1.2) ? 0.0 : result;
	}

	private bool TryUseSpeedOnlyFix(double reportedSpeedKmh, DateTimeOffset receivedAt, TimeSpan fixAge, double accuracyMeters, GpsFilterConfig filter)
	{
		if (_gnssSpeedService.IsRunning && double.IsFinite(_lastDirectGnssSpeedKmh))
		{
			TimeSpan directAge = ((_lastDirectGnssSpeedAt == default(DateTimeOffset)) ? TimeSpan.MaxValue : (receivedAt - _lastDirectGnssSpeedAt));
			if (directAge >= TimeSpan.Zero && directAge <= TimeSpan.FromSeconds(2.5))
			{
				double directSpeed = (_displaySpeedKmh = (_currentSpeedKmh = Math.Max(0.0, _lastDirectGnssSpeedKmh)));
				_lastGpsReceivedAt = receivedAt;
				_lastRawReportedSpeedKmh = directSpeed;
				_lastSpeedSource = _lastDirectGnssProvider;
				_gpsAcceptedFixCount++;
				_gpsAcceptedSpeedOnlyFixCount++;
				if (directSpeed >= 4.0)
				{
					MarkGpsMoving(directSpeed, receivedAt);
				}
				MainThread.BeginInvokeOnMainThread(delegate
				{
					GpsLabel.Text = $"GPS: velocidad directa · posición ±{accuracyMeters:F0} m";
				});
				UpdateTelemetryState();
				return true;
			}
		}
		if (!double.IsFinite(reportedSpeedKmh) || fixAge > TimeSpan.FromSeconds(2.5) || reportedSpeedKmh < 3.0 || reportedSpeedKmh > filter.MaxVehicleSpeedKmh + 25.0)
		{
			return false;
		}
		if (_gpsMotionState == GpsMotionState.Stationary)
		{
			if (reportedSpeedKmh >= 8.0)
			{
				_movingCandidateFixes = 2;
			}
			else
			{
				if (!(reportedSpeedKmh >= 4.0))
				{
					_movingCandidateFixes = 0;
					return false;
				}
				_movingCandidateFixes++;
				if (_movingCandidateFixes < 2)
				{
					return false;
				}
			}
		}
		double dt = ((_lastTrustedSpeedAt == default(DateTimeOffset)) ? 0.5 : Math.Clamp((receivedAt - _lastTrustedSpeedAt).TotalSeconds, 0.2, 2.0));
		double candidate = Math.Clamp(reportedSpeedKmh, 0.0, filter.MaxVehicleSpeedKmh);
		double speed = ((_currentSpeedKmh <= 0.0) ? candidate : ApplySpeedResponse(candidate, dt, filter));
		_lastSpeedSource = "GNSS SPEED-ONLY";
		if (speed < 4.0 && _gpsMotionState != GpsMotionState.Moving)
		{
			return false;
		}
		_currentSpeedKmh = speed;
		_lastGpsReceivedAt = receivedAt;
		_lastTrustedMovingSpeedKmh = Math.Max(4.0, speed);
		_lastTrustedSpeedAt = receivedAt;
		_gpsMotionState = GpsMotionState.Moving;
		_movingCandidateFixes = Math.Max(_movingCandidateFixes, 2);
		_stationaryCandidateFixes = 0;
		_stationaryDurationSeconds = 0.0;
		_stationaryAnchorLocation = null;
		_gpsAcceptedFixCount++;
		_gpsAcceptedSpeedOnlyFixCount++;
		MainThread.BeginInvokeOnMainThread(delegate
		{
			GpsLabel.Text = $"GPS: velocidad válida · posición ±{accuracyMeters:F0} m";
		});
		return true;
	}

	private void MarkGpsMoving(double speedKmh, DateTimeOffset timestamp)
	{
		if (double.IsFinite(speedKmh) && !(speedKmh < 4.0))
		{
			_gpsMotionState = GpsMotionState.Moving;
			_stationaryCandidateFixes = 0;
			_stationaryConfirmedLocation = null;
			_lastTrustedMovingSpeedKmh = Math.Max(_lastTrustedMovingSpeedKmh * 0.35, speedKmh);
			_lastTrustedSpeedAt = ((timestamp == default(DateTimeOffset)) ? DateTimeOffset.UtcNow : timestamp);
		}
	}

	private double? ResolveReliableAltitude(Location rawLocation, bool resetAfterGap, GpsFilterConfig filter)
	{
		if (!rawLocation.Altitude.HasValue || !rawLocation.VerticalAccuracy.HasValue || rawLocation.VerticalAccuracy <= 0.0 || rawLocation.VerticalAccuracy > filter.MaxVerticalAccuracyMeters)
		{
			_altitudeStableFixes = 0;
			return null;
		}
		double rawAltitude = rawLocation.Altitude.Value;
		if (!resetAfterGap)
		{
			double? filteredAltitudeMeters = _filteredAltitudeMeters;
			if (filteredAltitudeMeters.HasValue && !(_lastAltitudeTimestamp == default(DateTimeOffset)))
			{
				double seconds = (rawLocation.Timestamp - _lastAltitudeTimestamp).TotalSeconds;
				if (seconds <= 0.0 || seconds > filter.ResetAfter.TotalSeconds)
				{
					_filteredAltitudeMeters = rawAltitude;
					_lastAltitudeTimestamp = rawLocation.Timestamp;
					_altitudeStableFixes = 1;
					return null;
				}
				double maximumChange = 6.0 + seconds * 2.5;
				if (Math.Abs(rawAltitude - _filteredAltitudeMeters.Value) > maximumChange)
				{
					_filteredAltitudeMeters = rawAltitude;
					_lastAltitudeTimestamp = rawLocation.Timestamp;
					_altitudeStableFixes = 1;
					return null;
				}
				double alpha = 1.0 - Math.Exp((0.0 - seconds) / 4.0);
				_filteredAltitudeMeters += (rawAltitude - _filteredAltitudeMeters.Value) * alpha;
				_lastAltitudeTimestamp = rawLocation.Timestamp;
				_altitudeStableFixes++;
				return (_altitudeStableFixes >= 3) ? _filteredAltitudeMeters : ((double?)null);
			}
		}
		_filteredAltitudeMeters = rawAltitude;
		_lastAltitudeTimestamp = rawLocation.Timestamp;
		_altitudeStableFixes = 1;
		return null;
	}

	private void TryRefreshMapSnapshot()
	{
		if (CameraXHudShowsMap() && _isRecording && IsCameraXOverlayMode())
		{
			try
			{
				CameraXMapState liveState = _mapController.CaptureCameraXMapState(SnapshotPoints(), includeBaseSnapshot: false);
				_cameraXOverlayRecorder.UpdateMapState(liveState);
			}
			catch
			{
			}
			_mapSnapshotRefreshCts?.Cancel();
			_mapSnapshotRefreshCts?.Dispose();
			_mapSnapshotRefreshCts = new CancellationTokenSource();
			RefreshCameraXBaseMapAfterRenderAsync(_mapSnapshotRefreshCts.Token);
		}
	}

	private async Task RefreshCameraXBaseMapAfterRenderAsync(CancellationToken token)
	{
		try
		{
			await Task.Delay(180, token);
			await MainThread.InvokeOnMainThreadAsync(delegate
			{
				if (!token.IsCancellationRequested && _isRecording && IsCameraXOverlayMode())
				{
					CameraXMapState cameraXMapState = _mapController.CaptureCameraXMapState(SnapshotPoints(), includeBaseSnapshot: true);
					byte[] baseMapPng = cameraXMapState.BaseMapPng;
					if (baseMapPng != null && baseMapPng.Length > 0)
					{
						_telemetryState.SetMapSnapshot(cameraXMapState.BaseMapPng);
					}
					_cameraXOverlayRecorder.UpdateMapState(cameraXMapState);
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}

	private void UpdateTelemetryState(TimeSpan? elapsedOverride = null)
	{
		TimeSpan elapsed = elapsedOverride ?? ((_startedAt == default(DateTimeOffset)) ? TimeSpan.Zero : (DateTimeOffset.UtcNow - _startedAt));
		_telemetryState.Update(_displaySpeedKmh, _distanceKm, elapsed, _latestLocation?.Latitude, _latestLocation?.Longitude, _latestAltitudeMeters, _isRecording);
		if (_isRecording && IsCameraXOverlayMode())
		{
			_cameraXOverlayRecorder.UpdateHud(BuildCameraXHudSnapshot(elapsed, true));
		}
	}

	private void StartHealthMonitor()
	{
		_healthMonitorCts?.Cancel();
		_healthMonitorCts?.Dispose();
		_healthMonitorCts = new CancellationTokenSource();
		_healthMonitorTask = MonitorRecordingHealthAsync(_healthMonitorCts.Token);
	}

	private async Task MonitorRecordingHealthAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested && _isRecording)
		{
			long availableBytes = _deviceHealth.GetAvailableStorageBytes();
			Interlocked.Exchange(ref _lastAvailableStorageBytes, availableBytes);
			UpdateSystemHealthUi(availableBytes);
			if (IsCameraXOverlayMode())
			{
				UpdateTelemetryState();
			}
			if (_overlaySettings.RecordAudio && !string.IsNullOrWhiteSpace(_overlaySettings.AudioInputDeviceId) && _overlaySettings.AudioInputDeviceId.StartsWith("device|", StringComparison.OrdinalIgnoreCase))
			{
				AudioRouteResult route = await _audioInputService.EnsureSelectedRouteAsync(_overlaySettings.AudioInputDeviceId, token);
				_activeAudioInputName = route.ActiveDisplayName;
				if (route.UsedFallback && !_audioFallbackAnnounced)
				{
					_audioFallbackAnnounced = true;
					MainThread.BeginInvokeOnMainThread(delegate
					{
						RecordStatusLabel.Text = "AUDIO → TELÉFONO";
					});
				}
			}
			double freeMb = DeviceHealthService.BytesToMb(availableBytes);
			if (_overlaySettings.AutoStopOnCriticalStorage && freeMb >= 0.0 && freeMb <= _overlaySettings.CriticalFreeStorageMb && !_criticalStorageStopRequested)
			{
				_criticalStorageStopRequested = true;
				MainThread.BeginInvokeOnMainThread(async delegate
				{
					if (_isRecording)
					{
						RecordStatusLabel.Text = "ESPACIO CRÍTICO";
						await StopRecordingAsync(saveRecord: true);
					}
				});
				break;
			}
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(3L), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void UpdateSystemHealthUi(long? knownAvailableBytes = null)
	{
		if (_overlaySettings.ShowBatteryStatus || _overlaySettings.ShowStorageStatus)
		{
			string battery = _deviceHealth.FormatBattery();
			string storage = DeviceHealthService.FormatStorage(knownAvailableBytes ?? _deviceHealth.GetAvailableStorageBytes());
			MainThread.BeginInvokeOnMainThread(delegate
			{
				BatteryLabel.Text = battery;
				StorageLabel.Text = storage;
			});
		}
	}

	private async Task<Location?> WaitForGpsFixAsync(CancellationToken cancellationToken)
	{
		GpsFilterConfig filter = GetGpsFilterConfig();
		using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(TimeSpan.FromSeconds(15L));
		while (!timeoutCts.IsCancellationRequested)
		{
			try
			{
				GeolocationRequest request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(4L));
				Location location = await Geolocation.Default.GetLocationAsync(request, timeoutCts.Token);
				if ((object)location != null && (location.Accuracy ?? double.MaxValue) <= filter.MaxAccuracyMeters)
				{
					return location;
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
			}
			try
			{
				await Task.Delay(500, timeoutCts.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
		return null;
	}

	private GpsFilterConfig GetGpsFilterConfig()
	{
		string gpsFilterProfile = _overlaySettings.GpsFilterProfile;
		if (1 == 0)
		{
		}
		GpsFilterConfig result = ((gpsFilterProfile == "Preciso") ? new GpsFilterConfig(20.0, 45.0, 2, 1.5, 10.0, 0.24, 135.0, TimeSpan.FromSeconds(4L), TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(7L), TimeSpan.FromSeconds(12L), TimeSpan.FromSeconds(2.8), 55.0, 65.0, 15.0) : ((!(gpsFilterProfile == "Flexible")) ? new GpsFilterConfig(35.0, 70.0, 1, 1.2, 14.0, 0.22, 160.0, TimeSpan.FromSeconds(6L), TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(8L), TimeSpan.FromSeconds(14L), TimeSpan.FromSeconds(3.0), 70.0, 80.0, 20.0) : new GpsFilterConfig(55.0, 100.0, 1, 1.0, 18.0, 0.2, 190.0, TimeSpan.FromSeconds(8L), TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(10L), TimeSpan.FromSeconds(16L), TimeSpan.FromSeconds(3.2), 85.0, 95.0, 25.0)));
		if (1 == 0)
		{
		}
		return result;
	}

	private void RejectGpsFix(string reason)
	{
		_gpsRejectedFixCount++;
		Debug.WriteLine($"RutaCam GPS rechazado: {reason} · raw={_gpsRawFixCount} · aceptados={_gpsAcceptedFixCount} · speed-only={_gpsAcceptedSpeedOnlyFixCount} · rechazados={_gpsRejectedFixCount} · estado={_gpsMotionState}");
		MainThread.BeginInvokeOnMainThread(delegate
		{
			GpsLabel.Text = "GPS: " + reason;
		});
	}

	private string BuildGpsStatusText(double accuracyMeters, TimeSpan fixAge, bool weakAccuracy = false)
	{
		string quality = (weakAccuracy ? "GPS usable" : "GPS activo");
		string interval = ((_lastRawFixIntervalMilliseconds > 0.0) ? $" · cada {_lastRawFixIntervalMilliseconds / 1000.0:F1} s" : string.Empty);
		string raw = (double.IsFinite(_lastRawReportedSpeedKmh) ? $"{_lastRawReportedSpeedKmh:F0}" : "--");
		string hud = $"{_currentSpeedKmh:F0}";
		return $"{quality} ±{accuracyMeters:F0} m{interval} · RAW {raw}/HUD {hud} · {_lastSpeedSource} · edad {Math.Max(0.0, fixAge.TotalSeconds):F1} s";
	}

	private void ApplyPreferredCameraSelection()
	{
		if (_availableCameras.Count != 0)
		{
			CameraPosition desired = ((!string.Equals(_overlaySettings.DefaultCameraPosition, "Frontal", StringComparison.OrdinalIgnoreCase)) ? CameraPosition.Rear : CameraPosition.Front);
			CameraInfo preferred = _availableCameras.FirstOrDefault((CameraInfo camera) => camera.Position == desired) ?? _availableCameras.First();
			CameraView.SelectedCamera = preferred;
		}
	}

	private async Task RefreshCameraSelectionFromSettingsAsync()
	{
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5L));
			if (_availableCameras.Count == 0)
			{
				_availableCameras = await CameraView.GetAvailableCameras(cts.Token);
			}
			if (_availableCameras.Count == 0)
			{
				return;
			}
			string previousId = CameraView.SelectedCamera?.DeviceId;
			ApplyPreferredCameraSelection();
			if (!string.Equals(previousId, CameraView.SelectedCamera?.DeviceId, StringComparison.Ordinal))
			{
				CameraView.IsTorchOn = false;
				CameraView.StopCameraPreview();
				await CameraView.StartCameraPreview(cts.Token);
				ApplyCameraPreviewMode();
			}
			ApplyZoomFactor(_overlaySettings.DefaultZoomFactor);
		}
		catch
		{
		}
	}

	private void ApplyZoomFactor(double requestedZoom)
	{
		CameraInfo selected = CameraView.SelectedCamera;
		if (selected != null)
		{
			_minimumZoomFactor = Math.Max(1f, selected.MinimumZoomFactor);
			_maximumZoomFactor = Math.Max(_minimumZoomFactor, selected.MaximumZoomFactor);
		}
		float zoom = (_currentZoomFactor = (float)Math.Clamp(requestedZoom, _minimumZoomFactor, _maximumZoomFactor));
		if (_isRecording && IsCameraXNativeRecorderMode())
		{
			_cameraXOverlayRecorder.SetZoom(zoom);
		}
		else if (selected != null)
		{
			CameraView.ZoomFactor = zoom;
		}
		ZoomSlider.Minimum = _minimumZoomFactor;
		ZoomSlider.Maximum = _maximumZoomFactor;
		if (Math.Abs(ZoomSlider.Value - (double)zoom) > 0.001)
		{
			ZoomSlider.Value = zoom;
		}
		ZoomValueLabel.Text = $"{zoom:F1}×";
		ZoomButton.Text = $"{zoom:F1}×";
	}

	private void RefreshCameraControlsUi()
	{
		bool idleAndEnabled = !_isRecording && _overlaySettings.ShowCameraControls;
		CameraTools.IsVisible = idleAndEnabled;
		if (!idleAndEnabled)
		{
			ZoomPanel.IsVisible = false;
		}
		SwitchCameraButton.IsEnabled = idleAndEnabled && _availableCameras.Count > 1;
		TorchButton.IsEnabled = idleAndEnabled && (CameraView.SelectedCamera?.IsFlashSupported ?? false);
		TorchButton.Text = (CameraView.IsTorchOn ? "☀ ON" : "☀");
		float currentZoom = ((_currentZoomFactor <= 0f) ? 1f : _currentZoomFactor);
		ZoomButton.Text = $"{currentZoom:F1}×";
		ZoomValueLabel.Text = $"{currentZoom:F1}×";
	}

	private async void OnSwitchCameraClicked(object sender, EventArgs e)
	{
		if (_isRecording || _availableCameras.Count < 2)
		{
			return;
		}
		try
		{
			CameraInfo current = CameraView.SelectedCamera;
			CameraInfo cameraInfo = current;
			CameraPosition targetPosition = ((cameraInfo == null || cameraInfo.Position != CameraPosition.Rear) ? CameraPosition.Rear : CameraPosition.Front);
			CameraInfo target = _availableCameras.FirstOrDefault((CameraInfo camera) => camera.Position == targetPosition) ?? _availableCameras.FirstOrDefault((CameraInfo camera) => !string.Equals(camera.DeviceId, current?.DeviceId, StringComparison.Ordinal));
			if (target == null)
			{
				return;
			}
			CameraView.IsTorchOn = false;
			CameraView.StopCameraPreview();
			CameraView.SelectedCamera = target;
			ApplyZoomFactor(1.0);
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5L));
			await CameraView.StartCameraPreview(cts.Token);
			ApplyCameraPreviewMode();
			RefreshCameraControlsUi();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Cámara", "No se pudo cambiar de cámara: " + ex.Message, "OK");
		}
	}

	private async void OnTorchClicked(object sender, EventArgs e)
	{
		if (_isRecording)
		{
			return;
		}
		CameraInfo? selectedCamera = CameraView.SelectedCamera;
		if (selectedCamera == null || !selectedCamera.IsFlashSupported)
		{
			await DisplayAlertAsync("Luz", "La cámara seleccionada no informa soporte para la linterna.", "OK");
			return;
		}
		try
		{
			CameraView.IsTorchOn = !CameraView.IsTorchOn;
			RefreshCameraControlsUi();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Luz", "No se pudo cambiar la linterna: " + ex.Message, "OK");
		}
	}

	private void OnZoomButtonClicked(object sender, EventArgs e)
	{
		if (!_isRecording)
		{
			ZoomPanel.IsVisible = !ZoomPanel.IsVisible;
		}
	}

	private void OnZoomSliderChanged(object sender, ValueChangedEventArgs e)
	{
		if (CameraView.SelectedCamera != null)
		{
			ApplyZoomFactor(e.NewValue);
		}
	}

	private void OnCameraPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
	{
		if (_isRecording || CameraView.SelectedCamera != null)
		{
			switch (e.Status)
			{
			case GestureStatus.Started:
				_pinchStartZoom = ((_currentZoomFactor <= 0f) ? 1f : _currentZoomFactor);
				break;
			case GestureStatus.Running:
				ApplyZoomFactor((double)_pinchStartZoom * Math.Max(0.1, e.Scale));
				break;
			}
		}
	}

	private void OnCameraDoubleTapped(object sender, TappedEventArgs e)
	{
		ApplyZoomFactor(1.0);
	}

	private async Task TryRecoverInterruptedSessionAsync()
	{
		InterruptedSessionRecoveryResult result = await _sessionJournal.RecoverInterruptedAsync(CancellationToken.None);
		if (result.Found)
		{
			if (!result.Recovered || result.Record == null)
			{
				_recoveryBlockedLegacyMigration = true;
				await DisplayAlertAsync("Recuperación pendiente", result.Message, "OK");
				return;
			}
			RouteRecord record = result.Record;
			string videoText = ((record.VideoSegmentCount > 0) ? $"{record.VideoSegmentCount} video(s) asociado(s)" : (record.HasAnyVideo ? "1 video asociado" : "sin video recuperable"));
			await DisplayAlertAsync("Sesión recuperada", $"RutaCam restauró un recorrido interrumpido con {record.Points.Count} puntos GPS y {videoText}. Puedes revisarlo en Historial.", "OK");
		}
	}

	private void QueuePeriodicSessionCheckpoint(bool force)
	{
		if (_activeRouteId == Guid.Empty)
		{
			return;
		}
		if (force)
		{
			Task.Run(async delegate
			{
				try
				{
					await SaveSessionCheckpointAsync("Recording", force: true, CancellationToken.None);
				}
				catch
				{
				}
			});
			return;
		}
		DateTimeOffset now = DateTimeOffset.UtcNow;
		int pointCount = GetPointCount();
		if ((now - _lastSessionCheckpointAt < TimeSpan.FromSeconds(10L) && pointCount - _lastCheckpointPointCount < 10) || Interlocked.CompareExchange(ref _periodicCheckpointInProgress, 1, 0) != 0)
		{
			return;
		}
		Task.Run(async delegate
		{
			try
			{
				await SaveSessionCheckpointAsync("Recording", force: false, CancellationToken.None);
			}
			catch
			{
			}
			finally
			{
				Interlocked.Exchange(ref _periodicCheckpointInProgress, 0);
			}
		});
	}

	private async Task SaveSessionCheckpointAsync(string state, bool force, CancellationToken cancellationToken)
	{
		await _sessionCheckpointGate.WaitAsync(cancellationToken);
		try
		{
			if (!(_activeRouteId == Guid.Empty) && !(_startedAt == default(DateTimeOffset)))
			{
				int pointCount = GetPointCount();
				DateTimeOffset now = DateTimeOffset.UtcNow;
				if (force || !(now - _lastSessionCheckpointAt < TimeSpan.FromSeconds(10L)) || pointCount - _lastCheckpointPointCount >= 10)
				{
					ActiveRecordingSession snapshot = BuildActiveSessionSnapshot(state);
					await _sessionJournal.SaveCheckpointAsync(snapshot, cancellationToken);
					_lastSessionCheckpointAt = DateTimeOffset.UtcNow;
					_lastCheckpointPointCount = pointCount;
				}
			}
		}
		finally
		{
			_sessionCheckpointGate.Release();
		}
	}

	private async Task CloseSessionJournalAsync()
	{
		await _sessionCheckpointGate.WaitAsync();
		try
		{
			_activeRouteId = Guid.Empty;
			await _sessionJournal.ClearAsync();
		}
		finally
		{
			_sessionCheckpointGate.Release();
		}
	}

	private ActiveRecordingSession BuildActiveSessionSnapshot(string state)
	{
		List<int> protectedIndices;
		lock (_segmentStateLock)
		{
			protectedIndices = _protectedSegmentIndices.OrderBy((int x) => x).ToList();
		}
		bool cameraX = IsCameraXNativeRecorderMode();
		ActiveRecordingSession obj = new ActiveRecordingSession
		{
			RouteId = _activeRouteId,
			StartedAt = _startedAt,
			UpdatedAt = DateTimeOffset.UtcNow,
			State = state,
			DistanceKm = _distanceKm,
			MaxSpeedKmh = _maxSpeedKmh,
			CurrentSpeedKmh = _currentSpeedKmh,
			RecordingEngine = (cameraX ? GetCameraXRecordingEngineLabel() : _overlaySettings.RecordingEngine)
		};
		object loopRecordingMode;
		if (cameraX)
		{
			TimeSpan? sessionLoopRetention = _sessionLoopRetention;
			if (sessionLoopRetention.HasValue)
			{
				loopRecordingMode = _overlaySettings.LoopRecording;
				goto IL_00f5;
			}
		}
		loopRecordingMode = "Desactivada";
		goto IL_00f5;
		IL_00f5:
		obj.LoopRecordingMode = (string)loopRecordingMode;
		obj.DeletedByLoopCount = _deletedByLoopCount;
		obj.UsesVideoSegments = cameraX;
		obj.SessionSegmentationEnabled = _sessionSegmentationEnabled;
		obj.CurrentVideoPath = _videoPath;
		obj.CurrentSegmentIndex = _segmentIndex;
		obj.CurrentSegmentStartedAt = _segmentStartedAt;
		obj.SegmentFileStem = _segmentFileStem;
		obj.ProtectedSegmentIndices = protectedIndices;
		obj.VideoSegments = SnapshotVideoSegments();
		obj.Points = SnapshotPoints();
		return obj;
	}

	private int GetPointCount()
	{
		lock (_pointsStateLock)
		{
			return _points.Count;
		}
	}

	private List<RoutePoint> SnapshotPoints()
	{
		lock (_pointsStateLock)
		{
			return _points.Select((RoutePoint x) => new RoutePoint
			{
				Latitude = x.Latitude,
				Longitude = x.Longitude,
				SpeedKmh = x.SpeedKmh,
				ReportedSpeedKmh = x.ReportedSpeedKmh,
				DisplacementSpeedKmh = x.DisplacementSpeedKmh,
				AccuracyMeters = x.AccuracyMeters,
				AltitudeMeters = x.AltitudeMeters,
				VerticalAccuracyMeters = x.VerticalAccuracyMeters,
				CourseDegrees = x.CourseDegrees,
				FixAgeMilliseconds = x.FixAgeMilliseconds,
				ReducedAccuracy = x.ReducedAccuracy,
				IsFromMockProvider = x.IsFromMockProvider,
				Timestamp = x.Timestamp
			}).ToList();
		}
	}

	private List<VideoSegment> SnapshotVideoSegments()
	{
		lock (_segmentStateLock)
		{
			return (from x in _videoSegments
				orderby x.Index
				select new VideoSegment
				{
					Index = x.Index,
					StartedAt = x.StartedAt,
					EndedAt = x.EndedAt,
					VideoPath = x.VideoPath,
					PublicVideoUri = x.PublicVideoUri,
					DisplayName = x.DisplayName,
					RelativePath = x.RelativePath,
					SizeBytes = x.SizeBytes,
					IsProtected = x.IsProtected,
					ProtectedAt = x.ProtectedAt
				}).ToList();
		}
	}

	private void ResetSession()
	{
		lock (_pointsStateLock)
		{
			_points.Clear();
		}
		_mapController.Clear();
		_lastLocation = null;
		_lastRawLocation = null;
		_latestLocation = null;
		_lastGpsObservedAt = default(DateTimeOffset);
		_lastGpsReceivedAt = default(DateTimeOffset);
		_lastSpeedVisualUpdateAt = default(DateTimeOffset);
		_lastRawFixIntervalMilliseconds = 0.0;
		_displaySpeedKmh = 0.0;
		_lastRawReportedSpeedKmh = double.NaN;
		_lastDisplacementSpeedKmh = double.NaN;
		_lastSpeedSource = "SIN DATOS";
		_lastDirectGnssSpeedAt = default(DateTimeOffset);
		_lastDirectGnssSpeedKmh = double.NaN;
		_lastDirectGnssIntervalMilliseconds = null;
		_lastDirectGnssProvider = "SIN DATOS";
		_stationaryConfirmedLocation = null;
		_gpsRawFixCount = 0;
		_gpsAcceptedFixCount = 0;
		_gpsAcceptedSpeedOnlyFixCount = 0;
		_gpsRejectedFixCount = 0;
		_initialValidFixCount = 0;
		_gpsMotionState = GpsMotionState.Unknown;
		_stationaryCandidateFixes = 0;
		_movingCandidateFixes = 0;
		_lastTrustedMovingSpeedKmh = 0.0;
		_lastTrustedSpeedAt = default(DateTimeOffset);
		_stationaryDurationSeconds = 0.0;
		_stationaryAnchorLocation = null;
		_filteredAltitudeMeters = null;
		_latestAltitudeMeters = null;
		_altitudeStableFixes = 0;
		_lastAltitudeTimestamp = default(DateTimeOffset);
		_distanceKm = 0.0;
		_maxSpeedKmh = 0.0;
		_currentSpeedKmh = 0.0;
		_mapSnapshotRefreshCts?.Cancel();
		_mapSnapshotRefreshCts?.Dispose();
		_mapSnapshotRefreshCts = null;
		_criticalStorageStopRequested = false;
		_videoPath = string.Empty;
		_videoSegments.Clear();
		_segmentPublishTasks.Clear();
		_segmentRotationCts?.Cancel();
		_segmentRotationCts?.Dispose();
		_segmentRotationCts = null;
		_segmentRotationTask = null;
		_segmentIndex = 0;
		_segmentStartedAt = default(DateTimeOffset);
		_segmentFileStem = string.Empty;
		_sessionSegmentationEnabled = false;
		_sessionLoopRetention = null;
		_deletedByLoopCount = 0;
		_activeRouteId = Guid.Empty;
		_lastSessionCheckpointAt = default(DateTimeOffset);
		_lastCheckpointPointCount = 0;
		Interlocked.Exchange(ref _periodicCheckpointInProgress, 0);
		lock (_segmentStateLock)
		{
			_protectedSegmentIndices.Clear();
		}
		ProtectClipButton.IsVisible = false;
		ProtectClipButton.Text = "\ud83d\udd12 GUARDAR CLIP";
		SpeedLabel.Text = "0";
		DistanceLabel.Text = "0.00 km";
		TimeLabel.Text = "00:00";
		AverageSpeedLabel.Text = "0 km/h";
		MaxSpeedLabel.Text = "0 km/h";
		GpsLabel.Text = "GPS: buscando";
		CoordinatesLabel.Text = string.Empty;
		AltitudeLabel.Text = string.Empty;
		UpdateSystemHealthUi();
		UpdateTelemetryState(TimeSpan.Zero);
	}

	private async Task SafeDisposeVideoAsync()
	{
		if (_videoStream != null)
		{
			try
			{
				await _videoStream.FlushAsync();
			}
			catch
			{
			}
			await _videoStream.DisposeAsync();
			_videoStream = null;
		}
	}

	private void RestoreIdleUi()
	{
		if (Platform.CurrentActivity is MainActivity activity)
		{
			activity.ExitRecordingPresentation();
		}
		DeviceDisplay.Current.KeepScreenOn = false;
		_healthMonitorCts?.Cancel();
		StartButton.IsEnabled = true;
		StopButton.IsEnabled = false;
		HistoryButton.IsEnabled = true;
		SettingsButton.IsEnabled = true;
		ActionBar.IsVisible = true;
		RecordDot.Color = Color.FromArgb("#9FB0C7");
		RecordStatusLabel.Text = "LISTO";
		ProtectClipButton.IsVisible = false;
		ProtectClipButton.Text = "\ud83d\udd12 GUARDAR CLIP";
		UpdateSystemHealthUi();
		RefreshCameraControlsUi();
	}

	private bool IsCameraXBetaEngine()
	{
		return _overlaySettings.RecordingEngine.StartsWith("CameraX", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsCameraXNativeRecorderMode()
	{
		return IsCameraXBetaEngine();
	}

	private bool IsCameraXOverlayMode()
	{
		return IsCameraXBetaEngine() && string.Equals(_overlaySettings.RecordingMode, "Con telemetría", StringComparison.OrdinalIgnoreCase) && !_overlaySettings.CameraXHudMode.StartsWith("Sin HUD", StringComparison.OrdinalIgnoreCase);
	}

	private bool CameraXHudShowsMap()
	{
		return IsCameraXOverlayMode() && !_overlaySettings.CameraXHudMode.StartsWith("Limpio", StringComparison.OrdinalIgnoreCase) && _overlaySettings.ShowMap;
	}

	private bool IsBurnInRecordingMode()
	{
		return !IsCameraXBetaEngine() && string.Equals(_overlaySettings.RecordingMode, "Con telemetría", StringComparison.OrdinalIgnoreCase);
	}

	private CameraXRecordingOptions BuildCameraXRecordingOptions()
	{
		int fps = (_overlaySettings.CameraXFrameRate.StartsWith("60", StringComparison.OrdinalIgnoreCase) ? 60 : 30);
		return new CameraXRecordingOptions
		{
			Resolution = (_overlaySettings.CameraXResolution ?? "1080p · FHD"),
			TargetFps = fps,
			Stabilization = (_overlaySettings.CameraXStabilization ?? "Perfil recomendado · 1080p/30"),
			RutaCamSoftwareStabilization = (_overlaySettings.RutaCamSoftwareStabilization ?? "Desactivada"),
			RutaCamGpuStabilization = (_overlaySettings.RutaCamGpuStabilization ?? "Desactivada"),
			PhysicalCameraId = string.Empty,
			LensDisplayName = "Automática · cámara trasera"
		};
	}

	private CameraXHudOptions BuildCameraXHudOptions()
	{
		string mode = _overlaySettings.CameraXHudMode ?? "Básico · incrustado";
		bool clean = mode.StartsWith("Limpio", StringComparison.OrdinalIgnoreCase);
		bool complete = mode.StartsWith("Completo", StringComparison.OrdinalIgnoreCase);
		return new CameraXHudOptions
		{
			Enabled = IsCameraXOverlayMode(),
			ShowLogo = _overlaySettings.ShowLogo,
			ShowMap = CameraXHudShowsMap(),
			ShowRec = false,
			ShowSpeed = (!clean && _overlaySettings.ShowSpeed),
			ShowDistance = (!clean && _overlaySettings.ShowDistance),
			ShowTimer = (!clean && _overlaySettings.ShowTimer),
			ShowAverageSpeed = (!clean && _overlaySettings.ShowAverageSpeed),
			ShowMaxSpeed = (!clean && _overlaySettings.ShowMaxSpeed),
			ShowGpsStatus = (!clean && _overlaySettings.ShowGpsStatus),
			ShowCoordinates = (!clean && _overlaySettings.ShowCoordinates),
			ShowAltitude = (!clean && _overlaySettings.ShowAltitude),
			ShowDateTime = (!clean && _overlaySettings.ShowDateTime),
			DateTimeCorner = _overlaySettings.DateTimeCorner,
			ShowBatteryStatus = false,
			ShowStorageStatus = false,
			LogoOpacity = _overlaySettings.LogoOpacity,
			LogoWidthDp = _overlaySettings.LogoWidth,
			CustomLogoPath = _overlaySettings.CustomLogoPath,
			MapOpacity = _overlaySettings.MapOpacity,
			MapWidthDp = _overlaySettings.MapWidth,
			MapCorner = _overlaySettings.MapCorner,
			MapShape = _overlaySettings.MapShape
		};
	}

	private CameraXHudSnapshot BuildCameraXHudSnapshot(TimeSpan? elapsedOverride = null, bool? recordingOverride = null)
	{
		TimeSpan elapsed = elapsedOverride ?? ((_startedAt == default(DateTimeOffset)) ? TimeSpan.Zero : (DateTimeOffset.UtcNow - _startedAt));
		double average = ((elapsed.TotalHours > 0.0) ? (_distanceKm / elapsed.TotalHours) : 0.0);
		Location location = _latestLocation;
		double? accuracy = location?.Accuracy;
		object obj;
		if (accuracy.HasValue)
		{
			double accuracyMeters = accuracy.GetValueOrDefault();
			if (double.IsFinite(accuracyMeters))
			{
				obj = $"GPS ±{accuracyMeters:F0} m";
				goto IL_00e9;
			}
		}
		obj = "GPS --";
		goto IL_00e9;
		IL_00e9:
		string gpsText = (string)obj;
		string coordinatesText = (((object)location == null) ? "COORD --" : (location.Latitude.ToString("F5", CultureInfo.InvariantCulture) + ", " + location.Longitude.ToString("F5", CultureInfo.InvariantCulture)));
		double? latestAltitudeMeters = _latestAltitudeMeters;
		object obj2;
		if (latestAltitudeMeters.HasValue)
		{
			double altitude = latestAltitudeMeters.GetValueOrDefault();
			obj2 = $"ALT {altitude:F0} m";
		}
		else
		{
			obj2 = "ALT --";
		}
		string altitudeText = (string)obj2;
		long cachedStorageBytes = Interlocked.Read(ref _lastAvailableStorageBytes);
		string storageText = DeviceHealthService.FormatStorage((cachedStorageBytes >= 0) ? cachedStorageBytes : _deviceHealth.GetAvailableStorageBytes());
		return new CameraXHudSnapshot(_displaySpeedKmh, _distanceKm, elapsed, average, _maxSpeedKmh, gpsText, coordinatesText, altitudeText, _deviceHealth.FormatBattery(), storageText, recordingOverride ?? _isRecording);
	}

	private string GetCameraXRecordingEngineLabel()
	{
		string mode = _overlaySettings.CameraXHudMode ?? string.Empty;
		string profile = _overlaySettings.CameraXResolution + " · " + _overlaySettings.CameraXFrameRate;
		string audioLabel = (_overlaySettings.RecordAudio ? (" · audio " + _activeAudioInputName) : " · sin audio");
		string stabilizationLabel = (string.IsNullOrWhiteSpace(_cameraXOverlayRecorder.LastStabilizationStatus) ? string.Empty : (" · " + _cameraXOverlayRecorder.LastStabilizationStatus));
		string effectiveSegmentation = _overlaySettings.VideoSegmentation;
		TimeSpan? sessionLoopRetention;
		if (_sessionSegmentationEnabled && effectiveSegmentation.Equals("No dividir", StringComparison.OrdinalIgnoreCase))
		{
			sessionLoopRetention = _sessionLoopRetention;
			if (sessionLoopRetention.HasValue)
			{
				effectiveSegmentation = ((_sessionLoopRetention <= TimeSpan.FromMinutes(2L)) ? "1 minuto (auto)" : "5 minutos (auto)");
			}
		}
		string segmentLabel = (_sessionSegmentationEnabled ? (" · clips " + effectiveSegmentation) : string.Empty);
		sessionLoopRetention = _sessionLoopRetention;
		string loopLabel = (sessionLoopRetention.HasValue ? (" · bucle " + _overlaySettings.LoopRecording) : string.Empty);
		segmentLabel += loopLabel;
		if (string.Equals(_overlaySettings.RecordingMode, "Con telemetría", StringComparison.OrdinalIgnoreCase) && !mode.StartsWith("Sin HUD", StringComparison.OrdinalIgnoreCase))
		{
			if (!mode.StartsWith("Completo", StringComparison.OrdinalIgnoreCase))
			{
				if (!mode.StartsWith("Limpio", StringComparison.OrdinalIgnoreCase))
				{
					return $"CameraX · {profile} · HUD básico{stabilizationLabel}{audioLabel}{segmentLabel}";
				}
				return $"CameraX · {profile} · HUD limpio{stabilizationLabel}{audioLabel}{segmentLabel}";
			}
			return $"CameraX · {profile} · HUD completo{stabilizationLabel}{audioLabel}{segmentLabel}";
		}
		return $"CameraX · {profile} · cámara limpia{stabilizationLabel}{audioLabel}{segmentLabel}";
	}

	private async Task RestoreCameraPreviewAfterCameraXAsync()
	{
		_cameraPreviewNeedsRestart = true;
		for (int attempt = 0; attempt < 2; attempt++)
		{
			try
			{
				CreateFreshCameraView();
				using CancellationTokenSource handlerCts = new CancellationTokenSource(TimeSpan.FromSeconds(7L));
				await WaitForFreshCameraHandlerAsync(handlerCts.Token);
				using CancellationTokenSource cameraCts = new CancellationTokenSource(TimeSpan.FromSeconds(8L));
				_availableCameras = await CameraView.GetAvailableCameras(cameraCts.Token);
				ApplyPreferredCameraSelection();
				await CameraView.StartCameraPreview(cameraCts.Token);
				ApplyCameraPreviewMode();
				ApplyZoomFactor(_overlaySettings.DefaultZoomFactor);
				RefreshCameraControlsUi();
				_cameraPreviewNeedsRestart = false;
				return;
			}
			catch
			{
				if (attempt == 0)
				{
					await Task.Delay(450);
				}
			}
		}
		_cameraPreviewNeedsRestart = true;
		RecordStatusLabel.Text = "REINTENTA ABRIR CÁMARA";
	}

	private async void OnRecordBadgeTapped(object sender, TappedEventArgs e)
	{
		if (_isRecording && (IsBurnInRecordingMode() || IsCameraXNativeRecorderMode()))
		{
			await StopRecordingAsync(saveRecord: true);
		}
	}

	private async void OnHistoryClicked(object sender, EventArgs e)
	{
		if (!_isRecording)
		{
			await base.Navigation.PushAsync(new HistoryPage(_storage, _videoLibrary, _routeExport));
		}
	}

	private async void OnSettingsClicked(object sender, EventArgs e)
	{
		if (!_isRecording)
		{
			try
			{
				OverlaySettingsPage settingsPage = new OverlaySettingsPage(_overlaySettingsService, _audioInputService, _cameraStabilizationService, _activationService);
				await base.Navigation.PushAsync(settingsPage);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"No se pudo abrir Configuración: {ex}");
				await DisplayAlertAsync("Configuración", "No se pudo abrir la pantalla de configuración.\n\n" + ex.Message, "OK");
			}
		}
	}

	private static string FormatDuration(TimeSpan value)
	{
		return (value.TotalHours >= 1.0) ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
	}

}
