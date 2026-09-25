using Fulltxt.Core.Models;
using Microsoft.Data.Sqlite;

namespace Fulltxt.Core.Data;

public sealed class FileIndexRepository(IndexDatabase database)
{
    private const string Columns =
        "id, source_id, relative_path, file_name, size_bytes, modified_utc, content_hash, etag, indexed_utc, skipped, remote_id, web_url";

    /// <summary>Liefert die gespeicherten Metadaten einer Datei, um unveränderte Dateien beim
    /// erneuten Scan überspringen zu können (Delta-Indexierung).</summary>
    public IndexedFile? TryGetFile(long sourceId, string relativePath)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM files WHERE source_id = $sourceId AND relative_path = $relativePath;";
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.Parameters.AddWithValue("$relativePath", relativePath);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IndexedFile? TryGetByRemoteId(long sourceId, string remoteId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM files WHERE source_id = $sourceId AND remote_id = $remoteId;";
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.Parameters.AddWithValue("$remoteId", remoteId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IndexedFile? GetById(long fileId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM files WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", fileId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Legt eine Datei an oder aktualisiert sie inkl. Volltext-Index. Cloud-Dateien werden
    /// über ihre Remote-ID wiedererkannt, sodass verschobene/umbenannte Dateien keine Dubletten erzeugen.
    /// Bei <paramref name="content"/> == null wird nur der Metadaten-Eintrag geführt, ohne FTS-Zeile.</summary>
    public long Upsert(IndexedFile file, string? content)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        long? existingId = null;
        if (file.RemoteId is not null)
        {
            existingId = FindId(connection, transaction,
                "SELECT id FROM files WHERE source_id = $sourceId AND remote_id = $key;", file.SourceId, file.RemoteId);
        }
        existingId ??= FindId(connection, transaction,
            "SELECT id FROM files WHERE source_id = $sourceId AND relative_path = $key;", file.SourceId, file.RelativePath);

        long fileId;
        if (existingId is { } id)
        {
            // Anderer Eintrag auf dem Zielpfad (z.B. Datei wurde dorthin verschoben) würde UNIQUE verletzen.
            using (var clash = connection.CreateCommand())
            {
                clash.Transaction = transaction;
                clash.CommandText = """
                    DELETE FROM file_content_fts WHERE rowid IN (SELECT id FROM files WHERE source_id = $sourceId AND relative_path = $path AND id <> $id);
                    DELETE FROM files WHERE source_id = $sourceId AND relative_path = $path AND id <> $id;
                    """;
                clash.Parameters.AddWithValue("$sourceId", file.SourceId);
                clash.Parameters.AddWithValue("$path", file.RelativePath);
                clash.Parameters.AddWithValue("$id", id);
                clash.ExecuteNonQuery();
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE files SET relative_path = $relativePath, file_name = $fileName, size_bytes = $sizeBytes,
                    modified_utc = $modifiedUtc, content_hash = $contentHash, etag = $etag, indexed_utc = $indexedUtc,
                    skipped = $skipped, remote_id = $remoteId, web_url = $webUrl
                WHERE id = $id;
                """;
            BindFile(update, file);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
            fileId = id;
        }
        else
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO files (source_id, relative_path, file_name, size_bytes, modified_utc, content_hash, etag, indexed_utc, skipped, remote_id, web_url)
                VALUES ($sourceId, $relativePath, $fileName, $sizeBytes, $modifiedUtc, $contentHash, $etag, $indexedUtc, $skipped, $remoteId, $webUrl);
                SELECT last_insert_rowid();
                """;
            BindFile(insert, file);
            insert.Parameters.AddWithValue("$sourceId", file.SourceId);
            fileId = (long)insert.ExecuteScalar()!;
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

    /// <summary>Aktualisiert nur Pfad/Name/Link einer bereits indexierten, inhaltlich unveränderten Datei
    /// (z.B. nach Verschieben in der Cloud) - ohne erneuten Download.</summary>
    public void RefreshMetadata(long fileId, string relativePath, string fileName, string? webUrl, string? remoteId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                DELETE FROM file_content_fts WHERE rowid IN (SELECT id FROM files WHERE source_id = (SELECT source_id FROM files WHERE id = $id) AND relative_path = $path AND id <> $id);
                DELETE FROM files WHERE source_id = (SELECT source_id FROM files WHERE id = $id) AND relative_path = $path AND id <> $id;
                UPDATE files SET relative_path = $path, file_name = $name, web_url = $webUrl, remote_id = COALESCE($remoteId, remote_id) WHERE id = $id;
                UPDATE file_content_fts SET file_name = $name WHERE rowid = $id;
                """;
            cmd.Parameters.AddWithValue("$id", fileId);
            cmd.Parameters.AddWithValue("$path", relativePath);
            cmd.Parameters.AddWithValue("$name", fileName);
            cmd.Parameters.AddWithValue("$webUrl", (object?)webUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$remoteId", (object?)remoteId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
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
        return DeleteWhere(sourceId, toDelete, "relative_path = $key");
    }

    /// <summary>Löscht Dateien anhand ihrer Anbieter-ID (OneDrive-Delta: gelöschte Elemente).</summary>
    public int DeleteByRemoteIds(long sourceId, IEnumerable<string> remoteIds) =>
        DeleteWhere(sourceId, remoteIds.Distinct().ToList(), "remote_id = $key");

    /// <summary>Löscht eine Datei oder alle Dateien unterhalb eines (gelöschten) Ordners, Groß-/Kleinschreibung
    /// ignorierend (Dropbox liefert Löschungen nur als path_lower).</summary>
    public int DeleteByPathOrFolder(long sourceId, IEnumerable<string> paths)
    {
        var deleted = 0;
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var path in paths.Distinct())
        {
            var trimmed = path.TrimEnd('/');
            if (trimmed.Length == 0) continue;

            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                DELETE FROM file_content_fts WHERE rowid IN (
                    SELECT id FROM files WHERE source_id = $sourceId AND
                        (lower(relative_path) = lower($path) OR substr(lower(relative_path), 1, length($path) + 1) = lower($path) || '/'));
                DELETE FROM files WHERE source_id = $sourceId AND
                        (lower(relative_path) = lower($path) OR substr(lower(relative_path), 1, length($path) + 1) = lower($path) || '/');
                SELECT changes();
                """;
            cmd.Parameters.AddWithValue("$sourceId", sourceId);
            cmd.Parameters.AddWithValue("$path", trimmed);
            deleted += Convert.ToInt32(cmd.ExecuteScalar());
        }
        transaction.Commit();
        return deleted;
    }

    public int CountFiles(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM files WHERE source_id = $sourceId;";
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int DeleteWhere(long sourceId, IReadOnlyList<string> keys, string condition)
    {
        if (keys.Count == 0) return 0;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var key in keys)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            // Zwei Anweisungen pro Datei (FTS-Zeile + Metadaten); ExecuteNonQuery summiert die
            // betroffenen Zeilen beider Statements, deshalb zählen wir die gelöschten Dateien separat.
            cmd.CommandText = $"""
                DELETE FROM file_content_fts WHERE rowid IN (SELECT id FROM files WHERE source_id = $sourceId AND {condition});
                DELETE FROM files WHERE source_id = $sourceId AND {condition};
                """;
            cmd.Parameters.AddWithValue("$sourceId", sourceId);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
        return keys.Count;
    }

    private static long? FindId(SqliteConnection connection, SqliteTransaction transaction, string sql, long sourceId, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    private static void BindFile(SqliteCommand cmd, IndexedFile file)
    {
        cmd.Parameters.AddWithValue("$relativePath", file.RelativePath);
        cmd.Parameters.AddWithValue("$fileName", file.FileName);
        cmd.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
        cmd.Parameters.AddWithValue("$modifiedUtc", file.ModifiedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$contentHash", (object?)file.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$etag", (object?)file.ETag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$indexedUtc", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$skipped", file.Skipped ? 1 : 0);
        cmd.Parameters.AddWithValue("$remoteId", (object?)file.RemoteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$webUrl", (object?)file.WebUrl ?? DBNull.Value);
    }

    private static IndexedFile Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SourceId = reader.GetInt64(1),
        RelativePath = reader.GetString(2),
        FileName = reader.GetString(3),
        SizeBytes = reader.GetInt64(4),
        ModifiedUtc = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        ContentHash = reader.IsDBNull(6) ? null : reader.GetString(6),
        ETag = reader.IsDBNull(7) ? null : reader.GetString(7),
        IndexedUtc = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Skipped = reader.GetInt32(9) != 0,
        RemoteId = reader.IsDBNull(10) ? null : reader.GetString(10),
        WebUrl = reader.IsDBNull(11) ? null : reader.GetString(11),
    };
}
