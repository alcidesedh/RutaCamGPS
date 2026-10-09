using Microsoft.Maui.ApplicationModel;

namespace GPSCamRoute.Platforms.Android;

public sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
	public override (string androidPermission, bool isRuntime)[] RequiredPermissions => new(string, bool)[1] { ("android.permission.BLUETOOTH_CONNECT", true) };
}
