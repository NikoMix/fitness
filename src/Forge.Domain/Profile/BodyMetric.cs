using Forge.Domain.Common;
using Forge.Domain.Measurement;

namespace Forge.Domain.Profile;

/// <summary>A timestamped body measurement entry.</summary>
public sealed class BodyMetric : Entity, IProfileOwned
{
    /// <summary>The profile this measurement belongs to.</summary>
    public required Guid UserProfileId { get; init; }

    /// <summary>When the measurement was taken, in UTC.</summary>
    public DateTimeOffset RecordedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Body weight.</summary>
    public Mass Weight { get; set; } = Mass.Zero;

    /// <summary>Optional body-fat percentage.</summary>
    public Percentage? BodyFatPercentage { get; set; }

    /// <summary>Optional waist circumference.</summary>
    public Length? WaistCircumference { get; set; }

    /// <summary>Optional hip circumference.</summary>
    public Length? HipCircumference { get; set; }

    /// <summary>Optional chest circumference.</summary>
    public Length? ChestCircumference { get; set; }

    /// <summary>Optional thigh circumference.</summary>
    public Length? ThighCircumference { get; set; }
}
