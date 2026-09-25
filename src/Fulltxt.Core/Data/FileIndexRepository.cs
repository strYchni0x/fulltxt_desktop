using Fulltxt.Core.Models;
using Microsoft.Data.Sqlite;

namespace Fulltxt.Core.Data;

public sealed class FileIndexRepository(IndexDatabase database)
{
    /// <summary>Liefert die gespeicherten Metadaten einer Datei, um unveränderte Dateien beim
    /// erneuten Scan überspringen zu können (Delta-Indexierung).</summary>
    public IndexedFile? TryGetFile(long sourceId, string relativePath)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_id, relative_path, file_name, size_bytes, modified_utc, content_hash, etag, indexed_utc, skipped
            FROM files WHERE source_id = $sourceId AND relative_path = $relativePath;
            """;
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.Parameters.AddWithValue("$relativePath", relativePath);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Legt eine Datei an oder aktualisiert sie inkl. Volltext-Index. Bei <paramref name="content"/>
    /// == null (übersprungene/zu große Datei) wird nur der Metadaten-Eintrag geführt, ohne FTS-Zeile.</summary>
    public long Upsert(IndexedFile file, string? content)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        long fileId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO files (source_id, relative_path, file_name, size_bytes, modified_utc, content_hash, etag, indexed_utc, skipped)
                VALUES ($sourceId, $relativePath, $fileName, $sizeBytes, $modifiedUtc, $contentHash, $etag, $indexedUtc, $skipped)
                ON CONFLICT(source_id, relative_path) DO UPDATE SET
                    file_name = excluded.file_name,
                    size_bytes = excluded.size_bytes,
                    modified_utc = excluded.modified_utc,
                    content_hash = excluded.content_hash,
                    etag = excluded.etag,
                    indexed_utc = excluded.indexed_utc,
                    skipped = excluded.skipped
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$sourceId", file.SourceId);
            cmd.Parameters.AddWithValue("$relativePath", file.RelativePath);
            cmd.Parameters.AddWithValue("$fileName", file.FileName);
            cmd.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
            cmd.Parameters.AddWithValue("$modifiedUtc", file.ModifiedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$contentHash", (object?)file.ContentHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$etag", (object?)file.ETag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$indexedUtc", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$skipped", file.Skipped ? 1 : 0);
            fileId = (long)cmd.ExecuteScalar()!;
        }

        using (var deleteFtsCmd = connection.CreateCommand())
        {
            deleteFtsCmd.Transaction = transaction;
            deleteFtsCmd.CommandText = "DELETE FROM file_content_fts WHERE rowid = $fileId;";
            deleteFtsCmd.Parameters.AddWithValue("$fileId", fileId);
            deleteFtsCmd.ExecuteNonQuery();
        }

        if (content is not null)
        {
            using var insertFtsCmd = connection.CreateCommand();
            insertFtsCmd.Transaction = transaction;
            insertFtsCmd.CommandText = "INSERT INTO file_content_fts (rowid, file_name, content) VALUES ($fileId, $fileName, $content);";
            insertFtsCmd.Parameters.AddWithValue("$fileId", fileId);
            insertFtsCmd.Parameters.AddWithValue("$fileName", file.FileName);
            insertFtsCmd.Parameters.AddWithValue("$content", content);
            insertFtsCmd.ExecuteNonQuery();
        }

        transaction.Commit();
        return fileId;
    }

    public HashSet<string> GetAllRelativePaths(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT relative_path FROM files WHERE source_id = $sourceId;";
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        using var reader = cmd.ExecuteReader();
        var result = new HashSet<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    /// <summary>Entfernt Dateien, die beim letzten Scan verschwunden waren (lokal gelöscht /
    /// aus der Cloud entfernt), inkl. ihrer FTS-Einträge.</summary>
    public int DeleteMissing(long sourceId, IReadOnlySet<string> currentRelativePaths)
    {
        var toDelete = GetAllRelativePaths(sourceId).Where(p => !currentRelativePaths.Contains(p)).ToList();
        if (toDelete.Count == 0) return 0;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var path in toDelete)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            // Zwei Anweisungen pro Datei (FTS-Zeile + Metadaten); ExecuteNonQuery summiert die
            // betroffenen Zeilen beider Statements, deshalb zählen wir die gelöschten Dateien separat.
            cmd.CommandText = """
                DELETE FROM file_content_fts WHERE rowid IN (SELECT id FROM files WHERE source_id = $sourceId AND relative_path = $path);
                DELETE FROM files WHERE source_id = $sourceId AND relative_path = $path;
                """;
            cmd.Parameters.AddWithValue("$sourceId", sourceId);
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
        return toDelete.Count;
    }

    public int CountFiles(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM files WHERE source_id = $sourceId;";
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static IndexedFile Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SourceId = reader.GetInt64(1),
        RelativePath = reader.GetString(2),
        FileName = reader.GetString(3),
        SizeBytes = reader.GetInt64(4),
        ModifiedUtc = DateTime.Parse(reader.GetString(5)),
        ContentHash = reader.IsDBNull(6) ? null : reader.GetString(6),
        ETag = reader.IsDBNull(7) ? null : reader.GetString(7),
        IndexedUtc = DateTime.Parse(reader.GetString(8)),
        Skipped = reader.GetInt32(9) != 0,
    };
}
