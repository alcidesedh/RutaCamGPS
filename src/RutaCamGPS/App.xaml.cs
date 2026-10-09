using System;
using System.CodeDom.Compiler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace GPSCamRoute;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		MainPage mainPage = _services.GetRequiredService<MainPage>();
		return new Window(new NavigationPage(mainPage));
	}

}
