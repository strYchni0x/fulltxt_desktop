using Fulltxt.Core.Crypto;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Cloud;

public sealed class NextcloudFolderIndexer(FileIndexRepository repository, ContentExtractorRegistry extractors, IndexingOptions options)
{
    public async Task<IndexingSummary> IndexAsync(FileSource source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (source.ServerUrl is null || source.Username is null || source.ProtectedCredential is null)
        {
            throw new InvalidOperationException("Nextcloud-Quelle ist unvollständig konfiguriert.");
        }

        var appPassword = SecretProtector.UnprotectString(source.ProtectedCredential);
        using var client = new NextcloudWebDavClient(source.ServerUrl, source.Username, appPassword);

        var items = await client.ListRecursiveAsync(source.RootPath, ct);
        var currentPaths = new HashSet<string>();
        int added = 0, updated = 0, skipped = 0, unchanged = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            currentPaths.Add(item.RelativePath);

            var existing = repository.TryGetFile(source.Id, item.RelativePath);
            // ETag ändert sich bei jeder Inhaltsänderung auf dem Server - günstiger als jedes Mal neu herunterzuladen.
            if (existing is not null && existing.ETag is not null && existing.ETag == item.ETag)
            {
                unchanged++;
                continue;
            }

            progress?.Report(item.RelativePath);

            var tooLarge = item.ContentLength > options.MaxFileSizeBytes;
            var extractor = tooLarge ? null : extractors.FindExtractor(item.DisplayName);
            string? text = null;
            if (extractor is not null)
            {
                try
                {
                    await using var stream = await client.DownloadAsync(item, ct);
                    text = await extractor.ExtractTextAsync(stream, ct);
                }
                catch (HttpRequestException)
                {
                    // Datei zwischen Auflisten und Download entfernt/nicht erreichbar - überspringen.
                }
            }

            var indexedFile = new IndexedFile
            {
                SourceId = source.Id,
                RelativePath = item.RelativePath,
                FileName = item.DisplayName,
                SizeBytes = item.ContentLength,
                ModifiedUtc = item.LastModifiedUtc,
                ETag = item.ETag,
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
