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

public sealed class RouteStorageService
{
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private string RecordsPath => Path.Combine(FileSystem.AppDataDirectory, "routes.json");

	private string BackupPath => RecordsPath + ".bak";

	private string TempPath => RecordsPath + ".tmp";

	public async Task<IReadOnlyList<RouteRecord>> GetAllAsync()
	{
		await _gate.WaitAsync();
		try
		{
			return (await ReadRecordsWithFallbackAsync()).OrderByDescending((RouteRecord x) => x.StartedAt).ToList();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SaveAsync(RouteRecord record)
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		await _gate.WaitAsync();
		try
		{
			List<RouteRecord> records = await ReadRecordsWithFallbackAsync();
			records.RemoveAll((RouteRecord x) => x.Id == record.Id);
			records.Add(record);
			await WriteRecordsAtomicAsync(records.OrderByDescending((RouteRecord x) => x.StartedAt).ToList());
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DeleteAsync(Guid id)
	{
		await _gate.WaitAsync();
		try
		{
			List<RouteRecord> records = await ReadRecordsWithFallbackAsync();
			if (records.RemoveAll((RouteRecord x) => x.Id == id) != 0)
			{
				await WriteRecordsAtomicAsync(records.OrderByDescending((RouteRecord x) => x.StartedAt).ToList());
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DeleteAllAsync()
	{
		await _gate.WaitAsync();
		try
		{
			DeleteIfExists(TempPath);
			DeleteIfExists(RecordsPath);
			DeleteIfExists(BackupPath);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<List<RouteRecord>> ReadRecordsWithFallbackAsync()
	{
		List<RouteRecord> primary = await TryReadRecordsAsync(RecordsPath);
		if (primary != null)
		{
			return primary;
		}
		return (await TryReadRecordsAsync(BackupPath)) ?? new List<RouteRecord>();
	}

	private async Task<List<RouteRecord>?> TryReadRecordsAsync(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			List<RouteRecord> result;
			await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
			{
				result = (await JsonSerializer.DeserializeAsync<List<RouteRecord>>((Stream)stream, _jsonOptions, default(CancellationToken))) ?? new List<RouteRecord>();
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private async Task WriteRecordsAtomicAsync(IReadOnlyList<RouteRecord> records)
	{
		Directory.CreateDirectory(FileSystem.AppDataDirectory);
		DeleteIfExists(TempPath);
		await using (FileStream stream = new FileStream(TempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
		{
			await JsonSerializer.SerializeAsync((Stream)stream, records, _jsonOptions, default(CancellationToken));
			await stream.FlushAsync();
		}
		if (await TryReadRecordsAsync(RecordsPath) != null)
		{
			File.Copy(RecordsPath, BackupPath, overwrite: true);
		}
		File.Move(TempPath, RecordsPath, overwrite: true);
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
