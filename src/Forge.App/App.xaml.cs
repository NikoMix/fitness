using Forge.App.Composition;
using Forge.App.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App;

/// <summary>The Forge application.</summary>
public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;
    private readonly ForgeStartupService startup;

    /// <summary>Initialises the application.</summary>
    /// <param name="services">Resolves the shell once the application is current.</param>
    /// <param name="startup">Prepares the local database before any screen needs it.</param>
    internal App(IServiceProvider services, ForgeStartupService startup)
    {
        InitializeComponent();
        this.services = services;
        this.startup = startup;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The shell is resolved here rather than injected into the constructor. Its XAML uses
    /// DevExpress theme markup extensions, and those read Application.Current - which is only
    /// assigned once this App instance exists. Taking AppShell as a constructor parameter builds
    /// the shell before that assignment, so the extension dereferences null and the app dies at
    /// launch inside AppShell.InitializeComponent. CreateWindow runs after Application.Current is
    /// set, so resolving here is both safe and lazy.
    /// </remarks>
    protected override Window CreateWindow(IActivationState? activationState)
        => new(services.GetRequiredService<AppShell>()) { Title = "Forge" };

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();

        // Database preparation is deliberately not awaited here.
        //
        // OnStart runs on the UI thread, and blocking it would delay the first frame against a
        // 2.0 s cold-start budget. Screens that need data await ForgeStartupService themselves
        // rather than assuming it has finished. Kicking it off eagerly means it is almost
        // always complete by the time the first data-backed screen appears, without ever
        // holding up the shell.
        _ = Task.Run(async () =>
        {
            await startup.InitialiseAsync().ConfigureAwait(false);

            if (!startup.Succeeded)
            {
                // Startup already logged the fault. Presenting it to the user belongs to the
                // recovery surface (E01/F01.03); failing silently here would be worse.
                System.Diagnostics.Debug.WriteLine($"Forge startup failed: {startup.Failure}");
            }
        });
    }
}
