using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Fulltxt.Core.Search;

namespace Fulltxt.Linux.Controls;

/// <summary>Zeigt einen Suchauszug und hebt die zwischen den Steuerzeichen
/// <see cref="SearchService.MatchStart"/>/<see cref="SearchService.MatchEnd"/> markierten Treffer hervor.</summary>
public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> MarkedTextProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, string?>(nameof(MarkedText));

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? MarkedText
    {
        get => GetValue(MarkedTextProperty);
        set => SetValue(MarkedTextProperty, value);
    }

    public HighlightedTextBlock()
    {
        MarkedTextProperty.Changed.AddClassHandler<HighlightedTextBlock>((block, _) => block.Rebuild());
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var text = MarkedText ?? string.Empty;
        var highlight = HighlightBrush();
        var position = 0;
        while (position < text.Length)
        {
            var start = text.IndexOf(SearchService.MatchStart, position);
            if (start < 0)
            {
                Inlines.Add(new Run(text[position..]));
                break;
            }

            if (start > position) Inlines.Add(new Run(text[position..start]));

            var end = text.IndexOf(SearchService.MatchEnd, start + 1);
            if (end < 0) end = text.Length;

            Inlines.Add(new Run(text[(start + 1)..end]) { FontWeight = FontWeight.SemiBold, Background = highlight });
            position = Math.Min(end + 1, text.Length);
        }
    }

    private IBrush? HighlightBrush() =>
        this.TryFindResource("HighlightBrush", ActualThemeVariant, out var resource) ? resource as IBrush : null;
}
