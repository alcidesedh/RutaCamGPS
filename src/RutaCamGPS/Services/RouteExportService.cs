using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using GPSCamRoute.Models;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace GPSCamRoute.Services;

public sealed class RouteExportService
{
	public async Task ShareGpxAsync(RouteRecord record, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = await CreateGpxAsync(record, cancellationToken);
		await Share.Default.RequestAsync(new ShareFileRequest
		{
			Title = "Exportar recorrido GPX",
			File = new ShareFile(path, "application/gpx+xml")
		});
	}

	public async Task ShareCsvAsync(RouteRecord record, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = await CreateCsvAsync(record, cancellationToken);
		await Share.Default.RequestAsync(new ShareFileRequest
		{
			Title = "Exportar recorrido CSV",
			File = new ShareFile(path, "text/csv")
		});
	}

	public Task<string> CreateGpxAsync(RouteRecord record, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		if (record.Points.Count == 0)
		{
			throw new InvalidOperationException("Este recorrido no contiene puntos GPS para exportar.");
		}
		string path = Path.Combine(FileSystem.CacheDirectory, BuildFileName(record, "gpx"));
		XmlWriterSettings settings = new XmlWriterSettings
		{
			Async = true,
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = true,
			OmitXmlDeclaration = false
		};
		return WriteGpxAsync(path, record, settings, cancellationToken);
	}

	public async Task<string> CreateCsvAsync(RouteRecord record, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(record, "record");
		if (record.Points.Count == 0)
		{
			throw new InvalidOperationException("Este recorrido no contiene puntos GPS para exportar.");
		}
		string path = Path.Combine(FileSystem.CacheDirectory, BuildFileName(record, "csv"));
		string result;
		await using (FileStream stream = File.Create(path))
		{
			string text;
			await using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
			{
				await writer.WriteLineAsync("timestamp_utc,latitude,longitude,altitude_meters,speed_kmh,reported_speed_kmh,displacement_speed_kmh,accuracy_meters,vertical_accuracy_meters,course_degrees,fix_age_ms,reduced_accuracy,is_mock_provider");
				foreach (RoutePoint point in record.Points)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string row = string.Join(',',
						point.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
						point.Latitude.ToString("F7", CultureInfo.InvariantCulture),
						point.Longitude.ToString("F7", CultureInfo.InvariantCulture),
						point.AltitudeMeters?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
						point.SpeedKmh.ToString("F2", CultureInfo.InvariantCulture),
						point.ReportedSpeedKmh?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty,
						point.DisplacementSpeedKmh?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty,
						point.AccuracyMeters?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
						point.VerticalAccuracyMeters?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
						point.CourseDegrees?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
						point.FixAgeMilliseconds?.ToString("F0", CultureInfo.InvariantCulture) ?? string.Empty,
						point.ReducedAccuracy ? "true" : "false",
						point.IsFromMockProvider ? "true" : "false");
					await writer.WriteLineAsync(row);
				}
				await writer.FlushAsync(cancellationToken);
				text = path;
			}
			result = text;
		}
		return result;
	}

	private static async Task<string> WriteGpxAsync(string path, RouteRecord record, XmlWriterSettings settings, CancellationToken cancellationToken)
	{
		await using (FileStream stream = File.Create(path))
		{
			using XmlWriter writer = XmlWriter.Create(stream, settings);
			await writer.WriteStartDocumentAsync();
			await writer.WriteStartElementAsync(null, "gpx", "http://www.topografix.com/GPX/1/1");
			await writer.WriteAttributeStringAsync(null, "version", null, "1.1");
			await writer.WriteAttributeStringAsync(null, "creator", null, "RutaCam GPS 2.5.2");
			await writer.WriteStartElementAsync(null, "metadata", null);
			await writer.WriteElementStringAsync(null, "name", null, $"RutaCam {record.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
			await writer.WriteElementStringAsync(null, "time", null, record.StartedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
			await writer.WriteEndElementAsync();
			await writer.WriteStartElementAsync(null, "trk", null);
			await writer.WriteElementStringAsync(null, "name", null, $"RutaCam {record.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
			await writer.WriteStartElementAsync(null, "trkseg", null);
			foreach (RoutePoint point in record.Points)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await writer.WriteStartElementAsync(null, "trkpt", null);
				await writer.WriteAttributeStringAsync(null, "lat", null, point.Latitude.ToString("F7", CultureInfo.InvariantCulture));
				await writer.WriteAttributeStringAsync(null, "lon", null, point.Longitude.ToString("F7", CultureInfo.InvariantCulture));
				if (point.AltitudeMeters.HasValue)
				{
					await writer.WriteElementStringAsync(null, "ele", null, point.AltitudeMeters.Value.ToString("F1", CultureInfo.InvariantCulture));
				}
				await writer.WriteElementStringAsync(null, "time", null, point.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
				await writer.WriteStartElementAsync(null, "extensions", null);
				await writer.WriteElementStringAsync(null, "speedKmh", null, point.SpeedKmh.ToString("F2", CultureInfo.InvariantCulture));
				if (point.ReportedSpeedKmh.HasValue)
				{
					await writer.WriteElementStringAsync(null, "reportedSpeedKmh", null, point.ReportedSpeedKmh.Value.ToString("F2", CultureInfo.InvariantCulture));
				}
				if (point.DisplacementSpeedKmh.HasValue)
				{
					await writer.WriteElementStringAsync(null, "displacementSpeedKmh", null, point.DisplacementSpeedKmh.Value.ToString("F2", CultureInfo.InvariantCulture));
				}
				if (point.AccuracyMeters.HasValue)
				{
					await writer.WriteElementStringAsync(null, "accuracyMeters", null, point.AccuracyMeters.Value.ToString("F1", CultureInfo.InvariantCulture));
				}
				if (point.VerticalAccuracyMeters.HasValue)
				{
					await writer.WriteElementStringAsync(null, "verticalAccuracyMeters", null, point.VerticalAccuracyMeters.Value.ToString("F1", CultureInfo.InvariantCulture));
				}
				if (point.CourseDegrees.HasValue)
				{
					await writer.WriteElementStringAsync(null, "courseDegrees", null, point.CourseDegrees.Value.ToString("F1", CultureInfo.InvariantCulture));
				}
				if (point.FixAgeMilliseconds.HasValue)
				{
					await writer.WriteElementStringAsync(null, "fixAgeMs", null, point.FixAgeMilliseconds.Value.ToString("F0", CultureInfo.InvariantCulture));
				}
				await writer.WriteEndElementAsync();
				await writer.WriteEndElementAsync();
			}
			await writer.WriteEndElementAsync();
			await writer.WriteEndElementAsync();
			await writer.WriteEndElementAsync();
			await writer.WriteEndDocumentAsync();
			await writer.FlushAsync();
		}
		return path;
	}

	private static string BuildFileName(RouteRecord record, string extension)
	{
		return $"RutaCam_{record.StartedAt.ToLocalTime():yyyy-MM-dd_HH-mm-ss}.{extension}";
	}
}
