using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Audiofx;
using Java.Nio;

namespace GPSCamRoute.Platforms.Android.Recording;

internal sealed class BluetoothProcessedAudioRecorder : IDisposable
{
	private const int SampleRate = 16000;

	private const int Channels = 1;

	private const int AacBitRate = 64000;

	private const double InputGain = 0.4216965034;

	private const double SoftLimit = 0.92;

	private const double DeEsserThreshold = 0.055;

	private const double DeEsserMaxReduction = 0.5;

	private const double HighPassHz = 3800.0;

	private readonly object _sync = new object();

	private CancellationTokenSource? _cts;

	private Task? _worker;

	private TaskCompletionSource<bool>? _startedTcs;

	private string? _outputPath;

	private string _selectionId = string.Empty;

	private bool _disposed;

	public bool IsRunning
	{
		get
		{
			Task worker = _worker;
			return worker != null && !worker.IsCompleted;
		}
	}

	public string? LastError { get; private set; }

	public static bool IsBluetoothSelection(string? selectionId)
	{
		if (string.IsNullOrWhiteSpace(selectionId))
		{
			return false;
		}
		return selectionId.StartsWith("device|7|", StringComparison.OrdinalIgnoreCase) || selectionId.StartsWith("device|26|", StringComparison.OrdinalIgnoreCase);
	}

	public async Task<bool> StartAsync(string outputPath, string selectionId, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("BluetoothProcessedAudioRecorder");
		}
		if (IsRunning)
		{
			throw new InvalidOperationException("El procesador de audio Bluetooth ya está activo.");
		}
		LastError = null;
		_outputPath = outputPath;
		_selectionId = selectionId ?? string.Empty;
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
		if (File.Exists(outputPath))
		{
			File.Delete(outputPath);
		}
		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_worker = Task.Run(delegate
		{
			CaptureWorker(_cts.Token);
		}, CancellationToken.None);
		try
		{
			return await _startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3L), cancellationToken);
		}
		catch (Exception ex)
		{
			LastError = ex.Message;
			try
			{
				_cts.Cancel();
			}
			catch
			{
			}
			return false;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		Task worker = _worker;
		if (worker == null)
		{
			return;
		}
		try
		{
			_cts?.Cancel();
		}
		catch
		{
		}
		try
		{
			await worker.WaitAsync(TimeSpan.FromSeconds(5L), cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (TimeoutException)
		{
			LastError = "El procesador de audio Bluetooth tardó demasiado en cerrar.";
		}
		catch (Exception ex3)
		{
			Exception ex4 = ex3;
			LastError = ex4.Message;
		}
		finally
		{
			_worker = null;
			_cts?.Dispose();
			_cts = null;
		}
	}

	private void CaptureWorker(CancellationToken token)
	{
		AudioRecord recorder = null;
		AutomaticGainControl agc = null;
		MediaCodec encoder = null;
		MediaMuxer muxer = null;
		bool muxerStarted = false;
		int audioTrackIndex = -1;
		long submittedSamples = 0L;
		try
		{
			int bufferBytes = AudioRecord.GetMinBufferSize(16000, ChannelIn.Front, global::Android.Media.Encoding.Pcm16bit);
			if (bufferBytes <= 0)
			{
				bufferBytes = 16000;
			}
			bufferBytes = Math.Max(bufferBytes, 4096);
			recorder = CreateRecorder(bufferBytes, preferBluetooth: true);
			if (recorder.State != State.Initialized)
			{
				throw new InvalidOperationException("Android no pudo inicializar AudioRecord para Bluetooth.");
			}
			try
			{
				if (AutomaticGainControl.IsAvailable)
				{
					agc = AutomaticGainControl.Create(recorder.AudioSessionId);
					agc?.SetEnabled(enabled: false);
				}
			}
			catch
			{
			}
			encoder = MediaCodec.CreateEncoderByType("audio/mp4a-latm") ?? throw new InvalidOperationException("No se pudo crear el encoder AAC.");
			MediaFormat format = MediaFormat.CreateAudioFormat("audio/mp4a-latm", 16000, 1) ?? throw new InvalidOperationException("No se pudo crear MediaFormat AAC.");
			format.SetInteger("bitrate", 64000);
			format.SetInteger("aac-profile", 2);
			format.SetInteger("max-input-size", bufferBytes * 2);
			encoder.Configure(format, null, null, MediaCodecConfigFlags.Encode);
			encoder.Start();
			muxer = new MediaMuxer(_outputPath ?? throw new InvalidOperationException("Ruta temporal de audio no válida."), MuxerOutputType.Mpeg4);
			recorder.StartRecording();
			if (recorder.RecordingState != RecordState.Recording)
			{
				throw new InvalidOperationException("AudioRecord no entró en estado Recording.");
			}
			_startedTcs?.TrySetResult(result: true);
			short[] samples = new short[Math.Max(1024, bufferBytes / 2)];
			byte[] pcmBytes = new byte[samples.Length * 2];
			MediaCodec.BufferInfo outputInfo = new MediaCodec.BufferInfo();
			double dt = 6.25E-05;
			double rc = 4.188287976102509E-05;
			double hpAlpha = rc / (rc + dt);
			double previousInput = 0.0;
			double previousHigh = 0.0;
			double envelope = 0.0;
			double attackCoeff = Math.Exp(-1.0 / 48.0);
			double releaseCoeff = Math.Exp(-0.00078125);
			int consecutiveReadErrors = 0;
			while (!token.IsCancellationRequested)
			{
				int read = recorder.Read(samples, 0, samples.Length);
				if (read <= 0)
				{
					consecutiveReadErrors++;
					if (consecutiveReadErrors >= 3)
					{
						try
						{
							recorder.Stop();
						}
						catch
						{
						}
						try
						{
							agc?.Release();
						}
						catch
						{
						}
						agc?.Dispose();
						agc = null;
						recorder.Release();
						recorder.Dispose();
						recorder = CreateRecorder(bufferBytes, preferBluetooth: false);
						recorder.StartRecording();
						consecutiveReadErrors = 0;
						Debug.WriteLine("RutaCam AUDIO FIX45 · AudioRecord reiniciado en fallback sin afectar video.");
					}
					else
					{
						Thread.Sleep(15);
					}
					continue;
				}
				consecutiveReadErrors = 0;
				for (int i = 0; i < read; i++)
				{
					double input = (double)samples[i] / 32768.0;
					double scaled = input * 0.4216965034;
					double high = hpAlpha * (previousHigh + scaled - previousInput);
					previousInput = scaled;
					previousHigh = high;
					double absHigh = Math.Abs(high);
					envelope = ((absHigh > envelope) ? (attackCoeff * envelope + (1.0 - attackCoeff) * absHigh) : (releaseCoeff * envelope + (1.0 - releaseCoeff) * absHigh));
					double reduction = 0.0;
					if (envelope > 0.055)
					{
						double excess = (envelope - 0.055) / 0.055;
						reduction = Math.Clamp(excess * 0.3, 0.0, 0.5);
					}
					double low = scaled - high;
					double processed = low + high * (1.0 - reduction);
					processed = Math.Tanh(processed / 0.92) * 0.92;
					processed = Math.Clamp(processed, -0.999, 0.999);
					short sample = (samples[i] = (short)Math.Round(processed * 32767.0));
					pcmBytes[i * 2] = (byte)(sample & 0xFF);
					pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
				}
				QueuePcm(encoder, pcmBytes, read * 2, submittedSamples, endOfStream: false, token);
				submittedSamples += read;
				DrainEncoder(encoder, muxer, outputInfo, ref muxerStarted, ref audioTrackIndex, endOfStream: false);
			}
			QueuePcm(encoder, Array.Empty<byte>(), 0, submittedSamples, endOfStream: true, CancellationToken.None);
			DateTime drainDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2L);
			while (DateTime.UtcNow < drainDeadline && !DrainEncoder(encoder, muxer, outputInfo, ref muxerStarted, ref audioTrackIndex, endOfStream: true))
			{
			}
		}
		catch (Exception ex)
		{
			LastError = ex.ToString();
			_startedTcs?.TrySetResult(result: false);
			Debug.WriteLine($"RutaCam AUDIO FIX45 ERROR · {ex}");
		}
		finally
		{
			try
			{
				if (recorder != null && recorder.RecordingState == RecordState.Recording)
				{
					recorder.Stop();
				}
			}
			catch
			{
			}
			try
			{
				agc?.Release();
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
			try
			{
				encoder?.Stop();
			}
			catch
			{
			}
			try
			{
				encoder?.Release();
			}
			catch
			{
			}
			try
			{
				if (muxerStarted)
				{
					muxer?.Stop();
				}
			}
			catch
			{
			}
			try
			{
				muxer?.Release();
			}
			catch
			{
			}
			agc?.Dispose();
			recorder?.Dispose();
			encoder?.Dispose();
			muxer?.Dispose();
		}
	}

	private AudioRecord CreateRecorder(int bufferBytes, bool preferBluetooth)
	{
		AudioRecord recorder = new AudioRecord(AudioSource.VoiceCommunication, 16000, ChannelIn.Front, global::Android.Media.Encoding.Pcm16bit, bufferBytes);
		if (preferBluetooth)
		{
			try
			{
				AudioDeviceInfo preferred = FindSelectedInput(_selectionId);
				if (preferred != null)
				{
					recorder.SetPreferredDevice(preferred);
				}
			}
			catch
			{
			}
		}
		return recorder;
	}

	private static AudioDeviceInfo? FindSelectedInput(string selectionId)
	{
		if (string.IsNullOrWhiteSpace(selectionId))
		{
			return null;
		}
		string[] parts = selectionId.Split('|');
		if (parts.Length < 3 || !int.TryParse(parts[1], out var requestedType))
		{
			return null;
		}
		string requestedName;
		try
		{
			requestedName = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(string.Join("|", parts.Skip(2))));
		}
		catch
		{
			requestedName = string.Empty;
		}
		Context context = Application.Context;
		if (!(context.GetSystemService("audio") is AudioManager manager))
		{
			return null;
		}
		AudioDeviceInfo[] array = manager.GetDevices(GetDevicesTargets.Inputs) ?? Array.Empty<AudioDeviceInfo>();
		foreach (AudioDeviceInfo device in array)
		{
			int type = (int)device.Type;
			if (type == requestedType)
			{
				string name = device.ProductName?.ToString() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(requestedName) || name.Contains(requestedName, StringComparison.OrdinalIgnoreCase) || requestedName.Contains(name, StringComparison.OrdinalIgnoreCase))
				{
					return device;
				}
			}
		}
		return null;
	}

	private static void QueuePcm(MediaCodec encoder, byte[] bytes, int byteCount, long submittedSamples, bool endOfStream, CancellationToken token)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1L);
		while (DateTime.UtcNow < deadline)
		{
			token.ThrowIfCancellationRequested();
			int index = encoder.DequeueInputBuffer(10000L);
			if (index < 0)
			{
				continue;
			}
			ByteBuffer inputBuffer = encoder.GetInputBuffer(index) ?? throw new InvalidOperationException("Encoder AAC devolvió input buffer nulo.");
			inputBuffer.Clear();
			if (byteCount > 0)
			{
				inputBuffer.Put(bytes, 0, byteCount);
			}
			long presentationUs = submittedSamples * 1000000 / 16000;
			encoder.QueueInputBuffer(index, 0, byteCount, presentationUs, endOfStream ? MediaCodecBufferFlags.EndOfStream : MediaCodecBufferFlags.None);
			return;
		}
		throw new TimeoutException("No hubo input buffer disponible en el encoder AAC.");
	}

	private static bool DrainEncoder(MediaCodec encoder, MediaMuxer muxer, MediaCodec.BufferInfo info, ref bool muxerStarted, ref int trackIndex, bool endOfStream)
	{
		while (true)
		{
			int status = encoder.DequeueOutputBuffer(info, endOfStream ? 10000 : 0);
			if (status == -1)
			{
				return false;
			}
			if (status == -2)
			{
				if (muxerStarted)
				{
					throw new InvalidOperationException("El formato AAC cambió más de una vez.");
				}
				trackIndex = muxer.AddTrack(encoder.OutputFormat);
				muxer.Start();
				muxerStarted = true;
			}
			else if (status >= 0)
			{
				ByteBuffer encoded = encoder.GetOutputBuffer(status);
				if ((encoded != null && info.Size > 0) & muxerStarted)
				{
					encoded.Position(info.Offset);
					encoded.Limit(info.Offset + info.Size);
					muxer.WriteSampleData(trackIndex, encoded, info);
				}
				bool eos = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
				encoder.ReleaseOutputBuffer(status, render: false);
				if (eos)
				{
					break;
				}
			}
		}
		return true;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			try
			{
				_cts?.Cancel();
			}
			catch
			{
			}
			_cts?.Dispose();
		}
	}
}
