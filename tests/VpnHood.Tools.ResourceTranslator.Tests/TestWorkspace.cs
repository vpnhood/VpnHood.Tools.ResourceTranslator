namespace VpnHood.ResourceTranslator.Tests;

/// <summary>A throwaway directory that cleans itself up, for tests that touch the file system.</summary>
public sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vhtranslator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var dir = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string ReadFile(string relativePath)
    {
        return File.ReadAllText(System.IO.Path.Combine(Path, relativePath));
    }

    public bool Exists(string relativePath)
    {
        return File.Exists(System.IO.Path.Combine(Path, relativePath));
    }

    public void Dispose()
    {
        try {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException) {
            // A locked file must not fail an otherwise passing test; the temp folder is disposable.
        }
    }
}
