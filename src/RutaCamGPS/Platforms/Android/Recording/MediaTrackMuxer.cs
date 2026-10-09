using System;
using System.IO;
using Android.Media;
using Java.Nio;

namespace GPSCamRoute.Platforms.Android.Recording;

internal static class MediaTrackMuxer
{
	public static void MergeVideoAndAudio(string videoPath, string audioPath, string outputPath)
	{
		if (!File.Exists(videoPath))
		{
			throw new FileNotFoundException("No existe el video temporal CameraX.", videoPath);
		}
		if (!File.Exists(audioPath))
		{
			throw new FileNotFoundException("No existe el audio procesado temporal.", audioPath);
		}
		if (File.Exists(outputPath))
		{
			File.Delete(outputPath);
		}
		using MediaExtractor videoExtractor = new MediaExtractor();
		using MediaExtractor audioExtractor = new MediaExtractor();
		videoExtractor.SetDataSource(videoPath);
		audioExtractor.SetDataSource(audioPath);
		int videoSourceTrack = FindTrack(videoExtractor, "video/");
		int audioSourceTrack = FindTrack(audioExtractor, "audio/");
		if (videoSourceTrack < 0)
		{
			throw new InvalidOperationException("El MP4 temporal no contiene track de video.");
		}
		if (audioSourceTrack < 0)
		{
			throw new InvalidOperationException("El archivo temporal no contiene track de audio.");
		}
		MediaFormat videoFormat = videoExtractor.GetTrackFormat(videoSourceTrack);
		MediaFormat audioFormat = audioExtractor.GetTrackFormat(audioSourceTrack);
		using MediaMuxer muxer = new MediaMuxer(outputPath, MuxerOutputType.Mpeg4);
		int videoDestTrack = muxer.AddTrack(videoFormat);
		int audioDestTrack = muxer.AddTrack(audioFormat);
		muxer.Start();
		videoExtractor.SelectTrack(videoSourceTrack);
		audioExtractor.SelectTrack(audioSourceTrack);
		ByteBuffer videoBuffer = ByteBuffer.AllocateDirect(2097152) ?? throw new InvalidOperationException("No se pudo reservar buffer de video para remux.");
		ByteBuffer audioBuffer = ByteBuffer.AllocateDirect(524288) ?? throw new InvalidOperationException("No se pudo reservar buffer de audio para remux.");
		MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
		long videoDurationUs = long.MaxValue;
		try
		{
			if (videoFormat.ContainsKey("durationUs"))
			{
				videoDurationUs = videoFormat.GetLong("durationUs");
			}
		}
		catch
		{
		}
		bool videoDone = false;
		bool audioDone = false;
		try
		{
			while (!videoDone || !audioDone)
			{
				long videoPts = (videoDone ? long.MaxValue : videoExtractor.SampleTime);
				long audioPts = (audioDone ? long.MaxValue : audioExtractor.SampleTime);
				if (videoPts < 0)
				{
					videoDone = true;
				}
				else if (audioPts < 0 || audioPts > videoDurationUs)
				{
					audioDone = true;
				}
				else if (videoPts <= audioPts)
				{
					videoDone = !WriteCurrentSample(videoExtractor, videoBuffer, muxer, videoDestTrack, info);
				}
				else
				{
					audioDone = !WriteCurrentSample(audioExtractor, audioBuffer, muxer, audioDestTrack, info);
				}
			}
		}
		finally
		{
			try
			{
				muxer.Stop();
			}
			catch
			{
			}
			try
			{
				videoExtractor.UnselectTrack(videoSourceTrack);
			}
			catch
			{
			}
			try
			{
				audioExtractor.UnselectTrack(audioSourceTrack);
			}
			catch
			{
			}
		}
	}

	private static bool WriteCurrentSample(MediaExtractor extractor, ByteBuffer buffer, MediaMuxer muxer, int destinationTrack, MediaCodec.BufferInfo info)
	{
		buffer.Clear();
		int size = extractor.ReadSampleData(buffer, 0);
		if (size < 0)
		{
			return false;
		}
		long pts = extractor.SampleTime;
		if (pts < 0)
		{
			return false;
		}
		info.Set(0, size, pts, (MediaCodecBufferFlags)extractor.SampleFlags);
		muxer.WriteSampleData(destinationTrack, buffer, info);
		return extractor.Advance();
	}

	private static int FindTrack(MediaExtractor extractor, string mimePrefix)
	{
		for (int i = 0; i < extractor.TrackCount; i++)
		{
			MediaFormat format = extractor.GetTrackFormat(i);
			string mime = format.GetString("mime") ?? string.Empty;
			if (mime.StartsWith(mimePrefix, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}
}
