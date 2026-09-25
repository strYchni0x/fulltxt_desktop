namespace Fulltxt.Core.Indexing;

public sealed class ContentExtractorRegistry
{
    private readonly IReadOnlyList<IContentExtractor> _extractors;

    /// <summary>Standardumfang: Text/Code, Word, PDF, Excel, PowerPoint. Bewusst der einzige öffentliche
    /// Konstruktor - ein Konstruktor mit <c>IEnumerable</c>-Parameter würde vom DI-Container mit einer
    /// leeren Liste aufgerufen und die Standard-Extraktoren stillschweigend verdrängen.</summary>
    public ContentExtractorRegistry()
        : this([new PlainTextExtractor(), new DocxExtractor(), new PdfExtractor(), new XlsxExtractor(), new PptxExtractor()])
    {
    }

    private ContentExtractorRegistry(IReadOnlyList<IContentExtractor> extractors) => _extractors = extractors;

    public static ContentExtractorRegistry With(IEnumerable<IContentExtractor> extractors) => new(extractors.ToList());

    public IContentExtractor? FindExtractor(string fileName) =>
        _extractors.FirstOrDefault(e => e.CanHandle(fileName));
}
