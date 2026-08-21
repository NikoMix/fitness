using System.Globalization;
using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Barcodes;

namespace Forge.App.Features.Scanning.Services;

/// <summary>What a barcode resolved to on this device.</summary>
/// <param name="IsKnown">Whether the barcode was already remembered here.</param>
/// <param name="FoodItemId">The resolved food, when known.</param>
/// <param name="FoodName">The resolved food's name, when known.</param>
/// <param name="Brand">The resolved food's brand, when it has one.</param>
/// <param name="NutritionSummary">A one-line per-100g summary for confirmation.</param>
public sealed record BarcodeResolution(
    bool IsKnown,
    Guid? FoodItemId,
    string? FoodName,
    string? Brand,
    string? NutritionSummary)
{
    /// <summary>A barcode this device has never seen.</summary>
    public static BarcodeResolution Unknown { get; } = new(false, null, null, null, null);
}

/// <summary>The label values needed to remember an unknown barcode.</summary>
/// <param name="Name">Food name, as printed on the packet.</param>
/// <param name="Brand">Optional brand or producer.</param>
/// <param name="EnergyKilocalories">Energy per 100 g.</param>
/// <param name="ProteinGrams">Protein per 100 g.</param>
/// <param name="CarbohydrateGrams">Carbohydrate per 100 g.</param>
/// <param name="FatGrams">Fat per 100 g.</param>
/// <param name="ServingGrams">Grams in one serving, or zero when the packet does not say.</param>
public sealed record NewFoodDetails(
    string Name,
    string? Brand,
    decimal EnergyKilocalories,
    decimal ProteinGrams,
    decimal CarbohydrateGrams,
    decimal FatGrams,
    decimal ServingGrams);

/// <summary>
/// Resolves barcodes against the local food catalogue, and remembers new ones.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is local. Forge makes no network call to resolve a barcode: there is no
/// backend and no third-party food API, per <c>docs/adr/0001-local-first-no-backend.md</c> and the
/// published privacy policy. That also means scanning works in a supermarket basement with no
/// signal, which is where it is actually used.
/// </para>
/// <para>
/// The consequence is honest rather than hidden: most first scans will not be recognised. The
/// answer to that is to remember the barcode once, not to pretend at coverage Forge does not have.
/// </para>
/// </remarks>
public interface IBarcodeCatalogueService
{
    /// <summary>Resolves a barcode against remembered mappings, recording the scan when it hits.</summary>
    /// <param name="barcode">The validated barcode.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The resolved food, or <see cref="BarcodeResolution.Unknown"/>.</returns>
    Task<BarcodeResolution> ResolveAsync(Barcode barcode, CancellationToken cancellationToken);

    /// <summary>Creates a food from packet values and remembers the barcode against it.</summary>
    /// <param name="barcode">The validated barcode.</param>
    /// <param name="details">The values read off the packet.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identifier of the created food.</returns>
    Task<Guid> RememberAsync(Barcode barcode, NewFoodDetails details, CancellationToken cancellationToken);
}

/// <inheritdoc />
internal sealed class BarcodeCatalogueService(ForgeStartupService startup, IDataSessionFactory sessions)
    : IBarcodeCatalogueService
{
    private const string BaseServingName = "100 g";
    private const string PortionServingName = "1 serving";

    /// <inheritdoc />
    public async Task<BarcodeResolution> ResolveAsync(Barcode barcode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(barcode);

        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var mappings = session.Repository<FoodBarcode>();

        // Loading every mapping is fine here and stays fine: this table only ever holds barcodes
        // this person has scanned, so it is tens of rows, not a food database. IRepository offers
        // no predicate overload, and inventing one for this would widen a shared abstraction for
        // a table that will never be large.
        var lookup = BarcodeMatcher.Match(barcode, await mappings.ListAsync(cancellationToken).ConfigureAwait(false));
        if (lookup.Match is not { } mapping)
        {
            return BarcodeResolution.Unknown;
        }

        var food = await session.Repository<FoodItem>().GetAsync(mapping.FoodItemId, cancellationToken).ConfigureAwait(false);
        if (food is null)
        {
            // The mapping outlived its food. Reporting a hit that cannot be shown would look like
            // a defect, so this is treated as unknown and the person can remember it again.
            return BarcodeResolution.Unknown;
        }

        mapping.RecordScan(DateTimeOffset.UtcNow);
        await mappings.UpdateAsync(mapping, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BarcodeResolution(true, food.Id, food.Name, food.Brand, Summarise(food.Per100Grams));
    }

    /// <inheritdoc />
    public async Task<Guid> RememberAsync(Barcode barcode, NewFoodDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Name);

        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);

        // One session, so the new food and its barcode commit together. A partial write here would
        // leave either an unreachable food or a mapping pointing at nothing.
        await using var session = sessions.Create();
        var foods = session.Repository<FoodItem>();
        var mappings = session.Repository<FoodBarcode>();

        var food = new FoodItem
        {
            Name = details.Name.Trim(),
            Brand = string.IsNullOrWhiteSpace(details.Brand) ? null : details.Brand.Trim(),
            Per100Grams = new NutrientProfile(
                details.EnergyKilocalories,
                details.ProteinGrams,
                details.CarbohydrateGrams,
                details.FatGrams,
                0m,
                0m,
                0m),
            IsUserCreated = true,
        };

        food.Servings.Add(new ServingDefinition { Name = BaseServingName, Mass = Mass.FromKilograms(0.1m) });
        if (details.ServingGrams > 0m && details.ServingGrams != 100m)
        {
            food.Servings.Add(new ServingDefinition
            {
                Name = PortionServingName,
                Mass = Mass.FromKilograms(details.ServingGrams / 1000m),
            });
        }

        await foods.AddAsync(food, cancellationToken).ConfigureAwait(false);

        // The unique index on the canonical key would otherwise surface as a raw SQLite error.
        // Repointing an existing mapping is also the behaviour a person expects when they scan a
        // code they previously attached to the wrong food.
        var existing = BarcodeMatcher
            .Match(barcode, await mappings.ListAsync(cancellationToken).ConfigureAwait(false))
            .Match;

        if (existing is null)
        {
            var mapping = FoodBarcode.ForFood(barcode, food.Id, BarcodeProvenance.UserCreated);
            mapping.RecordScan(DateTimeOffset.UtcNow);
            await mappings.AddAsync(mapping, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.FoodItemId = food.Id;
            existing.RecordScan(DateTimeOffset.UtcNow);
            await mappings.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return food.Id;
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }

    private static string Summarise(NutrientProfile per100Grams) => string.Format(
        CultureInfo.CurrentCulture,
        "{0:0} kcal • P {1:0.#} g • C {2:0.#} g • F {3:0.#} g per 100 g",
        per100Grams.EnergyKilocalories,
        per100Grams.ProteinGrams,
        per100Grams.CarbohydrateGrams,
        per100Grams.FatGrams);
}
