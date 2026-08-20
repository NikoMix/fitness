## Summary

-

## Architecture rules

- [ ] I kept `Forge.Domain` and `Forge.Core` free of MAUI and DevExpress references (FORGE001).
- [ ] I did not edit `MauiProgram.cs` or `ForgeDbContext` for feature registration; features register through `Add<Name>Feature` and EF configurations are discovered from the assembly.
- [ ] I avoided `Forge.Application` and `Forge.Theme` namespaces.

## Platform test matrix

| Check | Android | iOS |
| --- | --- | --- |
| Builds | ⬜ | ⬜ |
| Smoke tested on device/emulator/simulator | ⬜ | ⬜ |

## Accessibility

- [ ] Interactive controls have accessible names.
- [ ] Text and touch targets remain usable with large text.
- [ ] Colour is not the only way to convey state.

## Performance

- [ ] This change respects the architecture performance budgets, including the 40 MB Android package budget.
