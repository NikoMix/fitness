using Forge.Domain.Common;

namespace Forge.Domain.Recovery;

/// <summary>Morning subjective recovery check-in stored locally on the device.</summary>
public sealed class MorningCheckIn : Entity
{
    /// <summary>Local calendar date the check-in describes.</summary>
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Energy from 1 (very low) to 5 (high).</summary>
    public int Energy { get; set; } = 3;

    /// <summary>Whole-body soreness from 1 (none) to 5 (severe).</summary>
    public int Soreness { get; set; } = 2;

    /// <summary>Motivation to train from 1 (none) to 5 (high).</summary>
    public int Motivation { get; set; } = 3;

    /// <summary>Stress from 1 (low) to 5 (very high).</summary>
    public int Stress { get; set; } = 3;

    /// <summary>Manual sleep duration if available; health-store sleep can override this when consented.</summary>
    public decimal? SleepHours { get; set; }
}
