using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Database;
using Android.Net;
using Android.OS;
using Android.Provider;
using GPSCamRoute.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class VideoLibraryService
{
	public const string PublicRelativePath = "Movies/RutaCam GPS";

	public Task<VideoPublishResult> PublishAsync(string privateVideoPath, DateTimeOffset startedAt, CancellationToken cancellationToken = default(CancellationToken))
	{
		return PublishAsync(privateVideoPath, startedAt, null, cancellationToken);
	}

	public async Task<VideoPublishResult> PublishAsync(string privateVideoPath, DateTimeOffset startedAt, int? segmentIndex, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(privateVideoPath))
		{
			return VideoPublishResult.Failed("No se recibió una ruta de video válida.");
		}
		if (!File.Exists(privateVideoPath))
		{
			return VideoPublishResult.Failed("El archivo MP4 no existe en el almacenamiento privado.");
		}
		FileInfo sourceInfo = new FileInfo(privateVideoPath);
		if (sourceInfo.Length <= 0)
		{
			return VideoPublishResult.Failed("El archivo MP4 está vacío.");
		}
		if (!OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			return VideoPublishResult.Failed("La publicación automática en Galería requiere Android 10 o superior.");
		}
		Context context = Application.Context;
		ContentResolver resolver = context.ContentResolver;
		Android.Net.Uri collection = MediaStore.Video.Media.ExternalContentUri;
		if (resolver == null || collection == null)
		{
			return VideoPublishResult.Failed("Android MediaStore no está disponible.");
		}
		DateTimeOffset localStart = ((startedAt == default(DateTimeOffset)) ? DateTimeOffset.Now : startedAt.ToLocalTime());
		object obj;
		if (segmentIndex.HasValue)
		{
			int index = segmentIndex.GetValueOrDefault();
			if (index > 0)
			{
				obj = $"_{index:000}";
				goto IL_018f;
			}
		}
		obj = string.Empty;
		goto IL_018f;
		IL_018f:
		string suffix = (string)obj;
		string displayName = $"RutaCam_{localStart:yyyy-MM-dd_HH-mm-ss}{suffix}.mp4";
		ContentValues values = new ContentValues();
		values.Put("_display_name", displayName);
		values.Put("mime_type", "video/mp4");
		values.Put("relative_path", "Movies/RutaCam GPS");
		values.Put("is_pending", 1);
		Android.Net.Uri itemUri = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			itemUri = resolver.Insert(collection, values) ?? throw new IOException("MediaStore no pudo crear el video público.");
			await using (FileStream input = File.OpenRead(privateVideoPath))
			{
				await using Stream output = resolver.OpenOutputStream(itemUri, "w") ?? throw new IOException("No se pudo abrir el destino de MediaStore.");
				await input.CopyToAsync(output, 1048576, cancellationToken);
				await output.FlushAsync(cancellationToken);
			}
			ContentValues publishValues = new ContentValues();
			publishValues.Put("is_pending", 0);
			resolver.Update(itemUri, publishValues, null, null);
			long publishedSize = QuerySize(resolver, itemUri, sourceInfo.Length);
			if (publishedSize <= 0)
			{
				throw new IOException("El video fue creado en Galería, pero Android reportó un tamaño inválido.");
			}
			try
			{
				File.Delete(privateVideoPath);
			}
			catch
			{
			}
			return new VideoPublishResult(Success: true, itemUri.ToString() ?? string.Empty, displayName, "Movies/RutaCam GPS", publishedSize);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			if (itemUri != null)
			{
				try
				{
					resolver.Delete(itemUri, null, null);
				}
				catch
				{
				}
			}
			return VideoPublishResult.Failed(ex2.Message);
		}
	}

	public async Task MigrateLegacyVideosAsync(RouteStorageService storage, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<RouteRecord> records = await storage.GetAllAsync();
		HashSet<string> referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (RouteRecord record in records)
		{
			cancellationToken.ThrowIfCancellationRequested();
			bool changed = false;
			foreach (VideoSegment segment in record.VideoSegments)
			{
				if (!string.IsNullOrWhiteSpace(segment.VideoPath))
				{
					referencedPaths.Add(segment.VideoPath);
				}
				if (!segment.HasPublicVideo && segment.HasPrivateVideo)
				{
					VideoPublishResult result = await PublishAsync(segment.VideoPath, record.StartedAt, segment.Index, cancellationToken);
					if (result.Success)
					{
						ApplyPublishResult(segment, result);
						changed = true;
					}
				}
			}
			if (!string.IsNullOrWhiteSpace(record.VideoPath))
			{
				referencedPaths.Add(record.VideoPath);
			}
			if (record.VideoSegments.Count == 0 && string.IsNullOrWhiteSpace(record.PublicVideoUri) && !string.IsNullOrWhiteSpace(record.VideoPath) && File.Exists(record.VideoPath))
			{
				VideoPublishResult result2 = await PublishAsync(record.VideoPath, record.StartedAt, cancellationToken);
				if (result2.Success)
				{
					ApplyPublishResult(record, result2);
					changed = true;
				}
			}
			if (changed)
			{
				await storage.SaveAsync(record);
			}
		}
		string privateVideosDirectory = Path.Combine(FileSystem.AppDataDirectory, "Videos");
		if (!Directory.Exists(privateVideosDirectory))
		{
			return;
		}
		foreach (string file in Directory.EnumerateFiles(privateVideosDirectory, "*.mp4"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!referencedPaths.Contains(file) && File.Exists(file))
			{
				DateTimeOffset timestamp = new DateTimeOffset(File.GetCreationTime(file));
				await PublishAsync(file, timestamp, cancellationToken);
			}
		}
	}

	public Task OpenVideoAsync(RouteRecord record)
	{
		VideoSegment segment = record.VideoSegments.FirstOrDefault((VideoSegment x) => x.HasPublicVideo);
		if (segment != null)
		{
			return OpenSegmentAsync(segment);
		}
		if (string.IsNullOrWhiteSpace(record.PublicVideoUri))
		{
			throw new InvalidOperationException("Este recorrido todavía no tiene un video publicado en Galería.");
		}
		return OpenPublicUriAsync(record.PublicVideoUri);
	}

	public Task OpenSegmentAsync(VideoSegment segment)
	{
		if (!segment.HasPublicVideo)
		{
			throw new InvalidOperationException("Este segmento todavía no está publicado en Galería.");
		}
		return OpenPublicUriAsync(segment.PublicVideoUri);
	}

	public Task ShareVideoAsync(RouteRecord record)
	{
		List<VideoSegment> publicSegments = record.VideoSegments.Where((VideoSegment x) => x.HasPublicVideo).ToList();
		if (publicSegments.Count > 1)
		{
			return ShareAllVideosAsync(record);
		}
		if (publicSegments.Count == 1)
		{
			return ShareSegmentAsync(publicSegments[0]);
		}
		if (string.IsNullOrWhiteSpace(record.PublicVideoUri))
		{
			throw new InvalidOperationException("Este recorrido todavía no tiene un video publicado en Galería.");
		}
		return SharePublicUrisAsync(new[] { record.PublicVideoUri });
	}

	public Task ShareSegmentAsync(VideoSegment segment)
	{
		if (!segment.HasPublicVideo)
		{
			throw new InvalidOperationException("Este segmento todavía no está publicado en Galería.");
		}
		return SharePublicUrisAsync(new[] { segment.PublicVideoUri });
	}

	public Task ShareAllVideosAsync(RouteRecord record)
	{
		List<string> uris = (from x in record.VideoSegments
			where x.HasPublicVideo
			select x.PublicVideoUri into x
			where !string.IsNullOrWhiteSpace(x)
			select x).ToList();
		if (uris.Count == 0 && !string.IsNullOrWhiteSpace(record.PublicVideoUri))
		{
			uris.Add(record.PublicVideoUri);
		}
		if (uris.Count == 0)
		{
			throw new InvalidOperationException("No hay videos publicados para compartir.");
		}
		return SharePublicUrisAsync(uris);
	}

	public Task<bool> DeleteSegmentVideoAsync(VideoSegment segment)
	{
		if (segment == null)
		{
			return Task.FromResult(result: false);
		}
		bool success = true;
		if (!string.IsNullOrWhiteSpace(segment.PublicVideoUri))
		{
			try
			{
				Android.Net.Uri uri = Android.Net.Uri.Parse(segment.PublicVideoUri);
				if (uri == null)
				{
					success = false;
				}
				else
				{
					ContentResolver resolver = Application.Context.ContentResolver;
					if (resolver == null)
					{
						success = false;
					}
					else
					{
						resolver.Delete(uri, null, null);
					}
				}
			}
			catch
			{
				success = false;
			}
		}
		if (!string.IsNullOrWhiteSpace(segment.VideoPath) && File.Exists(segment.VideoPath))
		{
			try
			{
				File.Delete(segment.VideoPath);
			}
			catch
			{
				success = false;
			}
		}
		return Task.FromResult(success);
	}

	public Task<bool> DeleteLegacyVideoAsync(RouteRecord record)
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		bool success = true;
		if (!string.IsNullOrWhiteSpace(record.PublicVideoUri))
		{
			try
			{
				Android.Net.Uri uri = Android.Net.Uri.Parse(record.PublicVideoUri);
				ContentResolver resolver = Application.Context.ContentResolver;
				if (uri == null || resolver == null)
				{
					success = false;
				}
				else
				{
					resolver.Delete(uri, null, null);
				}
			}
			catch
			{
				success = false;
			}
		}
		if (!string.IsNullOrWhiteSpace(record.VideoPath) && File.Exists(record.VideoPath))
		{
			try
			{
				File.Delete(record.VideoPath);
			}
			catch
			{
				success = false;
			}
		}
		if (success)
		{
			ClearVideoReference(record);
		}
		return Task.FromResult(success);
	}

	public async Task<(int Deleted, int Failed)> DeleteAllRouteVideosAsync(RouteRecord record)
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		int deleted = 0;
		int failed = 0;
		foreach (VideoSegment segment in record.VideoSegments.ToList())
		{
			if (await DeleteSegmentVideoAsync(segment))
			{
				record.VideoSegments.Remove(segment);
				deleted++;
			}
			else
			{
				failed++;
			}
		}
		if (!string.IsNullOrWhiteSpace(record.PublicVideoUri) || (!string.IsNullOrWhiteSpace(record.VideoPath) && File.Exists(record.VideoPath)))
		{
			if (await DeleteLegacyVideoAsync(record))
			{
				deleted++;
			}
			else
			{
				failed++;
			}
		}
		return (Deleted: deleted, Failed: failed);
	}

	public static void ClearVideoReference(VideoSegment segment)
	{
		ArgumentNullException.ThrowIfNull(segment, "segment");
		segment.VideoPath = string.Empty;
		segment.PublicVideoUri = string.Empty;
		segment.DisplayName = string.Empty;
		segment.RelativePath = string.Empty;
		segment.SizeBytes = 0L;
		segment.IsProtected = false;
		segment.ProtectedAt = null;
	}

	public static void ClearVideoReference(RouteRecord record)
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		record.VideoPath = string.Empty;
		record.PublicVideoUri = string.Empty;
		record.VideoDisplayName = string.Empty;
		record.VideoRelativePath = string.Empty;
		record.VideoSizeBytes = 0L;
	}

	public static void ApplyPublishResult(RouteRecord record, VideoPublishResult result)
	{
		if (result.Success)
		{
			record.PublicVideoUri = result.PublicUri;
			record.VideoDisplayName = result.DisplayName;
			record.VideoRelativePath = result.RelativePath;
			record.VideoSizeBytes = result.SizeBytes;
			record.VideoPath = string.Empty;
		}
	}

	public static void ApplyPublishResult(VideoSegment segment, VideoPublishResult result)
	{
		if (result.Success)
		{
			segment.PublicVideoUri = result.PublicUri;
			segment.DisplayName = result.DisplayName;
			segment.RelativePath = result.RelativePath;
			segment.SizeBytes = result.SizeBytes;
			segment.VideoPath = string.Empty;
		}
	}

	private static Task OpenPublicUriAsync(string publicUri)
	{
		Android.Net.Uri uri = Android.Net.Uri.Parse(publicUri) ?? throw new InvalidOperationException("La dirección del video no es válida.");
		Intent intent = new Intent("android.intent.action.VIEW");
		intent.SetDataAndType(uri, "video/mp4");
		intent.AddFlags(ActivityFlags.GrantReadUriPermission);
		Activity activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No se encontró una actividad Android visible.");
		activity.StartActivity(Intent.CreateChooser(intent, "Abrir video con"));
		return Task.CompletedTask;
	}

	private static Task SharePublicUrisAsync(IReadOnlyList<string> publicUris)
	{
		List<Android.Net.Uri> parsed = (from value in publicUris
			select Android.Net.Uri.Parse(value) into x
			where x != null
			select x).Cast<Android.Net.Uri>().ToList();
		if (parsed.Count == 0)
		{
			throw new InvalidOperationException("No hay videos válidos para compartir.");
		}
		Activity activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No se encontró una actividad Android visible.");
		if (parsed.Count == 1)
		{
			Intent intent = new Intent("android.intent.action.SEND");
			intent.SetType("video/mp4");
			intent.PutExtra("android.intent.extra.STREAM", parsed[0]);
			intent.ClipData = ClipData.NewRawUri("RutaCam GPS", parsed[0]);
			intent.AddFlags(ActivityFlags.GrantReadUriPermission);
			activity.StartActivity(Intent.CreateChooser(intent, "Compartir video"));
			return Task.CompletedTask;
		}
		Intent sendMultiple = new Intent("android.intent.action.SEND_MULTIPLE");
		sendMultiple.SetType("video/mp4");
		List<IParcelable> parcelables = new List<IParcelable>(parsed.Count);
		foreach (Android.Net.Uri uri in parsed)
		{
			parcelables.Add(uri);
		}
		sendMultiple.PutParcelableArrayListExtra("android.intent.extra.STREAM", parcelables);
		sendMultiple.ClipData = ClipData.NewRawUri("RutaCam GPS", parsed[0]);
		for (int i = 1; i < parsed.Count; i++)
		{
			sendMultiple.ClipData?.AddItem(new ClipData.Item(parsed[i]));
		}
		sendMultiple.AddFlags(ActivityFlags.GrantReadUriPermission);
		activity.StartActivity(Intent.CreateChooser(sendMultiple, "Compartir videos de RutaCam"));
		return Task.CompletedTask;
	}

	private static long QuerySize(ContentResolver resolver, Android.Net.Uri uri, long fallbackSize)
	{
		try
		{
			using ParcelFileDescriptor descriptor = resolver.OpenFileDescriptor(uri, "r");
			if (descriptor != null && descriptor.StatSize > 0)
			{
				return descriptor.StatSize;
			}
		}
		catch
		{
		}
		try
		{
			using ICursor cursor = resolver.Query(uri, new string[1] { "_size" }, null, null, null);
			if (cursor != null && cursor.MoveToFirst())
			{
				long size = cursor.GetLong(0);
				if (size > 0)
				{
					return size;
				}
			}
		}
		catch
		{
		}
		try
		{
			using Stream input = resolver.OpenInputStream(uri);
			if (input != null && input.ReadByte() >= 0)
			{
				return fallbackSize;
			}
		}
		catch
		{
		}
		return 0L;
	}
}
