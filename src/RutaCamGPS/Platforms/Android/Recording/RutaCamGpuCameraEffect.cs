using AndroidX.Camera.Core;
using AndroidX.Core.Util;
using Java.Util.Concurrent;

namespace GPSCamRoute.Platforms.Android.Recording;

public sealed class RutaCamGpuCameraEffect : CameraEffect
{
	public RutaCamGpuCameraEffect(IExecutor executor, ISurfaceProcessor processor, IConsumer errorListener)
		: base(2, executor, processor, errorListener)
	{
	}
}
