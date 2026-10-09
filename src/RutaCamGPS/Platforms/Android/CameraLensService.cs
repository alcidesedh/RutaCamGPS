using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Camera2;
using GPSCamRoute.Models;
using GPSCamRoute.Services;

namespace GPSCamRoute.Platforms.Android;

public sealed class CameraLensService : ICameraLensService
{
	public Task<IReadOnlyList<CameraLensOption>> GetRearLensOptionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<CameraLensOption> result = new List<CameraLensOption>
		{
			new CameraLensOption
			{
				DisplayName = "Automática · cámara trasera",
				PhysicalCameraId = string.Empty
			}
		};
		if (!OperatingSystem.IsAndroidVersionAtLeast(28))
		{
			return Task.FromResult((IReadOnlyList<CameraLensOption>)result);
		}
		try
		{
			Context context = Application.Context;
			if (!(context.GetSystemService("camera") is CameraManager manager))
			{
				return Task.FromResult((IReadOnlyList<CameraLensOption>)result);
			}
			SortedSet<string> physicalIds = new SortedSet<string>(StringComparer.Ordinal);
			string[] cameraIdList = manager.GetCameraIdList();
			foreach (string logicalId in cameraIdList)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CameraCharacteristics characteristics = manager.GetCameraCharacteristics(logicalId);
				string facingValue = characteristics.Get(CameraCharacteristics.LensFacing)?.ToString();
				if (!string.Equals(facingValue, "1", StringComparison.Ordinal))
				{
					continue;
				}
				foreach (string id in characteristics.PhysicalCameraIds)
				{
					if (!string.IsNullOrWhiteSpace(id))
					{
						physicalIds.Add(id);
					}
				}
			}
			int index = 1;
			foreach (string id2 in physicalIds)
			{
				result.Add(new CameraLensOption
				{
					DisplayName = $"Lente física trasera {index} · ID {id2}",
					PhysicalCameraId = id2
				});
				index++;
			}
		}
		catch
		{
		}
		return Task.FromResult((IReadOnlyList<CameraLensOption>)result);
	}
}
