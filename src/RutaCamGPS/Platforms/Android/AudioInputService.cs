using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Media;
using GPSCamRoute.Models;
using GPSCamRoute.Services;
using Microsoft.Maui.ApplicationModel;

namespace GPSCamRoute.Platforms.Android;

public sealed class AudioInputService : IAudioInputService
{
	private const int TypeWiredHeadset = 3;

	private const int TypeBluetoothSco = 7;

	private const int TypeUsbDevice = 11;

	private const int TypeUsbAccessory = 12;

	private const int TypeBuiltInMic = 15;

	private const int TypeUsbHeadset = 22;

	private const int TypeBleHeadset = 26;

	private readonly AudioManager _audioManager;

	private string _activeSelectionId = "automatic";

	public AudioInputService()
	{
		Context context = Application.Context;
		_audioManager = ((AudioManager)context.GetSystemService("audio")) ?? throw new InvalidOperationException("Android no proporcionó AudioManager.");
	}

	public async Task<IReadOnlyList<AudioInputOption>> GetAvailableInputsAsync(bool requestBluetoothPermission, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<AudioInputOption> results = new List<AudioInputOption>
		{
			new AudioInputOption
			{
				Id = "automatic",
				DisplayName = "Automática · Android decide",
				IsBuiltIn = true
			},
			new AudioInputOption
			{
				Id = "phone",
				DisplayName = "Micrófono del teléfono",
				DeviceType = 15,
				ProductName = "Micrófono integrado",
				IsBuiltIn = true
			}
		};
		if (!(await EnsureBluetoothPermissionAsync(requestBluetoothPermission)))
		{
			return results;
		}
		AudioDeviceInfo[] inputs;
		try
		{
			inputs = _audioManager.GetDevices(GetDevicesTargets.Inputs) ?? Array.Empty<AudioDeviceInfo>();
		}
		catch
		{
			return results;
		}
		AudioDeviceInfo[] array = inputs;
		foreach (AudioDeviceInfo device in array)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (device == null || !device.IsSource)
			{
				continue;
			}
			int type = (int)device.Type;
			if (type != 15 && IsSupportedExternalInput(type))
			{
				string productName = SafeProductName(device);
				string displayName = BuildDisplayName(type, productName);
				string id = BuildSelectionId(type, productName);
				if (!results.Any((AudioInputOption x) => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
				{
					results.Add(new AudioInputOption
					{
						Id = id,
						DisplayName = displayName,
						DeviceType = type,
						ProductName = productName,
						IsBluetooth = IsBluetoothType(type)
					});
				}
			}
		}
		return results;
	}

	public async Task<AudioRouteResult> ActivateAsync(string selectionId, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		selectionId = NormalizeSelection(selectionId);
		string text = selectionId;
		if ((text == "automatic" || text == "phone") ? true : false)
		{
			await ReleaseAsync();
			_activeSelectionId = selectionId;
			return new AudioRouteResult
			{
				Success = true,
				ActiveDisplayName = ((selectionId == "phone") ? "Micrófono del teléfono" : "Automática · Android decide"),
				Message = ((selectionId == "phone") ? "RutaCam utilizará el micrófono integrado del teléfono." : "Android elegirá automáticamente la fuente de audio.")
			};
		}
		if (!(await EnsureBluetoothPermissionAsync(request: true)))
		{
			await ReleaseAsync();
			return new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "No se concedió el permiso para usar dispositivos Bluetooth. Se usará el micrófono del teléfono."
			};
		}
		if (!TryParseSelectionId(selectionId, out int requestedType, out string productName))
		{
			await ReleaseAsync();
			return new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "La fuente seleccionada ya no es válida. Se usará el micrófono del teléfono."
			};
		}
		AudioDeviceInfo input = FindInputDevice(requestedType, productName);
		if (input == null)
		{
			await ReleaseAsync();
			return new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "El micrófono seleccionado no está conectado. Se usará el micrófono del teléfono."
			};
		}
		try
		{
			_audioManager.Mode = Mode.InCommunication;
			if (OperatingSystem.IsAndroidVersionAtLeast(31))
			{
				AudioDeviceInfo communicationDevice = FindCommunicationDevice(requestedType, productName);
				if (communicationDevice == null)
				{
					throw new InvalidOperationException("Android no expuso una ruta de comunicación compatible para este micrófono.");
				}
				if (!_audioManager.SetCommunicationDevice(communicationDevice))
				{
					throw new InvalidOperationException("Android rechazó la selección del dispositivo de comunicación.");
				}
				await Task.Delay(700, cancellationToken);
				AudioDeviceInfo active = _audioManager.CommunicationDevice;
				if (active == null || !TypesAreCompatible(requestedType, (int)active.Type))
				{
					throw new InvalidOperationException("Android no confirmó la ruta de audio solicitada.");
				}
			}
			else if (requestedType == 7)
			{
				_audioManager.StartBluetoothSco();
				_audioManager.BluetoothScoOn = true;
				await Task.Delay(1200, cancellationToken);
			}
			_activeSelectionId = selectionId;
			return new AudioRouteResult
			{
				Success = true,
				ActiveDisplayName = BuildDisplayName(requestedType, productName),
				Message = "Fuente activa: " + BuildDisplayName(requestedType, productName)
			};
		}
		catch (Exception ex)
		{
			await ReleaseAsync();
			return new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "No se pudo activar " + BuildDisplayName(requestedType, productName) + ". Se usará el micrófono del teléfono. Detalle: " + ex.Message
			};
		}
	}

	public Task<AudioRouteResult> EnsureSelectedRouteAsync(string selectionId, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		selectionId = NormalizeSelection(selectionId);
		if ((selectionId == "automatic" || selectionId == "phone") ? true : false)
		{
			return Task.FromResult(new AudioRouteResult
			{
				Success = true,
				ActiveDisplayName = ((selectionId == "phone") ? "Micrófono del teléfono" : "Automática · Android decide")
			});
		}
		if (!TryParseSelectionId(selectionId, out int requestedType, out string productName))
		{
			return Task.FromResult(new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "La fuente de audio seleccionada ya no es válida."
			});
		}
		AudioDeviceInfo input = FindInputDevice(requestedType, productName);
		if (input == null)
		{
			Debug.WriteLine("RutaCam AUDIO FIX44 · desconectado durante REC · " + BuildDisplayName(requestedType, productName));
			return Task.FromResult(new AudioRouteResult
			{
				Success = false,
				UsedFallback = true,
				ActiveDisplayName = "Micrófono del teléfono",
				Message = "El micrófono externo se desconectó durante la grabación. RutaCam no renegocia la ruta Bluetooth hasta la próxima grabación."
			});
		}
		if (OperatingSystem.IsAndroidVersionAtLeast(31))
		{
			try
			{
				int activeType = (int)(_audioManager.CommunicationDevice?.Type ?? ((AudioDeviceType)(-1)));
				Debug.WriteLine($"RutaCam AUDIO FIX44 · route-lock · input={requestedType} · communication={activeType}");
			}
			catch
			{
			}
		}
		return Task.FromResult(new AudioRouteResult
		{
			Success = true,
			ActiveDisplayName = BuildDisplayName(requestedType, productName),
			Message = "Ruta de audio bloqueada durante la grabación."
		});
	}

	public async Task<MicrophoneTestResult> TestMicrophoneAsync(string selectionId, IProgress<double>? levelProgress, CancellationToken cancellationToken)
	{
		AudioRouteResult route = await ActivateAsync(selectionId, cancellationToken);
		if (!route.Success && !string.Equals(NormalizeSelection(selectionId), "automatic", StringComparison.OrdinalIgnoreCase))
		{
			return new MicrophoneTestResult
			{
				Success = false,
				ActiveDisplayName = route.ActiveDisplayName,
				Message = route.Message
			};
		}
		AudioRecord recorder = null;
		double peak = 0.0;
		MicrophoneTestResult result;
		try
		{
			string normalizedSelection = NormalizeSelection(selectionId);
			TryParseSelectionId(normalizedSelection, out int selectedType, out string productName);
			string text = normalizedSelection;
			bool flag = ((text == "automatic" || text == "phone") ? true : false);
			AudioDeviceInfo preferredInput = (flag ? null : FindInputDevice(selectedType, productName));
			int sampleRate = (IsBluetoothType(selectedType) ? 16000 : 44100);
			int minimumBuffer = AudioRecord.GetMinBufferSize(sampleRate, ChannelIn.Front, global::Android.Media.Encoding.Pcm16bit);
			int bufferSize = Math.Max(minimumBuffer, sampleRate / 2);
			recorder = new AudioRecord(AudioSource.VoiceCommunication, sampleRate, ChannelIn.Front, global::Android.Media.Encoding.Pcm16bit, bufferSize);
			if (preferredInput != null)
			{
				recorder.SetPreferredDevice(preferredInput);
			}
			recorder.StartRecording();
			short[] buffer = new short[Math.Max(512, bufferSize / 2)];
			DateTimeOffset testUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(4L);
			while (DateTimeOffset.UtcNow < testUntil)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int read = recorder.Read(buffer, 0, buffer.Length);
				if (read > 0)
				{
					double sum = 0.0;
					for (int i = 0; i < read; i++)
					{
						double value = (double)buffer[i] / 32768.0;
						sum += value * value;
					}
					double rms = Math.Sqrt(sum / (double)read);
					double normalized = Math.Clamp(rms * 5.5, 0.0, 1.0);
					peak = Math.Max(peak, normalized);
					levelProgress?.Report(normalized);
				}
			}
			AudioDeviceInfo routedDevice = recorder.RoutedDevice;
			string routedName = ((routedDevice == null) ? route.ActiveDisplayName : BuildDisplayName((int)routedDevice.Type, SafeProductName(routedDevice)));
			result = new MicrophoneTestResult
			{
				Success = (peak > 0.01),
				PeakLevel = peak,
				ActiveDisplayName = routedName,
				Message = ((peak > 0.01) ? ("Se detectó audio desde " + routedName + ".") : ("Android abrió " + routedName + ", pero no se detectó señal. Habla cerca del micrófono y repite la prueba."))
			};
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			result = new MicrophoneTestResult
			{
				Success = false,
				ActiveDisplayName = route.ActiveDisplayName,
				Message = "No se pudo realizar la prueba: " + ex2.Message
			};
		}
		finally
		{
			try
			{
				recorder?.Stop();
			}
			catch
			{
			}
			try
			{
				recorder?.Release();
			}
			catch
			{
			}
			recorder?.Dispose();
			levelProgress?.Report(0.0);
			await ReleaseAsync();
		}
		return result;
	}

	public Task ReleaseAsync()
	{
		try
		{
			if (OperatingSystem.IsAndroidVersionAtLeast(31))
			{
				_audioManager.ClearCommunicationDevice();
			}
			else
			{
				try
				{
					_audioManager.BluetoothScoOn = false;
				}
				catch
				{
				}
				try
				{
					_audioManager.StopBluetoothSco();
				}
				catch
				{
				}
			}
			_audioManager.Mode = Mode.Normal;
		}
		catch
		{
		}
		_activeSelectionId = "automatic";
		return Task.CompletedTask;
	}

	private async Task<bool> EnsureBluetoothPermissionAsync(bool request)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(31))
		{
			return true;
		}
		if (await Permissions.CheckStatusAsync<BluetoothConnectPermission>() == PermissionStatus.Granted)
		{
			return true;
		}
		if (!request)
		{
			return false;
		}
		return await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<BluetoothConnectPermission>()) == PermissionStatus.Granted;
	}

	private AudioDeviceInfo? FindInputDevice(int type, string productName)
	{
		try
		{
			return (_audioManager.GetDevices(GetDevicesTargets.Inputs) ?? Array.Empty<AudioDeviceInfo>()).FirstOrDefault((AudioDeviceInfo device) => device != null && device.IsSource && TypesAreCompatible(type, (int)device.Type) && ProductNamesMatch(productName, SafeProductName(device)));
		}
		catch
		{
			return null;
		}
	}

	private AudioDeviceInfo? FindCommunicationDevice(int type, string productName)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(31))
		{
			return null;
		}
		try
		{
			IList<AudioDeviceInfo> candidates = _audioManager.AvailableCommunicationDevices;
			return candidates.FirstOrDefault((AudioDeviceInfo device) => TypesAreCompatible(type, (int)device.Type) && ProductNamesMatch(productName, SafeProductName(device))) ?? candidates.FirstOrDefault((AudioDeviceInfo device) => TypesAreCompatible(type, (int)device.Type));
		}
		catch
		{
			return null;
		}
	}

	private static bool IsSupportedExternalInput(int type)
	{
		switch (type)
		{
		case 3:
		case 7:
		case 11:
		case 12:
		case 22:
		case 26:
			return true;
		default:
			return false;
		}
	}

	private static bool IsBluetoothType(int type)
	{
		if (type == 7 || type == 26)
		{
			return true;
		}
		return false;
	}

	private static bool TypesAreCompatible(int requested, int available)
	{
		if (requested == available)
		{
			return true;
		}
		return IsBluetoothType(requested) && IsBluetoothType(available);
	}

	private static string BuildDisplayName(int type, string productName)
	{
		string name = (string.IsNullOrWhiteSpace(productName) ? "Dispositivo externo" : productName.Trim());
		if (1 == 0)
		{
		}
		string result;
		switch (type)
		{
		case 7:
			result = name + " · Bluetooth SCO";
			break;
		case 26:
			result = name + " · Bluetooth LE";
			break;
		case 11:
		case 12:
		case 22:
			result = name + " · USB";
			break;
		case 3:
			result = name + " · cable";
			break;
		case 15:
			result = "Micrófono del teléfono";
			break;
		default:
			result = name;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static string SafeProductName(AudioDeviceInfo device)
	{
		try
		{
			string name = device.ProductName?.ToString();
			return string.IsNullOrWhiteSpace(name) ? "Dispositivo externo" : name.Trim();
		}
		catch
		{
			return "Dispositivo externo";
		}
	}

	private static string BuildSelectionId(int type, string productName)
	{
		string encodedName = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(productName ?? string.Empty));
		return $"device|{type}|{encodedName}";
	}

	private static bool TryParseSelectionId(string selectionId, out int type, out string productName)
	{
		type = 0;
		productName = string.Empty;
		string[] parts = selectionId.Split('|');
		if (parts.Length != 3 || !parts[0].Equals("device", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!int.TryParse(parts[1], out type))
		{
			return false;
		}
		try
		{
			productName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ProductNamesMatch(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return true;
		}
		return left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeSelection(string? selectionId)
	{
		return string.IsNullOrWhiteSpace(selectionId) ? "automatic" : selectionId.Trim();
	}
}
