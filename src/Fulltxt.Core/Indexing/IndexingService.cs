using Fulltxt.Core.Cloud;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Indexing;

/// <summary>Einstiegspunkt für die WPF-App: indexiert eine Quelle unabhängig vom Typ
/// (lokaler Ordner oder Nextcloud) mit demselben Fortschritts-/Ergebnis-Vertrag.</summary>
public sealed class IndexingService(LocalFolderIndexer localIndexer, NextcloudFolderIndexer nextcloudIndexer)
{
    public Task<IndexingSummary> IndexSourceAsync(FileSource source, IProgress<string>? progress = null, CancellationToken ct = default) =>
        source.Type switch
        {
            SourceType.LocalFolder => localIndexer.IndexAsync(source, progress, ct),
            SourceType.Nextcloud => nextcloudIndexer.IndexAsync(source, progress, ct),
            _ => throw new NotSupportedException($"Unbekannter Quellentyp: {source.Type}"),
        };
}
