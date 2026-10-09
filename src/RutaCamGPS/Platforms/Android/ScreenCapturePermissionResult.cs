using Android.Content;

namespace GPSCamRoute.Platforms.Android;

public sealed record ScreenCapturePermissionResult(bool Granted, int ResultCode, Intent? Data)
{
	public static ScreenCapturePermissionResult Cancelled => new ScreenCapturePermissionResult(Granted: false, 0, null);
}
