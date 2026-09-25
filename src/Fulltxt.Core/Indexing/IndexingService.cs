using Fulltxt.Core.Cloud;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Indexing;

/// <summary>Einstiegspunkt für die WPF-App: indexiert eine Quelle unabhängig vom Typ
/// (lokaler Ordner oder Cloud-Konto) mit demselben Fortschritts-/Ergebnis-Vertrag.</summary>
public sealed class IndexingService(LocalFolderIndexer localIndexer, CloudFolderIndexer cloudIndexer)
{
    public Task<IndexingSummary> IndexSourceAsync(FileSource source, IProgress<string>? progress = null, CancellationToken ct = default) =>
        source.Type.IsCloud()
            ? cloudIndexer.IndexAsync(source, progress, ct)
            : localIndexer.IndexAsync(source, progress, ct);
}
