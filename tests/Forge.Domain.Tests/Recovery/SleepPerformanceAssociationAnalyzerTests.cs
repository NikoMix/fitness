using System.Globalization;
using Forge.Domain.Recovery;
using Shouldly;

namespace Forge.Domain.Tests.Recovery;

public sealed class SleepPerformanceAssociationAnalyzerTests
{
    [Fact]
    public void Enforces_minimum_sample_size_before_correlation_claim()
    {
        var samples = Enumerable.Range(0, SleepPerformanceAssociationAnalyzer.MinimumSampleSize - 1)
            .Select(index => new SleepPerformanceSample(new DateOnly(2026, 8, 1).AddDays(index), 7m, 100m));

        var result = SleepPerformanceAssociationAnalyzer.Analyze(samples);

        result.HasClaim.ShouldBeFalse();
        result.Message.ShouldContain(SleepPerformanceAssociationAnalyzer.MinimumSampleSize.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Words_supported_claim_as_association_not_causation()
    {
        var samples = Enumerable.Range(0, SleepPerformanceAssociationAnalyzer.MinimumSampleSize)
            .Select(index => new SleepPerformanceSample(new DateOnly(2026, 8, 1).AddDays(index), index < 4 ? 6m : 8m, index < 4 ? 90m : 100m));

        var result = SleepPerformanceAssociationAnalyzer.Analyze(samples);

        result.HasClaim.ShouldBeTrue();
        result.Message.ShouldContain("associated", Case.Insensitive);
        result.Message.ShouldNotContain("caus", Case.Insensitive);
    }
}
