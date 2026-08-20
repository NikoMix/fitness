namespace Forge.App.Features.Legal;

public abstract class LegalDocumentPage : ContentPage
{
    protected LegalDocumentPage(string title, IReadOnlyList<LegalSection> sections)
    {
        Title = title;
        Content = new ScrollView
        {
            Content = BuildContent(title, sections),
        };
    }

    private static VerticalStackLayout BuildContent(string title, IReadOnlyList<LegalSection> sections)
    {
        var layout = new VerticalStackLayout
        {
            Padding = Resource<Thickness>("PagePadding"),
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

    private static T Resource<T>(string key)
        => Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? (T)value
            : default!;
}

public sealed record LegalSection(string Title, string Body);
