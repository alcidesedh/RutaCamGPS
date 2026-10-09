using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace GPSCamRoute;

public partial class OverlaySettingsPage : ContentPage
{
	private readonly OverlaySettingsService _service;

	private readonly IAudioInputService _audioInputService;

	private readonly ICameraStabilizationService _cameraStabilizationService;

	private readonly ActivationService _activationService;

	private string _customLogoPath = string.Empty;

	private string _savedAudioInputId = "automatic";

	private bool _audioInputsLoaded;

	private string _latestCameraDiagnosticText = string.Empty;

	private CameraStabilizationCapabilities? _currentStabilizationCapabilities;

	private bool _stabilizationPickerInitialized;

	private const double LockedControlOpacity = 0.42;

	public OverlaySettingsPage(OverlaySettingsService service, IAudioInputService audioInputService, ICameraStabilizationService cameraStabilizationService, ActivationService activationService)
	{
		InitializeComponent();
		CreditsLabel.Text = "Creado por\nElvisMao 2026\nV " + AppInfo.Current.VersionString;
		_service = service;
		_audioInputService = audioInputService;
		_cameraStabilizationService = cameraStabilizationService;
		_activationService = activationService;
		AudioInputPicker.ItemDisplayBinding = new Binding("DisplayName");
		MapWidthSlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		MapOpacitySlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		LogoWidthSlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		LogoOpacitySlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		DefaultZoomSlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		MinimumStorageSlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		CriticalStorageSlider.ValueChanged += delegate
		{
			UpdateLabels();
		};
		LoadSettings();
		SelectSettingsSection("Video");
	}

	private void OnSettingsTabClicked(object sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: string sectionKey })
		{
			SelectSettingsSection(sectionKey);
		}
	}

	private void SelectSettingsSection(string sectionKey)
	{
		VideoSettingsSection.IsVisible = sectionKey == "Video";
		StabilizerSettingsSection.IsVisible = sectionKey == "Stabilizer";
		AudioSettingsSection.IsVisible = sectionKey == "Audio";
		CameraSettingsSection.IsVisible = sectionKey == "Camera";
		GpsSettingsSection.IsVisible = sectionKey == "Gps";
		SafetySettingsSection.IsVisible = sectionKey == "Safety";
		HudSettingsSection.IsVisible = sectionKey == "Hud";
		MapSettingsSection.IsVisible = sectionKey == "Map";
		LogoSettingsSection.IsVisible = sectionKey == "Logo";
		ActivationSettingsSection.IsVisible = sectionKey == "Activation";
		PrivacySettingsSection.IsVisible = sectionKey == "Privacy";
		(Button, string)[] tabs = new(Button, string)[11]
		{
			(VideoTabButton, "Video"),
			(StabilizerTabButton, "Stabilizer"),
			(AudioTabButton, "Audio"),
			(CameraTabButton, "Camera"),
			(GpsTabButton, "Gps"),
			(SafetyTabButton, "Safety"),
			(HudTabButton, "Hud"),
			(MapTabButton, "Map"),
			(LogoTabButton, "Logo"),
			(ActivationTabButton, "Activation"),
			(PrivacyTabButton, "Privacy")
		};
		(Button, string)[] array = tabs;
		for (int i = 0; i < array.Length; i++)
		{
			(Button, string) tab = array[i];
			bool selected = tab.Item2 == sectionKey;
			tab.Item1.BackgroundColor = Color.FromArgb(selected ? "#14B8A6" : "#132238");
			tab.Item1.TextColor = Color.FromArgb(selected ? "#04131B" : "#FFFFFF");
			tab.Item1.FontAttributes = (selected ? FontAttributes.Bold : FontAttributes.None);
		}
		Label currentSettingsSectionLabel = CurrentSettingsSectionLabel;
		if (1 == 0)
		{
		}
		string text = sectionKey switch
		{
			"Video" => "VIDEO Y GRABACIÓN", 
			"Stabilizer" => "ESTABILIZACIÓN GPU / CAMERAX", 
			"Audio" => "AUDIO", 
			"Camera" => "CÁMARA Y ENCUADRE", 
			"Gps" => "GPS Y DISTANCIA", 
			"Safety" => "SEGURIDAD DE GRABACIÓN", 
			"Hud" => "HUD / TELEMETRÍA", 
			"Map" => "MINI MAPA", 
			"Logo" => "LOGO PERSONAL", 
			"Activation" => "ACTIVACIÓN", 
			"Privacy" => "POLÍTICA DE PRIVACIDAD", 
			_ => "CONFIGURACIÓN", 
		};
		if (1 == 0)
		{
		}
		currentSettingsSectionLabel.Text = text;
	}

	private async void OnRefreshStabilizationClicked(object sender, EventArgs e)
	{
		await RefreshStabilizationCapabilitiesAsync();
	}

	private async Task RefreshStabilizationCapabilitiesAsync()
	{
		try
		{
			CameraStabilizationCapabilityLabel.Text = "Detectando Camera2 + CameraX...";
			CameraStabilizationCapabilityLabel.TextColor = Color.FromArgb("#CBD5E1");
			bool useFront = string.Equals(DefaultCameraPicker.SelectedItem?.ToString(), "Frontal", StringComparison.OrdinalIgnoreCase);
			CameraStabilizationCapabilities capabilities = (_currentStabilizationCapabilities = await _cameraStabilizationService.GetCapabilitiesAsync(useFront));
			CameraStabilizationCapabilityLabel.Text = "Capacidades detectadas. Usa VOLVER A DETECTAR si cambias de cámara.";
			CameraStabilizationCapabilityLabel.TextColor = (capabilities.IsAvailable ? Color.FromArgb("#9FB0C7") : Color.FromArgb("#9FB0C7"));
			ApplyCompatibleStabilizationModes(capabilities);
			ApplyRutaCamSoftwareStabilizerAvailability(capabilities);
			ApplyRutaCamGpuStabilizerAvailability(capabilities);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			CameraStabilizationCapabilityLabel.Text = "No se pudo leer la estabilización: " + ex2.Message;
			CameraStabilizationCapabilityLabel.TextColor = Color.FromArgb("#9FB0C7");
		}
	}

	private void ApplyRutaCamGpuStabilizerAvailability(CameraStabilizationCapabilities capabilities)
	{
		bool available = capabilities.RutaCamGpuStabilizerAvailable;
		RutaCamGpuStabilizationPicker.IsEnabled = available;
		RutaCamGpuStabilizationPicker.Opacity = (available ? 1.0 : 0.55);
		if (!available)
		{
			RutaCamGpuStabilizationPicker.SelectedItem = "Desactivada";
			List<string> reasons = new List<string>();
			if (!capabilities.GyroscopeAvailable)
			{
				reasons.Add("sin giroscopio");
			}
			if (!capabilities.OpenGlEs2Available)
			{
				reasons.Add("sin OpenGL ES 2");
			}
			RutaCamGpuStabilizationStatusLabel.Text = "No disponible en esta cámara" + ((reasons.Count == 0) ? "." : (" (" + string.Join(" · ", reasons) + ")."));
			RutaCamGpuStabilizationStatusLabel.TextColor = Color.FromArgb("#9FB0C7");
		}
		else
		{
			RutaCamGpuStabilizationStatusLabel.Text = "Disponible en esta cámara. GPU Equilibrada es el perfil recomendado.";
			RutaCamGpuStabilizationStatusLabel.TextColor = Color.FromArgb("#9FB0C7");
		}
	}

	private void ApplyRutaCamSoftwareStabilizerAvailability(CameraStabilizationCapabilities capabilities)
	{
		bool available = capabilities.RutaCamSoftwareStabilizerAvailable;
		RutaCamSoftwareStabilizationPicker.IsEnabled = available;
		RutaCamSoftwareStabilizationPicker.Opacity = (available ? 1.0 : 0.55);
		if (!available)
		{
			RutaCamSoftwareStabilizationPicker.SelectedItem = "Desactivada";
			List<string> reasons = new List<string>();
			if (!capabilities.GyroscopeAvailable)
			{
				reasons.Add("sin giroscopio");
			}
			if (!capabilities.FreeformCropAvailable)
			{
				reasons.Add("crop " + capabilities.CropTypeText);
			}
			RutaCamSoftwareStabilizationStatusLabel.Text = "No disponible en esta cámara" + ((reasons.Count == 0) ? "." : (" (" + string.Join(" · ", reasons) + ")."));
			RutaCamSoftwareStabilizationStatusLabel.TextColor = Color.FromArgb("#9FB0C7");
		}
		else
		{
			RutaCamSoftwareStabilizationStatusLabel.Text = "Disponible en esta cámara. Usa recorte dinámico durante la grabación.";
			RutaCamSoftwareStabilizationStatusLabel.TextColor = Color.FromArgb("#9FB0C7");
		}
	}

	private void ApplyCompatibleStabilizationModes(CameraStabilizationCapabilities capabilities)
	{
		string preferred = CameraXStabilizationPicker.SelectedItem?.ToString() ?? CameraXStabilizationPicker.AutomationId ?? "Perfil recomendado · 1080p/30";
		bool videoAvailable = capabilities.EffectiveVideoStabilizationAvailable;
		bool previewAvailable = capabilities.EffectivePreviewStabilizationAvailable;
		bool oisAvailable = capabilities.OisAvailable;
		bool anyStabilization = videoAvailable || previewAvailable || oisAvailable;
		List<string> items = new List<string> { "Perfil recomendado · 1080p/30" };
		if (anyStabilization)
		{
			items.Add("Automática inteligente · OIS/EIS");
		}
		if (videoAvailable || previewAvailable)
		{
			items.Add("Dashcam estable · 1080p/30");
		}
		if (videoAvailable)
		{
			items.Add("Digital CameraX · EIS");
		}
		if (oisAvailable)
		{
			items.Add("OIS hardware preferido");
		}
		items.Add("Desactivada · digital");
		CameraXStabilizationPicker.ItemsSource = items;
		if (items.Contains(preferred))
		{
			CameraXStabilizationPicker.SelectedItem = preferred;
		}
		else
		{
			CameraXStabilizationPicker.SelectedItem = "Perfil recomendado · 1080p/30";
		}
		_stabilizationPickerInitialized = true;
		if (!capabilities.IsAvailable)
		{
			CameraStabilizationCompatibilityLabel.Text = "No se pudieron confirmar modos adicionales; se mantiene el perfil compatible.";
			CameraStabilizationCompatibilityLabel.TextColor = Color.FromArgb("#9FB0C7");
			return;
		}
		if (!anyStabilization)
		{
			CameraStabilizationCompatibilityLabel.Text = "No hay estabilización CameraX disponible; se mantiene la grabación normal.";
			CameraStabilizationCompatibilityLabel.TextColor = Color.FromArgb("#9FB0C7");
			return;
		}
		List<string> available = new List<string>();
		if (oisAvailable)
		{
			available.Add("OIS");
		}
		if (videoAvailable)
		{
			available.Add("EIS vídeo");
		}
		if (previewAvailable)
		{
			available.Add("Preview");
		}
		CameraStabilizationCompatibilityLabel.Text = "Modos disponibles: " + string.Join(", ", available) + ".";
		CameraStabilizationCompatibilityLabel.TextColor = Color.FromArgb("#9FB0C7");
	}

	private async void OnAnalyzeAllCamerasClicked(object sender, EventArgs e)
	{
		AnalyzeAllCamerasButton.IsEnabled = false;
		CopyCameraDiagnosticButton.IsEnabled = false;
		CameraDiagnosticReportLabel.Text = "Analizando topología Camera2 y capacidades CameraX...";
		try
		{
			_latestCameraDiagnosticText = (await _cameraStabilizationService.GetDiagnosticReportAsync()).ToDiagnosticText();
			CameraDiagnosticReportLabel.Text = _latestCameraDiagnosticText;
			CopyCameraDiagnosticButton.IsEnabled = !string.IsNullOrWhiteSpace(_latestCameraDiagnosticText);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_latestCameraDiagnosticText = string.Empty;
			CameraDiagnosticReportLabel.Text = "No se pudo completar el diagnóstico: " + ex2.Message;
		}
		finally
		{
			AnalyzeAllCamerasButton.IsEnabled = true;
		}
	}

	private async void OnCopyCameraDiagnosticClicked(object sender, EventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_latestCameraDiagnosticText))
		{
			await Clipboard.Default.SetTextAsync(_latestCameraDiagnosticText);
			await DisplayAlertAsync("Diagnóstico copiado", "El reporte de cámaras fue copiado al portapapeles.", "OK");
		}
	}

	private void LoadSettings()
	{
		OverlaySettings s = _service.Load();
		ConfigureRestrictedPickerItems(s);
		ShowLogoSwitch.IsToggled = s.ShowLogo;
		ShowMapSwitch.IsToggled = s.ShowMap;
		ShowSpeedSwitch.IsToggled = s.ShowSpeed;
		ShowDistanceSwitch.IsToggled = s.ShowDistance;
		ShowTimerSwitch.IsToggled = s.ShowTimer;
		ShowAverageSpeedSwitch.IsToggled = s.ShowAverageSpeed;
		ShowMaxSpeedSwitch.IsToggled = s.ShowMaxSpeed;
		ShowGpsStatusSwitch.IsToggled = s.ShowGpsStatus;
		ShowCoordinatesSwitch.IsToggled = s.ShowCoordinates;
		ShowAltitudeSwitch.IsToggled = s.ShowAltitude;
		ShowDateTimeSwitch.IsToggled = s.ShowDateTime;
		DateTimeCornerPicker.SelectedItem = s.DateTimeCorner;
		if (DateTimeCornerPicker.SelectedIndex < 0)
		{
			DateTimeCornerPicker.SelectedItem = "Inferior derecha";
		}
		HideSystemBarsSwitch.IsToggled = s.HideSystemBarsDuringRecording;
		LockOrientationSwitch.IsToggled = s.LockOrientationDuringRecording;
		RecordAudioSwitch.IsToggled = s.RecordAudio;
		_savedAudioInputId = (string.IsNullOrWhiteSpace(s.AudioInputDeviceId) ? "automatic" : s.AudioInputDeviceId);
		AudioInputStatusLabel.Text = "Configurado: " + s.AudioInputDisplayName;
		VideoSegmentationPicker.SelectedItem = s.VideoSegmentation;
		if (VideoSegmentationPicker.SelectedIndex < 0)
		{
			VideoSegmentationPicker.SelectedItem = "5 minutos";
		}
		LoopRecordingPicker.SelectedItem = s.LoopRecording;
		if (LoopRecordingPicker.SelectedIndex < 0)
		{
			LoopRecordingPicker.SelectedItem = "Desactivada";
		}
		ProtectEventContextSwitch.IsToggled = s.ProtectEventContext;
		MinimumStorageSlider.Value = Math.Clamp(s.MinimumFreeStorageGb, 0.25, 5.0);
		AutoStopStorageSwitch.IsToggled = s.AutoStopOnCriticalStorage;
		CriticalStorageSlider.Value = Math.Clamp(s.CriticalFreeStorageMb, 100.0, 1000.0);
		WarnLowBatterySwitch.IsToggled = s.WarnOnLowBattery;
		ShowCameraControlsSwitch.IsToggled = s.ShowCameraControls;
		RecordingEnginePicker.SelectedItem = s.RecordingEngine;
		if (RecordingEnginePicker.SelectedIndex < 0)
		{
			RecordingEnginePicker.SelectedItem = "CameraX · recomendado";
		}
		CameraXResolutionPicker.SelectedItem = s.CameraXResolution;
		if (CameraXResolutionPicker.SelectedIndex < 0)
		{
			CameraXResolutionPicker.SelectedItem = "1080p · FHD";
		}
		CameraXFrameRatePicker.SelectedItem = s.CameraXFrameRate;
		if (CameraXFrameRatePicker.SelectedIndex < 0)
		{
			CameraXFrameRatePicker.SelectedItem = "30 FPS";
		}
		CameraXStabilizationPicker.AutomationId = (string.IsNullOrWhiteSpace(s.CameraXStabilization) ? "Perfil recomendado · 1080p/30" : s.CameraXStabilization);
		RutaCamSoftwareStabilizationPicker.SelectedItem = (string.IsNullOrWhiteSpace(s.RutaCamSoftwareStabilization) ? "Desactivada" : s.RutaCamSoftwareStabilization);
		if (RutaCamSoftwareStabilizationPicker.SelectedIndex < 0)
		{
			RutaCamSoftwareStabilizationPicker.SelectedItem = "Desactivada";
		}
		RutaCamGpuStabilizationPicker.SelectedItem = (string.IsNullOrWhiteSpace(s.RutaCamGpuStabilization) ? "Desactivada" : s.RutaCamGpuStabilization);
		if (RutaCamGpuStabilizationPicker.SelectedIndex < 0)
		{
			RutaCamGpuStabilizationPicker.SelectedItem = "Desactivada";
		}
		CameraXHudModePicker.SelectedItem = s.CameraXHudMode;
		if (CameraXHudModePicker.SelectedIndex < 0)
		{
			CameraXHudModePicker.SelectedIndex = 0;
		}
		RecordingModePicker.SelectedItem = s.RecordingMode;
		if (RecordingModePicker.SelectedIndex < 0)
		{
			RecordingModePicker.SelectedIndex = 0;
		}
		VideoQualityPicker.SelectedItem = s.VideoQuality;
		if (VideoQualityPicker.SelectedIndex < 0)
		{
			VideoQualityPicker.SelectedIndex = 0;
		}
		GpsFilterPicker.SelectedItem = s.GpsFilterProfile;
		if (GpsFilterPicker.SelectedIndex < 0)
		{
			GpsFilterPicker.SelectedItem = "Equilibrado";
		}
		CameraPreviewPicker.SelectedItem = s.CameraPreviewMode;
		if (CameraPreviewPicker.SelectedIndex < 0)
		{
			CameraPreviewPicker.SelectedIndex = 0;
		}
		DefaultCameraPicker.SelectedItem = s.DefaultCameraPosition;
		if (DefaultCameraPicker.SelectedIndex < 0)
		{
			DefaultCameraPicker.SelectedItem = "Trasera";
		}
		DefaultZoomSlider.Value = Math.Clamp(s.DefaultZoomFactor, 1.0, 4.0);
		MapShapePicker.SelectedItem = s.MapShape;
		if (MapShapePicker.SelectedIndex < 0)
		{
			MapShapePicker.SelectedIndex = 1;
		}
		MapCornerPicker.SelectedItem = s.MapCorner;
		if (MapCornerPicker.SelectedIndex < 0)
		{
			MapCornerPicker.SelectedIndex = 0;
		}
		MapWidthSlider.Value = s.MapWidth;
		MapOpacitySlider.Value = s.MapOpacity;
		LogoWidthSlider.Value = s.LogoWidth;
		LogoOpacitySlider.Value = s.LogoOpacity;
		_customLogoPath = s.CustomLogoPath ?? string.Empty;
		UpdateLogoPreview();
		ApplyAccessControlState();
		UpdateLabels();
	}

	private void ConfigureRestrictedPickerItems(OverlaySettings settings)
	{
		bool full = _activationService.IsFullAccess;
		Picker cameraXResolutionPicker = CameraXResolutionPicker;
		IEnumerable<string> values = !full
			? new[] { "720p · HD", "1080p · FHD" }
			: new[] { "720p · HD", "1080p · FHD", "4K · UHD" };
		SetPickerItems(cameraXResolutionPicker, values, settings.CameraXResolution, "1080p · FHD");

		Picker cameraXFrameRatePicker = CameraXFrameRatePicker;
		IEnumerable<string> values2 = !full
			? new[] { "30 FPS" }
			: new[] { "30 FPS", "60 FPS" };
		SetPickerItems(cameraXFrameRatePicker, values2, settings.CameraXFrameRate, "30 FPS");

		Picker cameraXHudModePicker = CameraXHudModePicker;
		IEnumerable<string> values3 = !full
			? new[] { "Limpio · solo logo", "Básico · incrustado", "Sin HUD · cámara limpia" }
			: new[] { "Limpio · solo logo", "Básico · incrustado", "Completo · telemetría total", "Sin HUD · cámara limpia" };
		SetPickerItems(cameraXHudModePicker, values3, settings.CameraXHudMode, "Básico · incrustado");

		Picker videoQualityPicker = VideoQualityPicker;
		IEnumerable<string> values4 = !full
			? new[] { "Ligera · hasta 720×1280" }
			: new[] { "Alta · hasta 1080×1920", "Ligera · hasta 720×1280" };
		SetPickerItems(videoQualityPicker, values4, settings.VideoQuality, "Ligera · hasta 720×1280");

		Picker videoSegmentationPicker = VideoSegmentationPicker;
		IEnumerable<string> values5 = !full
			? new[] { "5 minutos" }
			: new[] { "No dividir", "5 minutos", "10 minutos", "20 minutos", "30 minutos" };
		SetPickerItems(videoSegmentationPicker, values5, settings.VideoSegmentation, "5 minutos");

		Picker loopRecordingPicker = LoopRecordingPicker;
		IEnumerable<string> values6 = !full
			? new[] { "1 hora" }
			: new[] { "Desactivada", "1 hora", "2 horas", "4 horas", "8 horas" };
		SetPickerItems(loopRecordingPicker, values6, settings.LoopRecording, "1 hora");

		Picker defaultCameraPicker = DefaultCameraPicker;
		IEnumerable<string> values7 = !full
			? new[] { "Trasera" }
			: new[] { "Trasera", "Frontal" };
		SetPickerItems(defaultCameraPicker, values7, settings.DefaultCameraPosition, "Trasera");

		Picker mapShapePicker = MapShapePicker;
		IEnumerable<string> values8 = !full
			? new[] { "Cuadrado" }
			: new[] { "Cuadrado", "Circular" };
		SetPickerItems(mapShapePicker, values8, settings.MapShape, "Cuadrado");

		Picker mapCornerPicker = MapCornerPicker;
		IEnumerable<string> values9 = !full
			? new[] { "Superior derecha" }
			: new[] { "Superior derecha", "Superior izquierda", "Inferior derecha", "Inferior izquierda" };
		SetPickerItems(mapCornerPicker, values9, settings.MapCorner, "Superior derecha");
	}

	private static void SetPickerItems(Picker picker, IEnumerable<string> values, string? preferred, string fallback)
	{
		picker.Items.Clear();
		foreach (string value in values)
		{
			picker.Items.Add(value);
		}
		string selected = ((!string.IsNullOrWhiteSpace(preferred) && picker.Items.Contains(preferred)) ? preferred : fallback);
		picker.SelectedItem = (picker.Items.Contains(selected) ? selected : picker.Items.FirstOrDefault());
	}

	private static void SetLockedVisualState(VisualElement element, bool locked)
	{
		element.Opacity = (locked ? 0.42 : 1.0);
	}

	private void ApplyAccessControlState()
	{
		bool full = _activationService.IsFullAccess;
		FullProfileButton.IsEnabled = full;
		FullProfileButton.Text = (full ? "COMPLETO" : "COMPLETO \ud83d\udd12");
		CameraXFrameRatePicker.IsEnabled = full;
		VideoQualityPicker.IsEnabled = full;
		VideoSegmentationPicker.IsEnabled = full;
		LoopRecordingPicker.IsEnabled = full;
		RefreshAudioButton.IsEnabled = full;
		AudioInputPicker.IsEnabled = full;
		DefaultCameraPicker.IsEnabled = true;
		ShowLogoSwitch.IsEnabled = full;
		ChooseLogoButton.IsEnabled = full;
		UseDefaultLogoButton.IsEnabled = full;
		LogoWidthSlider.IsEnabled = full;
		LogoOpacitySlider.IsEnabled = full;
		ShowAverageSpeedSwitch.IsEnabled = full;
		ShowMaxSpeedSwitch.IsEnabled = full;
		ShowGpsStatusSwitch.IsEnabled = full;
		ShowCoordinatesSwitch.IsEnabled = full;
		ShowAltitudeSwitch.IsEnabled = full;
		ShowMapSwitch.IsEnabled = full;
		MapShapePicker.IsEnabled = full;
		MapCornerPicker.IsEnabled = full;
		MapOpacitySlider.IsEnabled = full;
		bool locked = !full;
		SetLockedVisualState(FullProfileButton, locked);
		SetLockedVisualState(Premium4KLabel, locked);
		SetLockedVisualState(Premium60FpsLabel, locked);
		SetLockedVisualState(PremiumCompleteHudLabel, locked);
		SetLockedVisualState(PremiumHighCompatibilityLabel, locked);
		SetLockedVisualState(PremiumExternalAudioLabel, locked);
		SetLockedVisualState(PremiumSegmentationLabel, locked);
		SetLockedVisualState(PremiumLoopLabel, locked);
		SetLockedVisualState(PremiumFrontCameraLabel, locked);
		SetLockedVisualState(PremiumTelemetryHintLabel, locked);
		SetLockedVisualState(PremiumMapShapeLabel, locked);
		SetLockedVisualState(PremiumMapCornerLabel, locked);
		SetLockedVisualState(PremiumMapOpacityLabel, locked);
		SetLockedVisualState(PremiumLogoOptionsLabel, locked);
		SetLockedVisualState(CameraXFrameRatePicker, locked);
		SetLockedVisualState(VideoQualityPicker, locked);
		SetLockedVisualState(VideoSegmentationPicker, locked);
		SetLockedVisualState(LoopRecordingPicker, locked);
		SetLockedVisualState(AudioInputPicker, locked);
		SetLockedVisualState(RefreshAudioButton, locked);
		SetLockedVisualState(PremiumLogoToggleRow, locked);
		SetLockedVisualState(ChooseLogoButton, locked);
		SetLockedVisualState(UseDefaultLogoButton, locked);
		SetLockedVisualState(PremiumLogoSizeTitleLabel, locked);
		SetLockedVisualState(LogoWidthSlider, locked);
		SetLockedVisualState(LogoWidthLabel, locked);
		SetLockedVisualState(PremiumLogoOpacityTitleLabel, locked);
		SetLockedVisualState(LogoOpacitySlider, locked);
		SetLockedVisualState(LogoOpacityLabel, locked);
		SetLockedVisualState(PremiumAverageSpeedRow, locked);
		SetLockedVisualState(PremiumMaxSpeedRow, locked);
		SetLockedVisualState(PremiumGpsStatusRow, locked);
		SetLockedVisualState(PremiumCoordinatesRow, locked);
		SetLockedVisualState(PremiumAltitudeRow, locked);
		SetLockedVisualState(PremiumMapToggleRow, locked);
		SetLockedVisualState(MapShapePicker, locked);
		SetLockedVisualState(MapCornerPicker, locked);
		SetLockedVisualState(MapOpacitySlider, locked);
		SetLockedVisualState(MapOpacityLabel, locked);
		if (!full)
		{
			CameraXFrameRatePicker.SelectedItem = "30 FPS";
			VideoQualityPicker.SelectedItem = "Ligera · hasta 720×1280";
			VideoSegmentationPicker.SelectedItem = "5 minutos";
			LoopRecordingPicker.SelectedItem = "1 hora";
			DefaultCameraPicker.SelectedItem = "Trasera";
			_savedAudioInputId = "phone";
			_customLogoPath = string.Empty;
			ShowLogoSwitch.IsToggled = true;
			ShowMapSwitch.IsToggled = true;
			ShowAverageSpeedSwitch.IsToggled = false;
			ShowMaxSpeedSwitch.IsToggled = false;
			ShowGpsStatusSwitch.IsToggled = false;
			ShowCoordinatesSwitch.IsToggled = false;
			ShowAltitudeSwitch.IsToggled = false;
			MapShapePicker.SelectedItem = "Cuadrado";
			MapCornerPicker.SelectedItem = "Superior derecha";
			MapOpacitySlider.Value = 0.8;
			LogoWidthSlider.Value = 88.0;
			LogoOpacitySlider.Value = 0.8;
			UpdateLogoPreview();
		}
		else
		{
			FullProfileButton.Opacity = 1.0;
		}
		UpdateActivationUi();
	}

	private void UpdateActivationUi()
	{
		bool full = _activationService.IsFullAccess;
		ActivationButtons.IsVisible = !full;
		PremiumLegendLabel.IsVisible = !full;
		Premium4KLabel.IsVisible = !full;
		Premium60FpsLabel.IsVisible = !full;
		PremiumCompleteHudLabel.IsVisible = !full;
		PremiumHighCompatibilityLabel.IsVisible = !full;
		PremiumExternalAudioLabel.IsVisible = !full;
		PremiumSegmentationLabel.IsVisible = !full;
		PremiumLoopLabel.IsVisible = !full;
		PremiumFrontCameraLabel.IsVisible = !full;
		PremiumTelemetryHintLabel.IsVisible = !full;
		PremiumMapShapeLabel.IsVisible = !full;
		PremiumMapCornerLabel.IsVisible = !full;
		PremiumMapOpacityLabel.IsVisible = !full;
		PremiumLogoOptionsLabel.IsVisible = !full;
		if (full)
		{
			ActivationCard.Stroke = new SolidColorBrush(Color.FromArgb("#23334A"));
			ActivationStatusLabel.Text = "ACCESO COMPLETO ACTIVO";
			ActivationStatusLabel.TextColor = Colors.White;
			Label activationDetailLabel = ActivationDetailLabel;
			DateTimeOffset? expiresAtLocal = _activationService.ExpiresAtLocal;
			object text;
			if (expiresAtLocal.HasValue)
			{
				DateTimeOffset expires = expiresAtLocal.GetValueOrDefault();
				text = $"Funciones completas disponibles hasta {expires:dd/MM/yyyy HH:mm}.";
			}
			else
			{
				text = "Funciones completas disponibles.";
			}
			activationDetailLabel.Text = (string)text;
		}
		else
		{
			ActivationCard.Stroke = new SolidColorBrush(Color.FromArgb("#23334A"));
			ActivationStatusLabel.Text = "MODO LIMITADO";
			ActivationStatusLabel.TextColor = Colors.White;
			ActivationDetailLabel.Text = "RutaCam está trabajando con funciones limitadas. Solicita el código de activación completa por solo 5€ EUR.";
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshStabilizationCapabilitiesAsync();
		if (!_audioInputsLoaded)
		{
			await RefreshAudioInputsAsync(requestPermission: false);
		}
	}

	private async Task RefreshAudioInputsAsync(bool requestPermission)
	{
		try
		{
			AudioInputStatusLabel.Text = "Buscando micrófonos disponibles...";
			IReadOnlyList<AudioInputOption> options = await _audioInputService.GetAvailableInputsAsync(requestPermission && _activationService.IsFullAccess, CancellationToken.None);
			if (!_activationService.IsFullAccess)
			{
				options = options.Where((AudioInputOption x) => string.Equals(x.Id, "phone", StringComparison.OrdinalIgnoreCase)).ToList();
				_savedAudioInputId = "phone";
			}
			AudioInputPicker.ItemsSource = options.ToList();
			AudioInputOption selected = options.FirstOrDefault((AudioInputOption x) => string.Equals(x.Id, _savedAudioInputId, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
			AudioInputPicker.SelectedItem = selected;
			_audioInputsLoaded = true;
			if (!_activationService.IsFullAccess)
			{
				AudioInputStatusLabel.Text = "Modo limitado: Micrófono del teléfono.";
				return;
			}
			AudioInputOption bluetooth = options.FirstOrDefault((AudioInputOption x) => x.IsBluetooth);
			AudioInputStatusLabel.Text = ((bluetooth != null) ? ("Detectado: " + bluetooth.DisplayName) : "No se detectó un micrófono Bluetooth. Conecta el Y12 Pro y pulsa ACTUALIZAR.");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			AudioInputStatusLabel.Text = "No se pudieron leer las entradas: " + ex2.Message;
		}
	}

	private async void OnRefreshAudioInputsClicked(object sender, EventArgs e)
	{
		await RefreshAudioInputsAsync(requestPermission: true);
	}

	private async void OnTestMicrophoneClicked(object sender, EventArgs e)
	{
		if (!RecordAudioSwitch.IsToggled)
		{
			await DisplayAlertAsync("Audio", "Activa Grabar audio ambiente antes de probar una entrada.", "OK");
			return;
		}
		if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
		{
			await DisplayAlertAsync("Micrófono", "RutaCam necesita permiso de micrófono para realizar la prueba.", "OK");
			return;
		}
		if (AudioInputPicker.SelectedItem is AudioInputOption selected)
		{
			TestMicrophoneButton.IsEnabled = false;
			AudioInputPicker.IsEnabled = false;
			MicrophoneLevelBar.Progress = 0.0;
			AudioInputStatusLabel.Text = "Probando " + selected.DisplayName + "... Habla durante 4 segundos.";
			try
			{
				Progress<double> progress = new Progress<double>(delegate(double value)
				{
					MainThread.BeginInvokeOnMainThread(delegate
					{
						MicrophoneLevelBar.Progress = value;
					});
				});
				MicrophoneTestResult result = await _audioInputService.TestMicrophoneAsync(selected.Id, progress, CancellationToken.None);
				AudioInputStatusLabel.Text = result.Message;
				if (!result.Success)
				{
					await DisplayAlertAsync("Prueba de micrófono", result.Message, "OK");
				}
				return;
			}
			finally
			{
				TestMicrophoneButton.IsEnabled = true;
				AudioInputPicker.IsEnabled = _activationService.IsFullAccess;
			}
		}
		await DisplayAlertAsync("Micrófono", "Selecciona primero una fuente de audio.", "OK");
	}

	private void SelectAutomaticAudioInput()
	{
		string selection = (_activationService.IsFullAccess ? "automatic" : "phone");
		_savedAudioInputId = selection;
		if (AudioInputPicker.ItemsSource is IEnumerable<AudioInputOption> options)
		{
			AudioInputPicker.SelectedItem = options.FirstOrDefault((AudioInputOption x) => x.Id == selection);
		}
	}

	private void OnCleanProfileClicked(object sender, EventArgs e)
	{
		RecordingEnginePicker.SelectedItem = "CameraX · recomendado";
		CameraXResolutionPicker.SelectedItem = "1080p · FHD";
		CameraXFrameRatePicker.SelectedItem = "30 FPS";
		CameraXStabilizationPicker.SelectedItem = "Automática inteligente · OIS/EIS";
		CameraXHudModePicker.SelectedItem = "Limpio · solo logo";
		RecordingModePicker.SelectedItem = "Con telemetría";
		ShowLogoSwitch.IsToggled = true;
		ShowMapSwitch.IsToggled = false;
		ShowSpeedSwitch.IsToggled = false;
		ShowDistanceSwitch.IsToggled = false;
		ShowTimerSwitch.IsToggled = false;
		ShowAverageSpeedSwitch.IsToggled = false;
		ShowMaxSpeedSwitch.IsToggled = false;
		ShowGpsStatusSwitch.IsToggled = false;
		ShowCoordinatesSwitch.IsToggled = false;
		ShowAltitudeSwitch.IsToggled = false;
		ShowDateTimeSwitch.IsToggled = false;
		DateTimeCornerPicker.SelectedItem = "Inferior derecha";
		LogoWidthSlider.Value = 88.0;
		LogoOpacitySlider.Value = 0.92;
		RecordAudioSwitch.IsToggled = true;
		SelectAutomaticAudioInput();
		VideoSegmentationPicker.SelectedItem = "5 minutos";
		LoopRecordingPicker.SelectedItem = "Desactivada";
		ProtectEventContextSwitch.IsToggled = true;
		ApplyAccessControlState();
		UpdateLabels();
	}

	private void OnBasicProfileClicked(object sender, EventArgs e)
	{
		RecordingEnginePicker.SelectedItem = "CameraX · recomendado";
		CameraXResolutionPicker.SelectedItem = "1080p · FHD";
		CameraXFrameRatePicker.SelectedItem = "30 FPS";
		CameraXStabilizationPicker.SelectedItem = "Automática inteligente · OIS/EIS";
		CameraXHudModePicker.SelectedItem = "Básico · incrustado";
		RecordingModePicker.SelectedItem = "Con telemetría";
		ShowLogoSwitch.IsToggled = true;
		ShowMapSwitch.IsToggled = true;
		ShowSpeedSwitch.IsToggled = true;
		ShowDistanceSwitch.IsToggled = true;
		ShowTimerSwitch.IsToggled = true;
		ShowAverageSpeedSwitch.IsToggled = false;
		ShowMaxSpeedSwitch.IsToggled = false;
		ShowGpsStatusSwitch.IsToggled = false;
		ShowCoordinatesSwitch.IsToggled = false;
		ShowAltitudeSwitch.IsToggled = false;
		ShowDateTimeSwitch.IsToggled = false;
		DateTimeCornerPicker.SelectedItem = "Inferior derecha";
		MapShapePicker.SelectedItem = "Circular";
		MapCornerPicker.SelectedItem = "Superior derecha";
		MapWidthSlider.Value = 150.0;
		MapOpacitySlider.Value = 0.92;
		LogoWidthSlider.Value = 88.0;
		LogoOpacitySlider.Value = 0.92;
		RecordAudioSwitch.IsToggled = true;
		SelectAutomaticAudioInput();
		VideoSegmentationPicker.SelectedItem = "5 minutos";
		LoopRecordingPicker.SelectedItem = "Desactivada";
		ProtectEventContextSwitch.IsToggled = true;
		GpsFilterPicker.SelectedItem = "Equilibrado";
		MinimumStorageSlider.Value = 1.0;
		AutoStopStorageSwitch.IsToggled = true;
		CriticalStorageSlider.Value = 250.0;
		WarnLowBatterySwitch.IsToggled = true;
		ShowCameraControlsSwitch.IsToggled = true;
		DefaultCameraPicker.SelectedItem = "Trasera";
		DefaultZoomSlider.Value = 1.0;
		ApplyAccessControlState();
		UpdateLabels();
	}

	private async void OnFullProfileClicked(object sender, EventArgs e)
	{
		if (!_activationService.IsFullAccess)
		{
			await DisplayAlertAsync("Función bloqueada", "El perfil Completo requiere la activación anual.", "OK");
			return;
		}
		RecordingEnginePicker.SelectedItem = "CameraX · recomendado";
		CameraXResolutionPicker.SelectedItem = "1080p · FHD";
		CameraXFrameRatePicker.SelectedItem = "30 FPS";
		CameraXStabilizationPicker.SelectedItem = "Automática inteligente · OIS/EIS";
		CameraXHudModePicker.SelectedItem = "Completo · telemetría total";
		RecordingModePicker.SelectedItem = "Con telemetría";
		ShowLogoSwitch.IsToggled = true;
		ShowMapSwitch.IsToggled = true;
		ShowSpeedSwitch.IsToggled = true;
		ShowDistanceSwitch.IsToggled = true;
		ShowTimerSwitch.IsToggled = true;
		ShowAverageSpeedSwitch.IsToggled = true;
		ShowMaxSpeedSwitch.IsToggled = true;
		ShowGpsStatusSwitch.IsToggled = true;
		ShowCoordinatesSwitch.IsToggled = false;
		ShowAltitudeSwitch.IsToggled = true;
		ShowDateTimeSwitch.IsToggled = true;
		DateTimeCornerPicker.SelectedItem = "Inferior derecha";
		MapWidthSlider.Value = 160.0;
		MapOpacitySlider.Value = 0.94;
		RecordAudioSwitch.IsToggled = true;
		SelectAutomaticAudioInput();
		VideoSegmentationPicker.SelectedItem = "5 minutos";
		LoopRecordingPicker.SelectedItem = "Desactivada";
		ProtectEventContextSwitch.IsToggled = true;
		GpsFilterPicker.SelectedItem = "Preciso";
		MinimumStorageSlider.Value = 1.5;
		AutoStopStorageSwitch.IsToggled = true;
		CriticalStorageSlider.Value = 350.0;
		WarnLowBatterySwitch.IsToggled = true;
		ShowCameraControlsSwitch.IsToggled = true;
		DefaultCameraPicker.SelectedItem = "Trasera";
		DefaultZoomSlider.Value = 1.0;
		ApplyAccessControlState();
		UpdateLabels();
	}

	private async void OnChooseLogoClicked(object sender, EventArgs e)
	{
		if (!_activationService.IsFullAccess)
		{
			await DisplayAlertAsync("Función bloqueada", "El logo personalizado requiere la activación anual.", "OK");
			return;
		}
		try
		{
			FileResult selected = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
			{
				Title = "Elegir logo para RutaCam"
			}))?.FirstOrDefault();
			if (selected == null)
			{
				return;
			}
			string brandingFolder = Path.Combine(FileSystem.AppDataDirectory, "Branding");
			Directory.CreateDirectory(brandingFolder);
			foreach (string oldFile in Directory.EnumerateFiles(brandingFolder, "custom_logo.*"))
			{
				try
				{
					File.Delete(oldFile);
				}
				catch
				{
				}
			}
			string extension = Path.GetExtension(selected.FileName);
			if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
			{
				extension = ".img";
			}
			string targetPath = Path.Combine(brandingFolder, "custom_logo" + extension.ToLowerInvariant());
			await using (Stream input = await selected.OpenReadAsync())
			{
				await using FileStream output = File.Create(targetPath);
				await input.CopyToAsync(output);
			}
			_customLogoPath = targetPath;
			ShowLogoSwitch.IsToggled = true;
			UpdateLogoPreview();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Logo personal", "No se pudo cargar la imagen: " + ex.Message, "Aceptar");
		}
	}

	private void OnUseDefaultLogoClicked(object sender, EventArgs e)
	{
		_customLogoPath = string.Empty;
		ShowLogoSwitch.IsToggled = true;
		UpdateLogoPreview();
	}

	private void UpdateLogoPreview()
	{
		if (!string.IsNullOrWhiteSpace(_customLogoPath) && File.Exists(_customLogoPath))
		{
			try
			{
				string path = _customLogoPath;
				LogoPreview.Source = ImageSource.FromStream(() => File.OpenRead(path));
				LogoSourceLabel.Text = "Personalizado · " + Path.GetFileName(path);
				return;
			}
			catch
			{
			}
		}
		LogoPreview.Source = "personal_logo.png";
		LogoSourceLabel.Text = "Logo predeterminado · icono oficial de RutaCam GPS";
	}

	private void UpdateLabels()
	{
		MapWidthLabel.Text = $"{MapWidthSlider.Value:F0} dp";
		MapOpacityLabel.Text = $"{MapOpacitySlider.Value:P0}";
		LogoWidthLabel.Text = $"{LogoWidthSlider.Value:F0} dp";
		LogoOpacityLabel.Text = $"{LogoOpacitySlider.Value:P0}";
		DefaultZoomLabel.Text = $"{DefaultZoomSlider.Value:F1}×";
		MinimumStorageLabel.Text = $"{MinimumStorageSlider.Value:F2} GB libres";
		CriticalStorageLabel.Text = $"{CriticalStorageSlider.Value:F0} MB libres";
	}

	private async void OnRequestActivationClicked(object sender, EventArgs e)
	{
		if (!(await _activationService.OpenWhatsAppRequestAsync()))
		{
			await DisplayAlertAsync("WhatsApp", "No fue posible abrir WhatsApp. Verifica que WhatsApp o WhatsApp Business estén instalados y actualizados.", "OK");
			return;
		}
		await DisplayAlertAsync("Solicitud preparada", "Envía el mensaje por WhatsApp. Al volver a RutaCam se abrirá el cuadro para introducir el código.", "CONTINUAR");
		await PromptForActivationCodeAsync();
	}

	private async void OnEnterActivationCodeClicked(object sender, EventArgs e)
	{
		await PromptForActivationCodeAsync();
	}

	private async Task PromptForActivationCodeAsync()
	{
		string code = await DisplayPromptAsync("Código de activación", "Introduce el código valido para la activacion.", "ACTIVAR", "CANCELAR", "Código numérico", 18, Keyboard.Numeric);
		if (!string.IsNullOrWhiteSpace(code))
		{
			ActivationResult result = _activationService.TryActivate(code);
			if (!result.Success)
			{
				await DisplayAlertAsync("Activación", result.Message, "OK");
				return;
			}
			LoadSettings();
			_audioInputsLoaded = false;
			await RefreshAudioInputsAsync(requestPermission: false);
			await DisplayAlertAsync("Activación completa", result.Message, "OK");
		}
	}

	private async void OnSaveClicked(object sender, EventArgs e)
	{
		if (!_stabilizationPickerInitialized || CameraXStabilizationPicker.SelectedItem == null)
		{
			CameraXStabilizationPicker.ItemsSource = new string[2] { "Perfil recomendado · 1080p/30", "Desactivada · digital" };
			CameraXStabilizationPicker.SelectedItem = "Perfil recomendado · 1080p/30";
			_stabilizationPickerInitialized = true;
		}
		OverlaySettings s = new OverlaySettings
		{
			ShowLogo = ShowLogoSwitch.IsToggled,
			ShowMap = ShowMapSwitch.IsToggled,
			ShowSpeed = ShowSpeedSwitch.IsToggled,
			ShowDistance = ShowDistanceSwitch.IsToggled,
			ShowTimer = ShowTimerSwitch.IsToggled,
			ShowAverageSpeed = ShowAverageSpeedSwitch.IsToggled,
			ShowMaxSpeed = ShowMaxSpeedSwitch.IsToggled,
			ShowRec = false,
			ShowGpsStatus = ShowGpsStatusSwitch.IsToggled,
			ShowCoordinates = ShowCoordinatesSwitch.IsToggled,
			ShowAltitude = ShowAltitudeSwitch.IsToggled,
			ShowDateTime = ShowDateTimeSwitch.IsToggled,
			DateTimeCorner = (DateTimeCornerPicker.SelectedItem?.ToString() ?? "Inferior derecha"),
			ShowBatteryStatus = false,
			ShowStorageStatus = false,
			RecordingEngine = (RecordingEnginePicker.SelectedItem?.ToString() ?? "CameraX · recomendado"),
			CameraXResolution = (CameraXResolutionPicker.SelectedItem?.ToString() ?? "1080p · FHD"),
			CameraXFrameRate = (CameraXFrameRatePicker.SelectedItem?.ToString() ?? "30 FPS"),
			CameraXStabilization = (CameraXStabilizationPicker.SelectedItem?.ToString() ?? "Perfil recomendado · 1080p/30"),
			RutaCamSoftwareStabilization = (RutaCamSoftwareStabilizationPicker.SelectedItem?.ToString() ?? "Desactivada"),
			RutaCamGpuStabilization = (RutaCamGpuStabilizationPicker.SelectedItem?.ToString() ?? "Desactivada"),
			CameraXPhysicalCameraId = string.Empty,
			CameraXLensDisplayName = "Automática · cámara trasera",
			CameraXHudMode = (CameraXHudModePicker.SelectedItem?.ToString() ?? "Básico · incrustado"),
			RecordingMode = (RecordingModePicker.SelectedItem?.ToString() ?? "Con telemetría"),
			VideoQuality = (VideoQualityPicker.SelectedItem?.ToString() ?? "Alta · hasta 1080×1920"),
			CameraPreviewMode = (CameraPreviewPicker.SelectedItem?.ToString() ?? "Llenar pantalla"),
			DefaultCameraPosition = (DefaultCameraPicker.SelectedItem?.ToString() ?? "Trasera"),
			DefaultZoomFactor = DefaultZoomSlider.Value,
			ShowCameraControls = ShowCameraControlsSwitch.IsToggled,
			MapShape = (MapShapePicker.SelectedItem?.ToString() ?? "Circular"),
			MapCorner = (MapCornerPicker.SelectedItem?.ToString() ?? "Superior derecha"),
			MapWidth = MapWidthSlider.Value,
			MapOpacity = MapOpacitySlider.Value,
			LogoWidth = LogoWidthSlider.Value,
			LogoOpacity = LogoOpacitySlider.Value,
			CustomLogoPath = _customLogoPath,
			HideSystemBarsDuringRecording = HideSystemBarsSwitch.IsToggled,
			LockOrientationDuringRecording = LockOrientationSwitch.IsToggled,
			RecordAudio = RecordAudioSwitch.IsToggled,
			AudioInputDeviceId = ((AudioInputPicker.SelectedItem as AudioInputOption)?.Id ?? _savedAudioInputId ?? "automatic"),
			AudioInputDisplayName = ((AudioInputPicker.SelectedItem as AudioInputOption)?.DisplayName ?? "Automática · Android decide"),
			VideoSegmentation = (VideoSegmentationPicker.SelectedItem?.ToString() ?? "5 minutos"),
			LoopRecording = (LoopRecordingPicker.SelectedItem?.ToString() ?? "Desactivada"),
			ProtectEventContext = ProtectEventContextSwitch.IsToggled,
			MinimumFreeStorageGb = MinimumStorageSlider.Value,
			AutoStopOnCriticalStorage = AutoStopStorageSwitch.IsToggled,
			CriticalFreeStorageMb = CriticalStorageSlider.Value,
			WarnOnLowBattery = WarnLowBatterySwitch.IsToggled,
			GpsFilterProfile = (GpsFilterPicker.SelectedItem?.ToString() ?? "Equilibrado"),
			RequireGpsFixBeforeRecording = false
		};
		_service.Save(s);
		await base.Navigation.PopAsync();
	}

}
