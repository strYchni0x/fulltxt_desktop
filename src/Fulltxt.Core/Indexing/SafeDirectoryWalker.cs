namespace Fulltxt.Core.Indexing;

/// <summary>Rekursive Verzeichnis-Traversierung, die einzelne unlesbare Unterordner
/// (Zugriffsrechte, kaputte Junctions) überspringt statt den gesamten Scan abzubrechen -
/// <c>Directory.EnumerateFiles(..., AllDirectories)</c> würde beim ersten Fehler abbrechen.</summary>
public static class SafeDirectoryWalker
{
    public static IEnumerable<string> EnumerateFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var currentDir = pending.Pop();
            var dirName = Path.GetFileName(currentDir);
            if (dirName.Length > 0 && IndexingOptions.ExcludedDirectoryNames.Contains(dirName))
            {
                continue;
            }

            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(currentDir);
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
            foreach (var subDir in subDirs)
            {
                pending.Push(subDir);
            }
        }
    }
}
