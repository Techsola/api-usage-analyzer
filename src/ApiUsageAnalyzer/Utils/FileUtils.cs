using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ApiUsageAnalyzer.Utils;

internal static class FileUtils
{
    /// <summary>
    /// Atomically creates a directory if no directory or other file system object exists at the specified path. The
    /// return value is safe from TOCTOU races.
    /// </summary>
    public static bool TryCreateChildDirectory(string existingDirectory, string newDirectoryName)
    {
        if (newDirectoryName.ContainsAny(Path.GetInvalidFileNameChars()))
            throw new ArgumentException($"The directory name '{newDirectoryName}' contains invalid characters.", nameof(newDirectoryName));

        if (!PInvoke.CreateDirectory(Path.Join(existingDirectory, newDirectoryName), lpSecurityAttributes: null))
        {
            var error = Marshal.GetLastWin32Error();
            return (WIN32_ERROR)error switch
            {
                WIN32_ERROR.ERROR_ALREADY_EXISTS => false,
                WIN32_ERROR.ERROR_PATH_NOT_FOUND => throw new DirectoryNotFoundException($"The path '{existingDirectory}' does not refer to an existing directory."),
                _ => throw new Win32Exception(error),
            };
        }

        return true;
    }

    public static void DeleteDirectory(string directory, bool recursive, bool deleteReadonlyFiles)
    {
        var readonlyFilesEnumerable = new FileSystemEnumerable<(string Path, FileAttributes Attributes)>(
            directory,
            (ref entry) => (entry.ToFullPath(), entry.Attributes),
            new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
            })
        {
            ShouldIncludePredicate = (ref entry) => !entry.IsDirectory && entry.Attributes.HasFlag(FileAttributes.ReadOnly),
        };

        if (!deleteReadonlyFiles)
        {
            var array = readonlyFilesEnumerable.Select(f => f.Path).ToArray();
            if (array is not [])
            {
                // This is the exception type that would be thrown if we called Directory.Delete with a readonly file in
                // the folder, but we want to avoid a partial delete if we know the delete will fail ahead of time.
                throw new UnauthorizedAccessException(new StringBuilder()
                    .Append($"The folder cannot be deleted. {nameof(deleteReadonlyFiles)} is false and ")
                    .AppendLine(array.Length == 1
                        ? "1 file is readonly:"
                        : array.Length + " files are readonly:")
                    .AppendJoin(Environment.NewLine, array)
                    .ToString());
            }
        }

        foreach (var file in readonlyFilesEnumerable)
            File.SetAttributes(file.Path, file.Attributes & ~FileAttributes.ReadOnly);

        Directory.Delete(directory, recursive);
    }
}
