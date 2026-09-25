namespace Fulltxt.Core.Indexing;

public interface IContentExtractor
{
    bool CanHandle(string fileName);

    /// <summary>Extrahiert reinen Text aus dem Dateiinhalt. Gibt null zurück, wenn keine Nutzdaten
    /// gewonnen werden konnten (z.B. leere/kaputte Datei) - die Datei wird dann als "skipped" geführt.</summary>
    Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default);
}
