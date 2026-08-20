# Forge contributor instructions

- v1 is Android and iOS only. DevExpress MAUI does not render on Windows or Mac Catalyst; it compiles against a stub and crashes at runtime.
- DevExpress registration calls must follow `.UseMauiApp<App>()`; analyzer DXM001 enforces this order.
- Use `dx:RadialProgressBar` for rings, not `dx:RadialGauge`. There is no `dx:RangeBar`.
- `Forge.Domain` and `Forge.Core` must not reference MAUI or DevExpress; FORGE001 enforces this.
- Never name a namespace `Application` or `Theme` under the `Forge` root because they shadow framework types and a global using alias does not fix it.
- Features register themselves via `Add<Name>Feature`; EF entities use `IEntityTypeConfiguration` discovered from the assembly. Do not edit `MauiProgram.cs` or `ForgeDbContext` for feature work.
- XAML colour comes from DevExpress semantic roles, and sizes come from `ForgeTokens.xaml`; avoid hex literals and magic numbers.
- Tests use xUnit v3 with Shouldly. Do not introduce FluentAssertions.
