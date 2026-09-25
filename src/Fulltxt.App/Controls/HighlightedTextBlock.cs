using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Fulltxt.Core.Search;

namespace Fulltxt.App.Controls;

/// <summary>Zeigt einen Suchauszug und hebt die zwischen den Steuerzeichen
/// <see cref="SearchService.MatchStart"/>/<see cref="SearchService.MatchEnd"/> markierten Treffer hervor.</summary>
public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty MarkedTextProperty = DependencyProperty.Register(
        nameof(MarkedText), typeof(string), typeof(HighlightedTextBlock),
        new PropertyMetadata(string.Empty, (d, _) => ((HighlightedTextBlock)d).Rebuild()));

    public string MarkedText
    {
        get => (string)GetValue(MarkedTextProperty);
        set => SetValue(MarkedTextProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var text = MarkedText ?? string.Empty;
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

            var match = new Run(text[(start + 1)..end]) { FontWeight = FontWeights.SemiBold };
            match.SetResourceReference(TextElement.BackgroundProperty, "HighlightBrush");
            Inlines.Add(match);
            position = Math.Min(end + 1, text.Length);
        }
    }
}
