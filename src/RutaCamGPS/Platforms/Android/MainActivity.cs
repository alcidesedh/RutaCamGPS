using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Media.Projection;
using Android.Views;
using GPSCamRoute.Platforms.Android;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace GPSCamRoute;

[Activity(Theme = "@style/Maui.SplashTheme", Icon = "@mipmap/rutacam_icon", MainLauncher = true, ConfigurationChanges = (ConfigChanges.Density | ConfigChanges.Orientation | ConfigChanges.ScreenLayout | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode))]
public class MainActivity : MauiAppCompatActivity
{
	private const int ScreenCaptureRequestCode = 7302;

	private TaskCompletionSource<ScreenCapturePermissionResult>? _screenCaptureCompletion;

	private CancellationTokenRegistration _screenCaptureCancellation;

	private TaskCompletionSource<bool>? _resumeCompletion;

	private bool _isResumed;

	private ScreenOrientation _previousRequestedOrientation = ScreenOrientation.Unspecified;

	private bool _orientationWasLocked;

	protected override void OnResume()
	{
		base.OnResume();
		_isResumed = true;
		_resumeCompletion?.TrySetResult(result: true);
		_resumeCompletion = null;
	}

	protected override void OnPause()
	{
		_isResumed = false;
		base.OnPause();
	}

	public Task<ScreenCapturePermissionResult> RequestScreenCaptureAsync(CancellationToken cancellationToken)
	{
		if (_screenCaptureCompletion != null)
		{
			throw new InvalidOperationException("Ya existe una solicitud de captura en curso.");
		}
		MediaProjectionManager manager = ((MediaProjectionManager)base.GetSystemService("media_projection")) ?? throw new InvalidOperationException("MediaProjection no está disponible.");
		_screenCaptureCompletion = new TaskCompletionSource<ScreenCapturePermissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_screenCaptureCancellation = cancellationToken.Register(delegate
		{
			_screenCaptureCompletion?.TrySetCanceled(cancellationToken);
			ClearScreenCaptureRequest();
		});
		base.StartActivityForResult(manager.CreateScreenCaptureIntent(), 7302);
		return _screenCaptureCompletion.Task;
	}

	public async Task WaitUntilVisibleAsync(CancellationToken cancellationToken)
	{
		if (_isResumed)
		{
			return;
		}
		if (_resumeCompletion == null)
		{
			_resumeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		}
		using (cancellationToken.Register(delegate
		{
			_resumeCompletion?.TrySetCanceled(cancellationToken);
		}))
		{
			await _resumeCompletion.Task.WaitAsync(TimeSpan.FromSeconds(8L), cancellationToken);
		}
	}

	public void EnterRecordingPresentation(bool hideSystemBars, bool lockOrientation)
	{
		if (lockOrientation && !_orientationWasLocked)
		{
			_previousRequestedOrientation = base.RequestedOrientation;
			Orientation orientation = base.Resources?.Configuration?.Orientation ?? Orientation.Portrait;
			base.RequestedOrientation = ((orientation != Orientation.Landscape) ? ScreenOrientation.Portrait : ScreenOrientation.Landscape);
			_orientationWasLocked = true;
		}
		if (hideSystemBars && base.Window?.DecorView != null)
		{
			base.Window.DecorView.SystemUiVisibility = (StatusBarVisibility)5894;
			base.Window.AddFlags(WindowManagerFlags.Fullscreen);
		}
	}

	public void ExitRecordingPresentation()
	{
		if (base.Window?.DecorView != null)
		{
			base.Window.DecorView.SystemUiVisibility = StatusBarVisibility.Visible;
			base.Window.ClearFlags(WindowManagerFlags.Fullscreen);
		}
		if (_orientationWasLocked)
		{
			base.RequestedOrientation = _previousRequestedOrientation;
			_orientationWasLocked = false;
		}
	}

	public override bool DispatchKeyEvent(KeyEvent? e)
	{
		if (e != null && (e.KeyCode == Keycode.VolumeUp || e.KeyCode == Keycode.VolumeDown) && TryGetVisibleMainPage(out MainPage mainPage))
		{
			if (e.Action == KeyEventActions.Down && e.RepeatCount == 0)
			{
				mainPage.ToggleRecordingFromHardwareAsync();
			}
			return true;
		}
		return base.DispatchKeyEvent(e);
	}

	private static bool TryGetVisibleMainPage(out MainPage mainPage)
	{
		mainPage = null;
		Microsoft.Maui.Controls.Window window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
		if (window?.Page is MainPage directMainPage)
		{
			mainPage = directMainPage;
			return true;
		}
		if (window?.Page is NavigationPage { CurrentPage: MainPage navigationMainPage })
		{
			mainPage = navigationMainPage;
			return true;
		}
		return false;
	}

	protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
	{
		if (requestCode == 7302)
		{
			bool granted = resultCode == Result.Ok && data != null;
			_screenCaptureCompletion?.TrySetResult(new ScreenCapturePermissionResult(granted, (int)resultCode, data));
			ClearScreenCaptureRequest();
		}
		else
		{
			base.OnActivityResult(requestCode, resultCode, data);
		}
	}

	private void ClearScreenCaptureRequest()
	{
		_screenCaptureCancellation.Dispose();
		_screenCaptureCompletion = null;
	}
}
