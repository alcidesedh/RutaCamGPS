namespace GPSCamRoute.Models;

public sealed record VideoPublishResult(bool Success, string PublicUri, string DisplayName, string RelativePath, long SizeBytes, string ErrorMessage = "")
{
	public static VideoPublishResult Failed(string errorMessage)
	{
		return new VideoPublishResult(Success: false, string.Empty, string.Empty, string.Empty, 0L, errorMessage);
	}
}
