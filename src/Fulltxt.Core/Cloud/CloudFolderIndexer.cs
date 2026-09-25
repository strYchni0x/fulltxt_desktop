using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Cloud;

/// <summary>
/// Indexiert ein Cloud-Konto ohne die Dateien dauerhaft herunterzuladen: Jede Datei wird einmal gelesen,
/// ihr Text im verschlüsselten Index abgelegt und der Inhalt sofort verworfen. Später wird immer nur die
/// gerade benötigte Datei einzeln geladen oder per Link im Browser geöffnet.
/// </summary>
public sealed class CloudFolderIndexer(
    SourceRepository sources,
    FileIndexRepository files,
    ContentExtractorRegistry extractors,
    IndexingOptions options,
    CloudConnectorFactory connectorFactory)
{
    public async Task<IndexingSummary> IndexAsync(FileSource source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Frisch laden: Cursor und (rotierte) Zugangsdaten können sich seit dem Anlegen des Objekts geändert haben.
        var current = sources.GetById(source.Id) ?? source;

        using var connector = connectorFactory.Create(current, credential => sources.UpdateCredential(current.Id, credential));
        var changes = await connector.GetChangesAsync(current.SyncCursor, ct).ConfigureAwait(false);

        var currentPaths = new HashSet<string>();
        int added = 0, updated = 0, skipped = 0, unchanged = 0, failed = 0;

        foreach (var item in changes.Changed)
        {
            ct.ThrowIfCancellationRequested();

            var extractor = extractors.FindExtractor(item.Name);
            if (extractor is null) continue; // Format ohne Textextraktion: gar nicht erst aufnehmen

            currentPaths.Add(item.Path);

            var existing = files.TryGetByRemoteId(current.Id, item.RemoteId) ?? files.TryGetFile(current.Id, item.Path);
            if (existing is not null && existing.ETag is not null && existing.ETag == item.ETag)
            {
                if (existing.RelativePath != item.Path || existing.FileName != item.Name
                    || existing.WebUrl != item.WebUrl || existing.RemoteId != item.RemoteId)
                {
                    files.RefreshMetadata(existing.Id, item.Path, item.Name, item.WebUrl, item.RemoteId);
                }
                unchanged++;
                continue;
            }

            progress?.Report(item.Path);

            string? text = null;
            if (item.Size <= options.MaxFileSizeBytes)
            {
                try
                {
                    await using var stream = await connector.OpenReadAsync(new CloudFileRef(item.RemoteId, item.Path, item.Name), ct).ConfigureAwait(false);
                    text = await extractor.ExtractTextAsync(stream, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is CloudException or HttpRequestException or IOException)
                {
                    // Vorübergehend nicht lesbar: NICHT als "erledigt" speichern, damit der nächste Lauf es erneut versucht.
                    failed++;
                    continue;
                }
            }

            files.Upsert(new IndexedFile
            {
                SourceId = current.Id,
                RelativePath = item.Path,
                FileName = item.Name,
                SizeBytes = item.Size,
                ModifiedUtc = item.ModifiedUtc,
                ETag = item.ETag,
                RemoteId = item.RemoteId,
                WebUrl = item.WebUrl,
                Skipped = text is null,
            }, text);

            if (existing is null) added++; else updated++;
            if (text is null) skipped++;
        }

        var deleted = 0;
        if (changes.IsFullListing)
        {
            deleted = files.DeleteMissing(current.Id, currentPaths);
        }
        else
        {
            deleted += files.DeleteByRemoteIds(current.Id, changes.DeletedRemoteIds);
            deleted += files.DeleteByPathOrFolder(current.Id, changes.DeletedPaths);
        }

        // Cursor nur weiterschieben, wenn nichts fehlgeschlagen ist - sonst würden übersprungene Änderungen verloren gehen.
        if (failed == 0 && changes.NextCursor != current.SyncCursor)
        {
            sources.UpdateSyncCursor(current.Id, changes.NextCursor);
        }

        return new IndexingSummary(added, updated, deleted, skipped, unchanged, failed);
    }
}
