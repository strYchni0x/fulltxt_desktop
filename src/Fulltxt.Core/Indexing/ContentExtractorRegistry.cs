namespace Fulltxt.Core.Indexing;

public sealed class ContentExtractorRegistry
{
    private readonly IReadOnlyList<IContentExtractor> _extractors;

    public ContentExtractorRegistry(IEnumerable<IContentExtractor>? extractors = null)
    {
        _extractors = extractors?.ToList() ?? [new PlainTextExtractor(), new DocxExtractor()];
    }

    public IContentExtractor? FindExtractor(string fileName) =>
        _extractors.FirstOrDefault(e => e.CanHandle(fileName));
}
