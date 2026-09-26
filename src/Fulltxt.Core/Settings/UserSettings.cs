using System.Text.Json;

namespace Fulltxt.Core.Settings;

/// <summary>Benutzereinstellungen in <c>settings.json</c>. Ein gemeinsames Objekt für die ganze App,
/// damit sich Darstellung und Suchlimit beim Speichern nicht gegenseitig überschreiben.</summary>
public sealed class UserSettings
{
    public const int DefaultMaxSearchResults = 100;

    /// <summary>Auswahl in den Einstellungen.</summary>
    public static readonly IReadOnlyList<int> SearchResultLimits = [50, 100, 250, 500, 1000];

    private string? filePath;
    private int maxSearchResults = DefaultMaxSearchResults;

    /// <summary>"System", "Light" oder "Dark" (Klartext, kompatibel zur ersten Dateiversion).</summary>
    public string Theme { get; set; } = "System";

    public int MaxSearchResults
    {
        get => maxSearchResults;
        set => maxSearchResults = value is >= 1 and <= 10_000 ? value : DefaultMaxSearchResults;
    }

    public static UserSettings Load(string path)
    {
        UserSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new UserSettings()
                : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new UserSettings();
        }
        settings.filePath = path;
        return settings;
    }

    public void Save()
    {
        if (filePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die Einstellung gilt für diese Sitzung weiter, nur die Persistenz schlägt fehl.
        }
    }
}
