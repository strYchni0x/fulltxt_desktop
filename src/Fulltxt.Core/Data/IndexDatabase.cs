using Fulltxt.Core.Models;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Fulltxt.Core.Data;

/// <summary>
/// Zugriff auf den lokalen, mit SQLCipher verschlüsselten Suchindex. Enthält Metadaten- und
/// eine FTS5-Volltexttabelle. Es verlässt nie Klartext-Inhalt den Rechner - die DB-Datei ist
/// ohne den DPAPI-geschützten Schlüssel (siehe Crypto/IndexKeyStore) nicht lesbar.
/// </summary>
public sealed class IndexDatabase : IDisposable
{
    private static bool _providerRegistered;
    private readonly string _connectionString;
    private readonly string _keyHex;

    public IndexDatabase(string databasePath, string keyHex)
    {
        EnsureProviderRegistered();
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Pooling deaktiviert: verhindert, dass eine bereits mit dem richtigen Schlüssel entsperrte
        // gepoolte Verbindung wiederverwendet wird, wenn (z.B. in Tests) ein anderer Schlüssel geprüft wird.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        _keyHex = keyHex;
        using var connection = OpenConnection();
        EnsureSchema(connection);
    }

    private static void EnsureProviderRegistered()
    {
        if (_providerRegistered) return;
        raw.SetProvider(new SQLite3Provider_e_sqlcipher());
        _providerRegistered = true;
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var keyCmd = connection.CreateCommand())
        {
            // Muss die erste Anweisung nach dem Öffnen sein (SQLCipher-Konvention).
            keyCmd.CommandText = $"PRAGMA key = \"x'{_keyHex}'\";";
            keyCmd.ExecuteNonQuery();
        }
        using (var checkCmd = connection.CreateCommand())
        {
            // Erzwingt eine echte Seiten-Lesung - bei falschem Schlüssel schlägt das mit
            // SqliteException ("file is not a database") fehl, statt still leere Tabellen zu liefern.
            checkCmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            checkCmd.ExecuteScalar();
        }
        using (var fkCmd = connection.CreateCommand())
        {
            fkCmd.CommandText = "PRAGMA foreign_keys = ON;";
            fkCmd.ExecuteNonQuery();
        }
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sources (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type TEXT NOT NULL,
                display_name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                server_url TEXT,
                username TEXT,
                credential_protected BLOB,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_id INTEGER NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
                relative_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                modified_utc TEXT NOT NULL,
                content_hash TEXT,
                etag TEXT,
                indexed_utc TEXT NOT NULL,
                skipped INTEGER NOT NULL DEFAULT 0,
                UNIQUE(source_id, relative_path)
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS file_content_fts USING fts5(
                file_name,
                content,
                tokenize='unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Verbindungen werden pro Aufruf über using geöffnet/geschlossen; nichts vorzuhalten.
    }
}
