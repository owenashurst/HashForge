namespace HashForge.Mining.Shares;

/// <summary>The block times a share may carry. Shared so that work is never handed out with a time that would then be refused.</summary>
public static class ShareTimeLimits
{
    /// <summary>How far past the template's time a block timestamp may be, per Bitcoin's consensus rules.</summary>
    public const uint MaxFutureSeconds = 7200;
}
