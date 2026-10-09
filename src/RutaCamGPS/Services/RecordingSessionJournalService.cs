using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GPSCamRoute.Models;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class RecordingSessionJournalService
{
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private readonly RouteStorageService _routeStorage;

	private readonly VideoLibraryService _videoLibrary;

	private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private string JournalPath => Path.Combine(FileSystem.AppDataDirectory, "active_recording.json");

	private string BackupPath => JournalPath + ".bak";

	private string TempPath => JournalPath + ".tmp";

	public RecordingSessionJournalService(RouteStorageService routeStorage, VideoLibraryService videoLibrary)
	{
		_routeStorage = routeStorage;
		_videoLibrary = videoLibrary;
	}

	public async Task SaveCheckpointAsync(ActiveRecordingSession session, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(session, "session");
		await _gate.WaitAsync(cancellationToken);
		try
		{
			session.UpdatedAt = DateTimeOffset.UtcNow;
			await WriteAtomicAsync(session, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task ClearAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			DeleteIfExists(TempPath);
			DeleteIfExists(JournalPath);
			DeleteIfExists(BackupPath);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<InterruptedSessionRecoveryResult> RecoverInterruptedAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			ActiveRecordingSession session = await ReadJournalWithFallbackAsync(cancellationToken);
			if (session == null)
			{
				return InterruptedSessionRecoveryResult.None;
			}
			if (session.RouteId == Guid.Empty || session.StartedAt == default(DateTimeOffset))
			{
				return new InterruptedSessionRecoveryResult(Found: true, Recovered: false, null, "Se encontró un diario de grabación, pero no contiene una identificación o fecha de inicio válida. El archivo se conservó para revisión.");
			}
			List<VideoSegment> segments = CloneSegments(session.VideoSegments);
			List<string> recoveryNotes = new List<string>();
			if (session.UsesVideoSegments)
			{
				AddPrivateSegmentCandidates(session, segments);
				foreach (VideoSegment segment in segments.OrderBy((VideoSegment x) => x.Index))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (!segment.HasPublicVideo && segment.HasPrivateVideo)
					{
						VideoPublishResult result = await _videoLibrary.PublishAsync(segment.VideoPath, session.StartedAt, (segment.Index > 0) ? new int?(segment.Index) : ((int?)null), cancellationToken);
						if (result.Success)
						{
							VideoLibraryService.ApplyPublishResult(segment, result);
							continue;
						}
						recoveryNotes.Add($"Clip {Math.Max(1, segment.Index):000}: {result.ErrorMessage}");
					}
				}
			}
			DateTimeOffset endedAt = ResolveEndedAt(session, segments);
			TimeSpan duration = ((endedAt > session.StartedAt) ? (endedAt - session.StartedAt) : TimeSpan.Zero);
			RouteRecord record = new RouteRecord
			{
				Id = session.RouteId,
				StartedAt = session.StartedAt,
				EndedAt = endedAt,
				DistanceKm = Math.Max(0.0, session.DistanceKm),
				AverageSpeedKmh = ((duration.TotalHours > 0.0) ? (Math.Max(0.0, session.DistanceKm) / duration.TotalHours) : 0.0),
				MaxSpeedKmh = Math.Max(0.0, session.MaxSpeedKmh),
				RecordingEngine = (string.IsNullOrWhiteSpace(session.RecordingEngine) ? "Sesión recuperada" : session.RecordingEngine),
				LoopRecordingMode = (string.IsNullOrWhiteSpace(session.LoopRecordingMode) ? "Desactivada" : session.LoopRecordingMode),
				DeletedByLoopCount = Math.Max(0, session.DeletedByLoopCount),
				VideoSegments = (session.UsesVideoSegments ? segments.OrderBy((VideoSegment x) => x.Index).ToList() : new List<VideoSegment>()),
				Points = ClonePoints(session.Points),
				WasRecovered = true,
				RecoveryNote = ((recoveryNotes.Count == 0) ? "Ruta recuperada automáticamente después de una interrupción inesperada." : ("Ruta recuperada. Algunos videos permanecen en almacenamiento privado: " + string.Join(" | ", recoveryNotes)))
			};
			if (!session.UsesVideoSegments && !string.IsNullOrWhiteSpace(session.CurrentVideoPath) && File.Exists(session.CurrentVideoPath))
			{
				VideoPublishResult result2 = await _videoLibrary.PublishAsync(session.CurrentVideoPath, session.StartedAt, cancellationToken);
				if (result2.Success)
				{
					VideoLibraryService.ApplyPublishResult(record, result2);
				}
				else
				{
					record.VideoPath = session.CurrentVideoPath;
					record.RecoveryNote = record.RecoveryNote + " Video pendiente: " + result2.ErrorMessage;
				}
			}
			await _routeStorage.SaveAsync(record);
			DeleteIfExists(TempPath);
			DeleteIfExists(JournalPath);
			DeleteIfExists(BackupPath);
			return new InterruptedSessionRecoveryResult(Found: true, Recovered: true, record, record.RecoveryNote);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			return new InterruptedSessionRecoveryResult(Found: true, Recovered: false, null, "No se pudo recuperar la sesión interrumpida: " + ex2.Message + ". El diario se conservó para volver a intentarlo.");
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task WriteAtomicAsync(ActiveRecordingSession session, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(FileSystem.AppDataDirectory);
		DeleteIfExists(TempPath);
		await using (FileStream stream = new FileStream(TempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
		{
			await JsonSerializer.SerializeAsync((Stream)stream, session, _jsonOptions, cancellationToken);
			await stream.FlushAsync(cancellationToken);
		}
		if (await TryReadAsync(JournalPath, cancellationToken) != null)
		{
			File.Copy(JournalPath, BackupPath, overwrite: true);
		}
		File.Move(TempPath, JournalPath, overwrite: true);
	}

	private async Task<ActiveRecordingSession?> ReadJournalWithFallbackAsync(CancellationToken cancellationToken)
	{
		ActiveRecordingSession primary = await TryReadAsync(JournalPath, cancellationToken);
		if (primary != null)
		{
			return primary;
		}
		return await TryReadAsync(BackupPath, cancellationToken);
	}

	private async Task<ActiveRecordingSession?> TryReadAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			ActiveRecordingSession result;
			await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
			{
				result = await JsonSerializer.DeserializeAsync<ActiveRecordingSession>((Stream)stream, _jsonOptions, cancellationToken);
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static void AddPrivateSegmentCandidates(ActiveRecordingSession session, List<VideoSegment> segments)
	{
		HashSet<int> protectedIndices = session.ProtectedSegmentIndices.ToHashSet();
		HashSet<string> knownPaths = (from x in segments
			where !string.IsNullOrWhiteSpace(x.VideoPath)
			select x.VideoPath).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<int> knownIndices = segments.Select((VideoSegment x) => x.Index).ToHashSet();
		List<string> candidates = new List<string>();
		if (!string.IsNullOrWhiteSpace(session.CurrentVideoPath))
		{
			candidates.Add(session.CurrentVideoPath);
		}
		if (!string.IsNullOrWhiteSpace(session.SegmentFileStem))
		{
			string directory = Path.Combine(FileSystem.AppDataDirectory, "Videos");
			if (Directory.Exists(directory))
			{
				candidates.AddRange(Directory.EnumerateFiles(directory, session.SegmentFileStem + "*.mp4"));
			}
		}
		foreach (string path in candidates.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!File.Exists(path) || knownPaths.Contains(path))
			{
				continue;
			}
			FileInfo info;
			try
			{
				info = new FileInfo(path);
				if (info.Length <= 0)
				{
					continue;
				}
			}
			catch
			{
				continue;
			}
			int index;
			for (index = ResolveSegmentIndex(path, session); knownIndices.Contains(index); index++)
			{
			}
			DateTimeOffset startedAt = ((index == session.CurrentSegmentIndex && session.CurrentSegmentStartedAt != default(DateTimeOffset)) ? session.CurrentSegmentStartedAt : new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero));
			if (startedAt == default(DateTimeOffset) || startedAt < session.StartedAt)
			{
				startedAt = session.StartedAt;
			}
			DateTimeOffset endedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
			if (endedAt <= startedAt)
			{
				endedAt = ((session.UpdatedAt > startedAt) ? session.UpdatedAt : startedAt);
			}
			segments.Add(new VideoSegment
			{
				Index = Math.Max(1, index),
				StartedAt = startedAt,
				EndedAt = endedAt,
				VideoPath = path,
				IsProtected = protectedIndices.Contains(index),
				ProtectedAt = (protectedIndices.Contains(index) ? new DateTimeOffset?(session.UpdatedAt) : ((DateTimeOffset?)null))
			});
			knownPaths.Add(path);
			knownIndices.Add(index);
		}
	}

	private static int ResolveSegmentIndex(string path, ActiveRecordingSession session)
	{
		string fileName = Path.GetFileNameWithoutExtension(path);
		if (!string.IsNullOrWhiteSpace(session.SegmentFileStem) && fileName.StartsWith(session.SegmentFileStem + "_", StringComparison.OrdinalIgnoreCase))
		{
			string suffix = fileName.Substring(session.SegmentFileStem.Length + 1);
			if (int.TryParse(suffix, out var parsed) && parsed > 0)
			{
				return parsed;
			}
		}
		return (session.CurrentSegmentIndex <= 0) ? 1 : session.CurrentSegmentIndex;
	}

	private static DateTimeOffset ResolveEndedAt(ActiveRecordingSession session, IReadOnlyList<VideoSegment> segments)
	{
		List<DateTimeOffset> candidates = new List<DateTimeOffset> { session.StartedAt, session.UpdatedAt };
		if (session.Points.Count > 0)
		{
			candidates.Add(session.Points.Max((RoutePoint x) => x.Timestamp));
		}
		if (segments.Count > 0)
		{
			candidates.Add(segments.Max((VideoSegment x) => x.EndedAt));
		}
		return candidates.Where((DateTimeOffset x) => x != default(DateTimeOffset)).DefaultIfEmpty(session.StartedAt).Max();
	}

	private static List<RoutePoint> ClonePoints(IEnumerable<RoutePoint> source)
	{
		return source.Select((RoutePoint x) => new RoutePoint
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

	private static List<VideoSegment> CloneSegments(IEnumerable<VideoSegment> source)
	{
		return source.Select((VideoSegment x) => new VideoSegment
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

	private static void DeleteIfExists(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
