using Fulltxt.Core.Data;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Indexing;

public sealed class LocalFolderIndexer(FileIndexRepository repository, ContentExtractorRegistry extractors, IndexingOptions options)
{
    public async Task<IndexingSummary> IndexAsync(FileSource source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var currentPaths = new HashSet<string>();
        int added = 0, updated = 0, skipped = 0, unchanged = 0;

        foreach (var filePath in SafeDirectoryWalker.EnumerateFiles(source.RootPath))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(source.RootPath, filePath);
            currentPaths.Add(relativePath);

            FileInfo info;
            try
            {
                info = new FileInfo(filePath);
            }
            catch (IOException)
            {
                continue;
            }

            var existing = repository.TryGetFile(source.Id, relativePath);
            if (existing is not null && existing.ModifiedUtc == info.LastWriteTimeUtc && existing.SizeBytes == info.Length)
            {
                unchanged++;
                continue;
            }

            progress?.Report(relativePath);

            var tooLarge = info.Length > options.MaxFileSizeBytes;
            var extractor = tooLarge ? null : extractors.FindExtractor(filePath);
            string? text = null;
            if (extractor is not null)
            {
                try
                {
                    await using var stream = File.OpenRead(filePath);
                    text = await extractor.ExtractTextAsync(stream, ct);
                }
                catch (IOException)
                {
                    // Datei gerade gesperrt/in Benutzung - nächstes Mal erneut versuchen.
                }
            }

            var indexedFile = new IndexedFile
            {
                SourceId = source.Id,
                RelativePath = relativePath,
                FileName = Path.GetFileName(filePath),
                SizeBytes = info.Length,
                ModifiedUtc = info.LastWriteTimeUtc,
                Skipped = text is null,
            };
            repository.Upsert(indexedFile, text);

            if (existing is null) added++; else updated++;
            if (text is null) skipped++;
        }

        var deleted = repository.DeleteMissing(source.Id, currentPaths);
        return new IndexingSummary(added, updated, deleted, skipped, unchanged);
    }
}
