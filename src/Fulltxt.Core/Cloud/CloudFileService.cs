using Fulltxt.Core.Data;
using Fulltxt.Core.Models;

namespace Fulltxt.Core.Cloud;

/// <summary>Lädt bei Bedarf genau eine Datei aus der Cloud herunter (statt den ganzen Bestand lokal vorzuhalten).</summary>
public sealed class CloudFileService(SourceRepository sources, FileIndexRepository files, CloudConnectorFactory connectorFactory)
{
    /// <summary>Speichert die Datei im Zielordner (bei Namensgleichheit mit Zähler) und liefert den Pfad.</summary>
    public async Task<string> DownloadAsync(long fileId, string targetDirectory, CancellationToken ct = default)
    {
        var file = files.GetById(fileId) ?? throw new CloudException("Die Datei ist nicht mehr im Index.");
        var source = sources.GetById(file.SourceId) ?? throw new CloudException("Die zugehörige Quelle existiert nicht mehr.");
        if (!source.Type.IsCloud() || string.IsNullOrEmpty(file.RemoteId))
        {
            throw new CloudException("Diese Datei liegt nicht in einem Cloud-Konto.");
        }

        Directory.CreateDirectory(targetDirectory);
        var target = UniquePath(targetDirectory, SanitizeFileName(file.FileName));

        using var connector = connectorFactory.Create(source, credential => sources.UpdateCredential(source.Id, credential));
        try
        {
            await using var input = await connector.OpenReadAsync(new CloudFileRef(file.RemoteId, file.RelativePath, file.FileName), ct).ConfigureAwait(false);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        catch
        {
            // Halb geschriebene Datei nicht liegen lassen.
            try { File.Delete(target); } catch (IOException) { }
            throw;
        }
        return target;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Datei" : cleaned;
    }

    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var counter = 1; File.Exists(candidate); counter++)
        {
            candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");
        }
        return candidate;
    }
}
