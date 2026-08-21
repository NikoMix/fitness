using Forge.Domain.Nutrition.Barcodes;

namespace Forge.App.Features.Scanning;

/// <summary>How a barcode scan ended.</summary>
public enum BarcodeScanOutcome
{
    /// <summary>The person left without choosing a food.</summary>
    Cancelled,

    /// <summary>A food was resolved, either from a remembered barcode or by creating one.</summary>
    FoodResolved,
}

/// <summary>
/// What a barcode scan produced.
/// </summary>
/// <remarks>
/// The scanner is a standalone destination that hands back a result rather than logging anything
/// itself. Food logging owns meal slots, servings and quantities; duplicating any of that inside
/// the scanner would give Forge two places that write food entries and two sets of rules about
/// them. Callers reach the scanner through <see cref="IBarcodeScanCoordinator"/>.
/// </remarks>
public sealed record BarcodeScanResult
{
    private BarcodeScanResult(BarcodeScanOutcome outcome)
    {
        Outcome = outcome;
    }

    /// <summary>How the scan ended.</summary>
    public BarcodeScanOutcome Outcome { get; }

    /// <summary>The resolved food, or <see langword="null"/> when the scan was cancelled.</summary>
    public Guid? FoodItemId { get; private init; }

    /// <summary>The canonical fourteen-digit key of the barcode that resolved the food.</summary>
    public string? Gtin14 { get; private init; }

    /// <summary>The barcode as scanned or typed.</summary>
    public string? ScannedValue { get; private init; }

    /// <summary>
    /// Whether the food was created during this scan rather than already known.
    /// </summary>
    /// <remarks>
    /// Worth surfacing: a caller may reasonably want to open the newly created food for editing,
    /// since it was filled in from a packet label in a hurry.
    /// </remarks>
    public bool FoodWasCreated { get; private init; }

    /// <summary>Whether a food was resolved.</summary>
    public bool HasFood => Outcome == BarcodeScanOutcome.FoodResolved && FoodItemId.HasValue;

    /// <summary>A scan the person abandoned.</summary>
    public static BarcodeScanResult Cancelled { get; } = new(BarcodeScanOutcome.Cancelled);

    /// <summary>A scan that produced a food.</summary>
    /// <param name="foodItemId">The resolved food.</param>
    /// <param name="barcode">The barcode that resolved it.</param>
    /// <param name="foodWasCreated">Whether the food was created during this scan.</param>
    /// <returns>A resolved result.</returns>
    public static BarcodeScanResult Resolved(Guid foodItemId, Barcode barcode, bool foodWasCreated)
    {
        ArgumentNullException.ThrowIfNull(barcode);

        return new BarcodeScanResult(BarcodeScanOutcome.FoodResolved)
        {
            FoodItemId = foodItemId,
            Gtin14 = barcode.Gtin14,
            ScannedValue = barcode.ScannedValue,
            FoodWasCreated = foodWasCreated,
        };
    }
}

/// <summary>
/// Opens the barcode scanner and waits for its result.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole public surface of the Scanning feature. A caller injects it, awaits
/// <see cref="ScanAsync"/>, and acts on the result; it never needs to know the scanner is a page,
/// which route it uses, or how the camera is abstracted.
/// </para>
/// <para>
/// Shell navigation is one-way, so a result cannot travel back along it. The coordinator bridges
/// that gap, and guarantees a result arrives even when the person dismisses the page with a back
/// gesture - an awaited call that can hang forever is worse than one that returns a cancellation.
/// </para>
/// </remarks>
public interface IBarcodeScanCoordinator
{
    /// <summary>Opens the scanner and waits for it to close.</summary>
    /// <param name="cancellationToken">Abandons the scan.</param>
    /// <returns>The scan result, which may be <see cref="BarcodeScanResult.Cancelled"/>.</returns>
    Task<BarcodeScanResult> ScanAsync(CancellationToken cancellationToken = default);
}
