namespace DumpToTxt.Core;

/// <summary>Refuses stable linked inputs before following their contents or policy files.</summary>
internal static class FilesystemSafety
{
    public static void EnsureDirectoryPath(string path)
    {
        var directories = new Stack<string>();
        string? directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (directory is not null)
        {
            directories.Push(directory);
            directory = Path.GetDirectoryName(directory);
        }
        foreach (string component in directories)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(component); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Directory link was not traversed: '{component}'. Select the original folder instead.");
        }
    }

    public static void EnsureTargetPath(string path)
    {
        string full = Path.GetFullPath(path);
        EnsureDirectoryPath(File.Exists(full) ? Path.GetDirectoryName(full)! : full);
        if (File.Exists(full)) TextFileClassifier.EnsureRegularFile(full);
    }
}
