using Fulltxt.Core.Models;
using Microsoft.Data.Sqlite;

namespace Fulltxt.Core.Data;

public sealed class SourceRepository(IndexDatabase database)
{
    private const string Columns =
        "id, type, display_name, root_path, server_url, username, credential_protected, created_utc, sync_cursor";

    public long Add(FileSource source)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sources (type, display_name, root_path, server_url, username, credential_protected, created_utc, sync_cursor)
            VALUES ($type, $displayName, $rootPath, $serverUrl, $username, $credential, $createdUtc, $syncCursor);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$type", source.Type.ToString());
        cmd.Parameters.AddWithValue("$displayName", source.DisplayName);
        cmd.Parameters.AddWithValue("$rootPath", source.RootPath);
        cmd.Parameters.AddWithValue("$serverUrl", (object?)source.ServerUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$username", (object?)source.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$credential", (object?)source.ProtectedCredential ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$syncCursor", (object?)source.SyncCursor ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<FileSource> GetAll()
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM sources ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        var result = new List<FileSource>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public FileSource? GetById(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM sources WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sourceId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Speichert erneuerte Zugangsdaten (z.B. rotierte OAuth-Refresh-Tokens) - bereits DPAPI-geschützt.</summary>
    public void UpdateCredential(long sourceId, byte[] protectedCredential)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sources SET credential_protected = $credential WHERE id = $id;";
        cmd.Parameters.AddWithValue("$credential", protectedCredential);
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSyncCursor(long sourceId, string? cursor)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sources SET sync_cursor = $cursor WHERE id = $id;";
        cmd.Parameters.AddWithValue("$cursor", (object?)cursor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", sourceId);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long sourceId)
    {
        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        // Zugehörige FTS-Zeilen erst löschen (kein automatisches ON DELETE CASCADE für virtuelle Tabellen).
        cmd.CommandText = """
            DELETE FROM file_content_fts WHERE rowid IN (SELECT id FROM files WHERE source_id = $sourceId);
            DELETE FROM sources WHERE id = $sourceId;
            """;
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.ExecuteNonQuery();
    }

    private static FileSource Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Type = Enum.Parse<SourceType>(reader.GetString(1)),
        DisplayName = reader.GetString(2),
        RootPath = reader.GetString(3),
        ServerUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
        Username = reader.IsDBNull(5) ? null : reader.GetString(5),
        ProtectedCredential = reader.IsDBNull(6) ? null : (byte[])reader[6],
        CreatedUtc = DateTime.Parse(reader.GetString(7)),
        SyncCursor = reader.IsDBNull(8) ? null : reader.GetString(8),
    };
}
