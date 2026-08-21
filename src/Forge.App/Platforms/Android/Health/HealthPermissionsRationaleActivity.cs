#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Forge.Core.Abstractions.Health;
using AndroidView = Android.Views.View;
using AndroidButton = Android.Widget.Button;
using AndroidScrollView = Android.Widget.ScrollView;

namespace Forge.App.Platforms.Android.Health;

/// <summary>
/// Explains why Forge asks for health data, launched by Health Connect itself.
/// </summary>
/// <remarks>
/// <para>
/// Both Health Connect entry points - <c>SHOW_PERMISSIONS_RATIONALE</c> before Android 14 and
/// <c>VIEW_PERMISSION_USAGE</c> from Android 14 through the alias in the manifest overlay - land
/// here, and both can start it cold, from the system settings UI, with no Forge process running.
/// </para>
/// <para>
/// That is why this screen is plain Android views rather than a MAUI page. Reaching a Shell route
/// from a cold start means booting the whole app, waiting for the database, and hoping navigation
/// is ready; if any of that fails the user sees a crash while asking a privacy question, which is
/// the worst possible moment. The rationale is short, static text, so it renders without Forge's
/// UI stack at all.
/// </para>
/// <para>
/// The copy is generated from <see cref="HealthDataTypeCatalog"/> rather than duplicated, so the
/// screen cannot drift from the permissions the app actually requests.
/// </para>
/// </remarks>
[Activity(
    Name = "com.nikomix.forge.HealthPermissionsRationaleActivity",
    Label = "Forge health data",
    Exported = true,
    ExcludeFromRecents = true)]
[IntentFilter(
    ["androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE"],
    Categories = [Intent.CategoryDefault])]
public sealed class HealthPermissionsRationaleActivity : Activity
{
    /// <summary>
    /// The published privacy policy.
    /// </summary>
    /// <remarks>
    /// Google Play's Health Apps declaration requires a publicly hosted policy, and the rationale
    /// screen is where Health Connect sends users who ask what an app does with their data. The URL
    /// is repeated in the connections screen and in the Play Console submission; all three must
    /// agree, so it is declared once here.
    /// </remarks>
    public const string PrivacyPolicyUrl = "https://nikomix.github.io/fitness/privacy/";

    private const int EdgePaddingDip = 24;
    private const int ParagraphSpacingDip = 12;
    private const int SectionSpacingDip = 20;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildLayout());
    }

    private AndroidScrollView BuildLayout()
    {
        var content = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };

        var padding = ToPixels(EdgePaddingDip);
        content.SetPadding(padding, padding, padding, padding);

        content.AddView(CreateHeading("How Forge uses your health data"));
        content.AddView(CreateParagraph(
            "Forge is local-first. Anything it reads from Health Connect stays in the encrypted " +
            "database on this device. It is never uploaded, never used for advertising and never " +
            "shared with anyone."));
        content.AddView(CreateParagraph(
            "You can refuse any category, change your mind later in Health Connect, and keep using " +
            "every part of Forge - manual entry always works."));
        content.AddView(CreateParagraph($"Full privacy policy: {PrivacyPolicyUrl}"));

        content.AddView(CreateHeading("What each permission is for"));
        foreach (var dataType in HealthDataTypeCatalog.ReadTypes)
        {
            var descriptor = HealthDataTypeCatalog.Describe(dataType);
            content.AddView(CreateParagraph($"{descriptor.DisplayName}: {descriptor.Purpose}"));
        }

        var workout = HealthDataTypeCatalog.Describe(HealthDataType.Workout);
        content.AddView(CreateParagraph($"{workout.DisplayName}: {workout.Purpose}"));

        content.AddView(CreateHeading("Manage your choices"));
        content.AddView(CreateParagraph(
            "Health Connect settings hold the final say. Forge shows the same list, with what it " +
            "does and does not know about each category, under Settings then Health connections."));

        var openPolicy = new AndroidButton(this) { Text = "Read the privacy policy" };
        openPolicy.ContentDescription = "Read the Forge privacy policy in your browser";
        openPolicy.Click += OnOpenPrivacyPolicyClicked;
        content.AddView(openPolicy);

        var openForge = new AndroidButton(this) { Text = "Open Forge" };
        openForge.ContentDescription = "Open Forge";
        openForge.Click += OnOpenForgeClicked;
        content.AddView(openForge);

        var scroll = new AndroidScrollView(this);
        scroll.AddView(content);
        return scroll;
    }

    private void OnOpenPrivacyPolicyClicked(object? sender, EventArgs e)
    {
        // Best effort, like the launch button below: a device with no browser must not crash the
        // one screen whose whole purpose is answering a privacy question.
        try
        {
            using var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(PrivacyPolicyUrl));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(this, $"Open {PrivacyPolicyUrl} in a browser.", ToastLength.Long)?.Show();
        }
    }

    private void OnOpenForgeClicked(object? sender, EventArgs e)
    {
        // Best effort. The rationale is complete on its own, so failing to launch the app is a
        // missing convenience rather than something worth showing an error for.
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        if (launch is not null)
        {
            launch.AddFlags(ActivityFlags.NewTask);
            StartActivity(launch);
        }

        Finish();
    }

    private TextView CreateHeading(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 20f };
        view.SetTypeface(view.Typeface, global::Android.Graphics.TypefaceStyle.Bold);
        ApplySpacing(view, SectionSpacingDip);
        return view;
    }

    private TextView CreateParagraph(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 15f };
        ApplySpacing(view, ParagraphSpacingDip);
        return view;
    }

    private void ApplySpacing(AndroidView view, int topSpacingDip)
    {
        var parameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        parameters.TopMargin = ToPixels(topSpacingDip);
        view.LayoutParameters = parameters;
    }

    private int ToPixels(int dip) =>
        (int)Math.Round(dip * (Resources?.DisplayMetrics?.Density ?? 1f));
}
#endif
