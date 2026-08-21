using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Barcodes;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>
/// The barcode mapping as SQLite actually stores it.
/// </summary>
/// <remarks>
/// <para>
/// An entity configuration is not exercised by compiling. A bad one throws when the model is
/// built, which on a device means the app fails to start and the person's only copy of their data
/// becomes unreachable - so the mapping is verified against the real engine here rather than
/// trusted.
/// </para>
/// <para>
/// These live beside the barcode domain tests because the barcode feature owns this folder. Real
/// SQLite is used rather than the in-memory provider for the reason given in
/// <c>ForgeDbContextTests</c>: the in-memory provider is not relational and would silently accept
/// the duplicate the unique index exists to reject.
/// </para>
/// </remarks>
public sealed class FoodBarcodePersistenceTests : IAsyncLifetime
{
    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;

    public async ValueTask InitializeAsync()
    {
        // An in-memory SQLite database exists only while a connection to it is open.
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        options = new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options;

        await using var context = new ForgeDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private ForgeDbContext CreateContext() => new(options);

    private static Barcode Parse(string raw) => BarcodeNormaliser.Parse(raw).Barcode
        ?? throw new InvalidOperationException($"Test barcode '{raw}' should be valid.");

    private static FoodItem NewFood(string name) => new()
    {
        Name = name,
        Per100Grams = new NutrientProfile(389m, 16.9m, 66.3m, 6.9m, 10.6m, 0.9m, 2m),
        IsUserCreated = true,
    };

    [Fact]
    public async Task A_remembered_barcode_round_trips_with_its_symbology_and_provenance()
    {
        var food = NewFood("Own-brand porridge oats");
        var mapping = FoodBarcode.ForFood(Parse("04252614"), food.Id, BarcodeProvenance.UserCreated);
        mapping.RecordScan(new DateTimeOffset(2026, 5, 4, 7, 15, 0, TimeSpan.Zero));

        await using (var context = CreateContext())
        {
            context.Set<FoodItem>().Add(food);
            context.Set<FoodBarcode>().Add(mapping);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var stored = await verify.Set<FoodBarcode>().SingleAsync(TestContext.Current.CancellationToken);

        stored.Gtin14.ShouldBe("00042100005264");
        stored.ScannedValue.ShouldBe("04252614");
        stored.Symbology.ShouldBe(BarcodeSymbology.UpcE);
        stored.Provenance.ShouldBe(BarcodeProvenance.UserCreated);
        stored.TimesScanned.ShouldBe(1);
        stored.LastScannedUtc.ShouldBe(new DateTimeOffset(2026, 5, 4, 7, 15, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Two live mappings for one code would make the resolved food depend on row order, so the
    /// same packet could log different things on different days.
    /// </summary>
    [Fact]
    public async Task One_code_cannot_map_to_two_live_foods()
    {
        var first = NewFood("Porridge oats");
        var second = NewFood("Porridge oats, mistake");

        await using var context = CreateContext();
        context.Set<FoodItem>().AddRange(first, second);
        context.Set<FoodBarcode>().AddRange(
            FoodBarcode.ForFood(Parse("4006381333931"), first.Id, BarcodeProvenance.UserCreated),
            FoodBarcode.ForFood(Parse("4006381333931"), second.Id, BarcodeProvenance.UserCreated));

        await Should.ThrowAsync<DbUpdateException>(
            async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The unique index is filtered on the soft-delete column, so forgetting a mapping genuinely
    /// frees the code rather than poisoning it for good.
    /// </summary>
    [Fact]
    public async Task A_forgotten_code_can_be_remembered_again()
    {
        var original = NewFood("Wrong food");
        var replacement = NewFood("Right food");

        await using (var context = CreateContext())
        {
            var forgotten = FoodBarcode.ForFood(Parse("4006381333931"), original.Id, BarcodeProvenance.UserCreated);
            forgotten.DeletedUtc = DateTimeOffset.UtcNow;

            context.Set<FoodItem>().AddRange(original, replacement);
            context.Set<FoodBarcode>().Add(forgotten);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = CreateContext())
        {
            context.Set<FoodBarcode>().Add(
                FoodBarcode.ForFood(Parse("4006381333931"), replacement.Id, BarcodeProvenance.UserCreated));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var live = await verify.Set<FoodBarcode>().SingleAsync(TestContext.Current.CancellationToken);
        live.FoodItemId.ShouldBe(replacement.Id);
        (await verify.Set<FoodBarcode>().IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    /// <summary>
    /// A mapping to a food that no longer exists would report a hit that cannot be shown, which
    /// reads as a defect rather than as an unknown barcode.
    /// </summary>
    [Fact]
    public async Task Removing_a_food_removes_the_barcodes_pointing_at_it()
    {
        var food = NewFood("Discontinued bar");

        await using (var context = CreateContext())
        {
            context.Set<FoodItem>().Add(food);
            context.Set<FoodBarcode>().Add(
                FoodBarcode.ForFood(Parse("4006381333931"), food.Id, BarcodeProvenance.UserCreated));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = CreateContext())
        {
            var stored = await context.Set<FoodItem>().SingleAsync(TestContext.Current.CancellationToken);
            context.Set<FoodItem>().Remove(stored);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        (await verify.Set<FoodBarcode>().IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// One food legitimately carries several codes: a multipack, a regional variant and an
    /// own-brand relabel are the same porridge.
    /// </summary>
    [Fact]
    public async Task One_food_can_carry_several_codes()
    {
        var food = NewFood("Porridge oats");

        await using (var context = CreateContext())
        {
            context.Set<FoodItem>().Add(food);
            context.Set<FoodBarcode>().AddRange(
                FoodBarcode.ForFood(Parse("4006381333931"), food.Id, BarcodeProvenance.UserCreated),
                FoodBarcode.ForFood(Parse("036000291452"), food.Id, BarcodeProvenance.UserCreated));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var mappings = await verify.Set<FoodBarcode>().ToListAsync(TestContext.Current.CancellationToken);

        mappings.Count.ShouldBe(2);
        mappings.ShouldAllBe(mapping => mapping.FoodItemId == food.Id);
    }
}
