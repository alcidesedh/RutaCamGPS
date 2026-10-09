using System;

namespace GPSCamRoute.Services;

public interface IGnssSpeedService
{
	bool IsRunning { get; }

	event EventHandler<GnssSpeedSample>? SpeedChanged;

	event EventHandler<string>? StatusChanged;

	void Start();

	void Stop();
}
