using System.Runtime.CompilerServices;

namespace GPSCamRoute.Models;

public sealed record InterruptedSessionRecoveryResult(bool Found, bool Recovered, RouteRecord? Record, string Message)
{
	public static InterruptedSessionRecoveryResult None { get; } = new InterruptedSessionRecoveryResult(Found: false, Recovered: false, null, string.Empty);

	[CompilerGenerated]
	private InterruptedSessionRecoveryResult(InterruptedSessionRecoveryResult original)
	{
		Found = original.Found;
		Recovered = original.Recovered;
		Record = original.Record;
		Message = original.Message;
	}
}
