namespace PackageDeployer.Core;

public static class FileCopier
{
    public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
        {
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
        }
        foreach (var file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }
    }
}