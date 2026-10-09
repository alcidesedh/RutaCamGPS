using Android.Content;

namespace GPSCamRoute.Platforms.Android;

internal static class ProjectionPermissionStore
{
	private static readonly object Sync = new object();

	private static int _resultCode;

	private static Intent? _data;

	public static void Set(int resultCode, Intent data)
	{
		lock (Sync)
		{
			_resultCode = resultCode;
			_data = data;
		}
	}

	public static (int ResultCode, Intent Data)? Take()
	{
		lock (Sync)
		{
			if (_data == null)
			{
				return null;
			}
			(int, Intent) result = (_resultCode, _data);
			_data = null;
			_resultCode = 0;
			return result;
		}
	}
}
