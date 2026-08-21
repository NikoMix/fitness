namespace Forge.App.Services.Localization;

/// <summary>XAML markup extension that binds a property to a translated string.</summary>
/// <remarks>
/// <para>
/// Usage: <c>Text="{loc:Translate Key=settings.language.title}"</c> after declaring
/// <c>xmlns:loc="clr-namespace:Forge.App.Services.Localization"</c>.
/// </para>
/// <para>
/// It returns a binding rather than a string on purpose. A string would be resolved once while
/// the page is being inflated and would then be frozen for the life of that page, so switching
/// language would only affect screens opened afterwards. Returning a binding against
/// <see cref="LocalizedStrings"/> means every label already on screen updates in place.
/// </para>
/// </remarks>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The key to translate, from <c>ForgeStringKeys</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]", BindingMode.OneWay, source: LocalizedStrings.Current);

    /// <inheritdoc />
    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
