using System.ComponentModel;
using Forge.App.Adaptive;

namespace Forge.App.Features.Legal;

public abstract class LegalDocumentPage : ContentPage
{
    protected LegalDocumentPage(string title, IReadOnlyList<LegalSection> sections)
    {
        Title = title;

        // Policies and disclaimers are the longest unbroken prose in Forge, which makes them the
        // pages that suffer most on a 13-inch screen: an uncapped paragraph runs to roughly 180
        // characters a line. Legal text nobody can comfortably read is legal text nobody reads.
        var body = BuildContent(title, sections);
        body.HorizontalOptions = LayoutOptions.Center;

        var host = new AdaptiveHost { Content = new ScrollView { Content = body } };
        host.PropertyChanged += (_, e) => OnHostChanged(host, body, e);

        Content = host;
    }

    private static void OnHostChanged(AdaptiveHost host, View body, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdaptiveHost.ReadingWidth))
        {
            body.WidthRequest = host.ReadingWidth;
        }
    }

    private static VerticalStackLayout BuildContent(string title, IReadOnlyList<LegalSection> sections)
    {
        var layout = new VerticalStackLayout
        {
            Padding = PagePadding(),
            Spacing = Resource<double>("SpaceL"),
        };

        var heading = new Label
        {
            Text = title,
            Style = Resource<Style>("HeadlineText"),
        };
        SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level1);
        layout.Children.Add(heading);

        foreach (var section in sections)
        {
            var sectionHeading = new Label
            {
                Text = section.Title,
                Style = Resource<Style>("TitleText"),
            };
            SemanticProperties.SetHeadingLevel(sectionHeading, SemanticHeadingLevel.Level2);
            layout.Children.Add(sectionHeading);
            layout.Children.Add(new Label
            {
                Text = section.Body,
                Style = Resource<Style>("BodyText"),
            });
        }

        return layout;
    }

    /// <remarks>
    /// PagePadding is an OnIdiom token so that tablets get a wider gutter without every page
    /// opting in. XAML applies the implicit conversion; a cast from object cannot, so it is done
    /// here rather than through the generic lookup below.
    /// </remarks>
    private static Thickness PagePadding()
        => Lookup("PagePadding") switch
        {
            Thickness fixedPadding => fixedPadding,
            OnIdiom<Thickness> perIdiom => perIdiom,
            _ => default
        };

    private static T Resource<T>(string key) => Lookup(key) is T value ? value : default!;

    private static object? Lookup(string key)
        => Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value
            : null;
}

public sealed record LegalSection(string Title, string Body);