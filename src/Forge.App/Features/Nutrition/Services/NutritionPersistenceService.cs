using System.Text.Json;
using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Profile;
using Forge.Infrastructure.Content;

namespace Forge.App.Features.Nutrition.Services;

public interface INutritionPersistenceService
{
    Task<NutritionDaySnapshot> LoadNutritionDayAsync(DateOnly day, CancellationToken cancellationToken);

    Task<FoodLogSnapshot> LoadFoodLogAsync(DateOnly day, CancellationToken cancellationToken);

    Task<IReadOnlyList<FoodCatalogItemSnapshot>> SearchFoodsAsync(string query, CancellationToken cancellationToken);

    Task LogFoodAsync(Guid foodItemId, MealSlot mealSlot, CancellationToken cancellationToken);

    Task<int> CopyPreviousDayAsync(DateOnly targetDate, CancellationToken cancellationToken);

    Task<HydrationDaySnapshot> LoadHydrationDayAsync(DateOnly day, CancellationToken cancellationToken);

    Task LogHydrationAsync(Volume volume, BeverageType beverageType, decimal caffeineMilligrams, DateTimeOffset consumedUtc, CancellationToken cancellationToken);
}

public sealed record FoodCatalogItemSnapshot(Guid Id, string Name, string? Brand, NutrientProfile Per100Grams, IReadOnlyList<string> Servings);

public sealed record FoodLogItemSnapshot(string Meal, string Food, string Detail);

public sealed record FoodLogSnapshot(
    IReadOnlyList<FoodCatalogItemSnapshot> RecentFoods,
    IReadOnlyList<FoodCatalogItemSnapshot> FrequentFoods,
    IReadOnlyList<FoodCatalogItemSnapshot> SearchFoods,
    IReadOnlyList<FoodLogItemSnapshot> LoggedFoods);

public sealed record NutritionDaySnapshot(
    NutrientProfile Total,
    IReadOnlyList<MealSummarySnapshot> Meals,
    IReadOnlyList<FoodCatalogItemSnapshot> FeaturedFoods);

public sealed record MealSummarySnapshot(string Meal, string Summary, string Detail);

public sealed record HydrationHistorySnapshot(string Time, string Beverage, string VolumeText);

public sealed record HydrationDaySnapshot(decimal ConsumedMillilitres, IReadOnlyList<HydrationHistorySnapshot> History);

/// <summary>
/// Reads and writes the food and hydration log for the active profile.
/// </summary>
/// <remarks>
/// The food catalogue is deliberately shared between profiles and is read unscoped: it is shipped
/// reference data, not somebody's record of what they ate. The log entries that point at it are
/// scoped, so two people sharing a device see the same foods and different days.
/// </remarks>
internal sealed class NutritionPersistenceService(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles) : INutritionPersistenceService
{
    private const string FoodCatalogueResourceName = "Forge.Infrastructure.Content.food-catalogue.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    public async Task<NutritionDaySnapshot> LoadNutritionDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        return await WithRepositoriesAsync(async (foods, foodLogs, _, unitOfWork, scope) =>
        {
            await EnsureFoodCatalogueAsync(foods, unitOfWork, cancellationToken).ConfigureAwait(false);
            var allFoods = await foods.ListAsync(cancellationToken).ConfigureAwait(false);
            var foodLookup = allFoods.ToDictionary(food => food.Id);
            var dayEntries = FilterByDate(await OwnedLogsAsync(foodLogs, scope, cancellationToken).ConfigureAwait(false), day)
                .OrderBy(entry => entry.ConsumedUtc)
                .ToList();
            var total = SumNutrients(dayEntries, foodLookup);

            return new NutritionDaySnapshot(
                total,
                BuildMealSummaries(dayEntries, foodLookup),
                BuildFeaturedFoods(dayEntries, allFoods));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FoodLogSnapshot> LoadFoodLogAsync(DateOnly day, CancellationToken cancellationToken)
    {
        return await WithRepositoriesAsync(async (foods, foodLogs, _, unitOfWork, scope) =>
        {
            await EnsureFoodCatalogueAsync(foods, unitOfWork, cancellationToken).ConfigureAwait(false);
            var allFoods = await foods.ListAsync(cancellationToken).ConfigureAwait(false);
            var allLogs = await OwnedLogsAsync(foodLogs, scope, cancellationToken).ConfigureAwait(false);
            var foodLookup = allFoods.ToDictionary(food => food.Id);
            var dayEntries = FilterByDate(allLogs, day)
                .OrderByDescending(entry => entry.ConsumedUtc)
                .ToList();

            return new FoodLogSnapshot(
                BuildRecentFoods(allLogs, foodLookup),
                BuildFrequentFoods(allLogs, foodLookup),
                allFoods.OrderBy(food => food.Name, StringComparer.OrdinalIgnoreCase).Take(12).Select(ToFoodSnapshot).ToList(),
                dayEntries.Select(entry => ToLoggedFood(entry, foodLookup)).ToList());
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FoodCatalogItemSnapshot>> SearchFoodsAsync(string query, CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            return await WithRepositoriesAsync(async (foods, _, _, unitOfWork, _) =>
            {
                await EnsureFoodCatalogueAsync(foods, unitOfWork, cancellationToken).ConfigureAwait(false);
                var normalized = query.Trim();
                var allFoods = await foods.ListAsync(cancellationToken).ConfigureAwait(false);
                var source = string.IsNullOrWhiteSpace(normalized)
                    ? allFoods
                    : allFoods.Where(food => food.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || (food.Brand?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false));

                return source
                    .OrderBy(food => food.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .Select(ToFoodSnapshot)
                    .ToList();
            }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task LogFoodAsync(Guid foodItemId, MealSlot mealSlot, CancellationToken cancellationToken)
    {
        await WithRepositoriesAsync(async (foods, foodLogs, _, unitOfWork, scope) =>
        {
            await EnsureFoodCatalogueAsync(foods, unitOfWork, cancellationToken).ConfigureAwait(false);
            var food = await foods.GetAsync(foodItemId, cancellationToken).ConfigureAwait(false);
            if (food is null)
            {
                return 0;
            }

            var servingName = food.Servings.FirstOrDefault(s => string.Equals(s.Name, "1 serving", StringComparison.OrdinalIgnoreCase))?.Name
                ?? food.Servings.FirstOrDefault()?.Name
                ?? "100 g";
            var serving = food.Servings.Count > 0
                ? food.SnapshotServing(servingName, 1m)
                : new ServingSnapshot(servingName, 1m, 100m);

            await foodLogs.AddAsync(new FoodLogEntry
            {
                Id = Guid.CreateVersion7(),
                UserProfileId = scope.ProfileId,
                FoodItemId = food.Id,
                MealSlot = mealSlot,
                Serving = serving,
                ConsumedUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            return await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CopyPreviousDayAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        return await WithRepositoriesAsync(async (foods, foodLogs, _, unitOfWork, scope) =>
        {
            await EnsureFoodCatalogueAsync(foods, unitOfWork, cancellationToken).ConfigureAwait(false);
            var previous = FilterByDate(await OwnedLogsAsync(foodLogs, scope, cancellationToken).ConfigureAwait(false), targetDate.AddDays(-1))
                .OrderBy(entry => entry.ConsumedUtc)
                .ToList();
            var targetStart = StartOfLocalDate(targetDate);

            foreach (var entry in previous)
            {
                var copiedTime = targetStart.Add(entry.ConsumedUtc.ToLocalTime().TimeOfDay).ToUniversalTime();
                await foodLogs.AddAsync(new FoodLogEntry
                {
                    Id = Guid.CreateVersion7(),
                    UserProfileId = scope.ProfileId,
                    FoodItemId = entry.FoodItemId,
                    MealSlot = entry.MealSlot,
                    Serving = entry.Serving,
                    ConsumedUtc = copiedTime
                }, cancellationToken).ConfigureAwait(false);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return previous.Count;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HydrationDaySnapshot> LoadHydrationDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        return await WithRepositoriesAsync(async (_, _, hydrationEntries, _, scope) =>
        {
            var dayEntries = FilterByDate(
                    (await hydrationEntries.ListAsync(cancellationToken).ConfigureAwait(false)).OwnedBy(scope),
                    day)
                .OrderByDescending(entry => entry.ConsumedUtc)
                .ToList();
            return new HydrationDaySnapshot(
                dayEntries.Sum(entry => entry.Volume.Millilitres),
                dayEntries.Select(entry => new HydrationHistorySnapshot(
                    entry.ConsumedUtc.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    entry.BeverageType.ToString(),
                    entry.Volume.ToString())).ToList());
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task LogHydrationAsync(Volume volume, BeverageType beverageType, decimal caffeineMilligrams, DateTimeOffset consumedUtc, CancellationToken cancellationToken)
    {
        await WithRepositoriesAsync(async (_, _, hydrationEntries, unitOfWork, scope) =>
        {
            await hydrationEntries.AddAsync(new HydrationEntry
            {
                Id = Guid.CreateVersion7(),
                UserProfileId = scope.ProfileId,
                Volume = volume,
                BeverageType = beverageType,
                CaffeineMilligrams = caffeineMilligrams,
                ConsumedUtc = consumedUtc.ToUniversalTime()
            }, cancellationToken).ConfigureAwait(false);

            return await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads this profile's food log entries.</summary>
    /// <remarks>
    /// The materialised overload is used because the repository already returns a list. An
    /// unresolved scope yields nothing, so a day opened before the profile is known shows an empty
    /// log rather than the household's combined calories.
    /// </remarks>
    private static async Task<IReadOnlyList<FoodLogEntry>> OwnedLogsAsync(
        IRepository<FoodLogEntry> foodLogs,
        ProfileScope scope,
        CancellationToken cancellationToken)
        => [.. (await foodLogs.ListAsync(cancellationToken).ConfigureAwait(false)).OwnedBy(scope)];

    private async Task<TResult> WithRepositoriesAsync<TResult>(
        Func<IRepository<FoodItem>, IRepository<FoodLogEntry>, IRepository<HydrationEntry>, IDataSession, ProfileScope, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        return await action(
            session.Repository<FoodItem>(),
            session.Repository<FoodLogEntry>(),
            session.Repository<HydrationEntry>(),
            session,
            scope).ConfigureAwait(false);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }

    private static async Task EnsureFoodCatalogueAsync(IRepository<FoodItem> foods, IUnitOfWork? unitOfWork, CancellationToken cancellationToken)
    {
        var existing = await foods.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(food => !food.IsUserCreated))
        {
            return;
        }

        await SeedLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await foods.ListAsync(cancellationToken).ConfigureAwait(false);
            if (existing.Any(food => !food.IsUserCreated))
            {
                return;
            }

            foreach (var food in LoadSeedFoods())
            {
                await foods.AddAsync(food, cancellationToken).ConfigureAwait(false);
            }

            if (unitOfWork is not null)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static List<FoodItem> LoadSeedFoods()
    {
        var assembly = typeof(SeedCatalogue).Assembly;
        using var stream = assembly.GetManifestResourceStream(FoodCatalogueResourceName)
            ?? throw new InvalidOperationException($"The embedded food catalogue '{FoodCatalogueResourceName}' was not found.");
        var catalogue = JsonSerializer.Deserialize<FoodCatalogueDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The embedded food catalogue could not be parsed.");

        if (catalogue.Foods.Count == 0
            || string.IsNullOrWhiteSpace(catalogue.Provenance)
            || !catalogue.Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The embedded food catalogue must contain original Forge food content.");
        }

        return catalogue.Foods.Select(item => item.ToFood()).ToList();
    }

    private static NutrientProfile SumNutrients(IEnumerable<FoodLogEntry> entries, Dictionary<Guid, FoodItem> foods)
    {
        var total = NutrientProfile.Zero;
        foreach (var entry in entries)
        {
            if (foods.TryGetValue(entry.FoodItemId, out var food))
            {
                total += food.Per100Grams.ForGrams(entry.Serving.TotalGrams);
            }
        }

        return total;
    }

    private static List<MealSummarySnapshot> BuildMealSummaries(IReadOnlyList<FoodLogEntry> entries, Dictionary<Guid, FoodItem> foods) =>
        Enum.GetValues<MealSlot>()
            .Select(slot =>
            {
                var slotEntries = entries.Where(entry => entry.MealSlot == slot).ToList();
                if (slotEntries.Count == 0)
                {
                    return new MealSummarySnapshot(slot.ToString(), "Not logged yet", "Tap Log food when you are ready.");
                }

                var nutrients = SumNutrients(slotEntries, foods);
                var names = slotEntries
                    .Select(entry => foods.TryGetValue(entry.FoodItemId, out var food) ? food.Name : "Food")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2);
                return new MealSummarySnapshot(
                    slot.ToString(),
                    string.Join(", ", names),
                    $"{nutrients.EnergyKilocalories:0} kcal • P {nutrients.ProteinGrams:0.#} g • C {nutrients.CarbohydrateGrams:0.#} g • F {nutrients.FatGrams:0.#} g");
            })
            .ToList();

    private static List<FoodCatalogItemSnapshot> BuildFeaturedFoods(IReadOnlyList<FoodLogEntry> entries, IReadOnlyList<FoodItem> allFoods)
    {
        var lookup = allFoods.ToDictionary(food => food.Id);
        var logged = BuildRecentFoods(entries, lookup);
        return logged.Count > 0
            ? logged.Take(5).ToList()
            : allFoods.OrderBy(food => food.Name, StringComparer.OrdinalIgnoreCase).Take(5).Select(ToFoodSnapshot).ToList();
    }

    private static List<FoodCatalogItemSnapshot> BuildRecentFoods(IEnumerable<FoodLogEntry> logs, Dictionary<Guid, FoodItem> foodLookup) =>
        logs.OrderByDescending(log => log.ConsumedUtc)
            .Select(log => log.FoodItemId)
            .Distinct()
            .Where(foodLookup.ContainsKey)
            .Take(8)
            .Select(id => ToFoodSnapshot(foodLookup[id]))
            .ToList();

    private static List<FoodCatalogItemSnapshot> BuildFrequentFoods(IEnumerable<FoodLogEntry> logs, Dictionary<Guid, FoodItem> foodLookup) =>
        logs.GroupBy(log => log.FoodItemId)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(log => log.ConsumedUtc))
            .Select(group => group.Key)
            .Where(foodLookup.ContainsKey)
            .Take(8)
            .Select(id => ToFoodSnapshot(foodLookup[id]))
            .ToList();

    private static FoodLogItemSnapshot ToLoggedFood(FoodLogEntry entry, Dictionary<Guid, FoodItem> foods)
    {
        var name = foods.TryGetValue(entry.FoodItemId, out var food) ? food.Name : "Food";
        return new FoodLogItemSnapshot(
            entry.MealSlot.ToString(),
            name,
            $"{entry.Serving.Quantity:0.###} × {entry.Serving.ServingName} • {entry.Serving.TotalGrams:0.#} g");
    }

    private static FoodCatalogItemSnapshot ToFoodSnapshot(FoodItem food) => new(
        food.Id,
        food.Name,
        food.Brand,
        food.Per100Grams,
        food.Servings.Select(serving => serving.Name).ToList());

    private static List<T> FilterByDate<T>(IEnumerable<T> entries, DateOnly date)
        where T : class
    {
        var start = StartOfLocalDate(date).ToUniversalTime();
        var end = StartOfLocalDate(date.AddDays(1)).ToUniversalTime();

        return entries.Where(entry =>
        {
            var consumedUtc = entry switch
            {
                FoodLogEntry food => food.ConsumedUtc,
                HydrationEntry hydration => hydration.ConsumedUtc,
                _ => throw new InvalidOperationException("Unsupported nutrition date filter type.")
            };
            return consumedUtc >= start && consumedUtc < end;
        }).ToList();
    }

    private static DateTimeOffset StartOfLocalDate(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class FoodCatalogueDocument
    {
        public required int Version { get; init; }

        public required string Provenance { get; init; }

        public required IReadOnlyList<FoodSeedItem> Foods { get; init; }
    }

    private sealed class FoodSeedItem
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public string? Brand { get; init; }

        public decimal EnergyKilocalories { get; init; }

        public decimal ProteinGrams { get; init; }

        public decimal CarbohydrateGrams { get; init; }

        public decimal FatGrams { get; init; }

        public decimal FibreGrams { get; init; }

        public decimal SugarGrams { get; init; }

        public decimal SodiumMilligrams { get; init; }

        public required IReadOnlyList<ServingSeedItem> Servings { get; init; }

        public FoodItem ToFood()
        {
            var food = new FoodItem
            {
                Id = Id,
                Name = Name,
                Brand = Brand,
                Per100Grams = new NutrientProfile(
                    EnergyKilocalories,
                    ProteinGrams,
                    CarbohydrateGrams,
                    FatGrams,
                    FibreGrams,
                    SugarGrams,
                    SodiumMilligrams),
                IsUserCreated = false
            };

            foreach (var serving in Servings)
            {
                food.Servings.Add(new ServingDefinition
                {
                    Name = serving.Name,
                    Mass = Mass.FromKilograms(serving.Grams / 1000m)
                });
            }

            return food;
        }
    }

    private sealed class ServingSeedItem
    {
        public required string Name { get; init; }

        public decimal Grams { get; init; }
    }
}
