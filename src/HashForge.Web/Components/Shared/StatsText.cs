using HashForge.Domain.Formatting;
using HashForge.Mining.Statistics;

namespace HashForge.Web.Components.Shared;

/// <summary>Text for statistics that may not exist yet.</summary>
public static class StatsText
{
    public static string BestDifficulty(BestShare? best) => best is null ? Display.None : Display.Difficulty(best.Difficulty);

    public static string BestWorker(BestShare? best) => best is null ? Display.None : Display.Shorten(best.Worker);

    /// <summary>The count behind a reject rate, for the small print beside it.</summary>
    public static string RejectedCount(long rejected) => $"{Display.Count(rejected)} rejected";

    /// <summary>The count with its rate, for the admin page, where the operator wants both.</summary>
    public static string RejectedWithRate(long accepted, long rejected) =>
        accepted + rejected == 0
            ? Display.Count(rejected)
            : $"{Display.Count(rejected)} ({Display.RejectRate(accepted, rejected)})";
}