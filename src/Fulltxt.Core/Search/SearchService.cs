using System.Text;
using Fulltxt.Core.Data;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Search;

public sealed class SearchService(IndexDatabase database)
{
    /// <summary>Steuerzeichen, die Treffer im Snippet markieren (kollisionsfrei mit echtem Dateitext).</summary>
    public const char MatchStart = '\u0001';
    public const char MatchEnd = '\u0002';

    private static readonly System.Text.RegularExpressions.Regex WhitespaceRuns = new(@"\s+");

    public IReadOnlyList<SearchHit> Search(string query, int limit = 100)
    {
        var matchExpression = BuildMatchExpression(query);
        if (matchExpression is null) return [];

        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT f.id, f.file_name, f.relative_path, s.display_name, s.type, s.id, f.remote_id, f.web_url,
                   snippet(file_content_fts, 1, $open, $close, ' … ', 24) AS snippet
            FROM file_content_fts
            JOIN files f ON f.id = file_content_fts.rowid
            JOIN sources s ON s.id = f.source_id
            WHERE file_content_fts MATCH $match
            ORDER BY rank
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$match", matchExpression);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$open", MatchStart.ToString());
        cmd.Parameters.AddWithValue("$close", MatchEnd.ToString());

        using var reader = cmd.ExecuteReader();
        var results = new List<SearchHit>();
        while (reader.Read())
        {
            results.Add(new SearchHit
            {
                FileId = reader.GetInt64(0),
                FileName = reader.GetString(1),
                RelativePath = reader.GetString(2),
                SourceDisplayName = reader.GetString(3),
                SourceType = Enum.Parse<SourceType>(reader.GetString(4)),
                SourceId = reader.GetInt64(5),
                RemoteId = reader.IsDBNull(6) ? null : reader.GetString(6),
                WebUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
                Snippet = WhitespaceRuns.Replace(reader.IsDBNull(8) ? "" : reader.GetString(8), " "),
            });
        }
        return results;
    }

    /// <summary>Baut eine sichere FTS5-MATCH-Abfrage: jeder Suchbegriff wird als Phrase in
    /// Anführungszeichen gequotet (implizites AND), sodass Nutzereingaben nie als FTS5-Syntax
    /// (z.B. "NEAR", Operatoren, unausgeglichene Klammern) interpretiert werden.</summary>
    private static string? BuildMatchExpression(string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return null;

        var sb = new StringBuilder();
        foreach (var term in terms)
        {
            if (sb.Length > 0) sb.Append(" AND ");
            sb.Append('"').Append(term.Replace("\"", "\"\"")).Append('"');
        }
        return sb.ToString();
    }
}
