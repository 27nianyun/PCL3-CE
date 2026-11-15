using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO;
using PCL.Core.Logging;

namespace PCL.Core.Minecraft;

public static class SaveImportHelper
{
    /// <summary>存档压缩包常见扩展名（不区分大小写）。</summary>
    private static readonly string[] CompressedExtensions =
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tar.gz"
    };

    /// <summary>判断文件是否为受支持的压缩存档。</summary>
    public static bool IsCompressedSave(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var fileName = filePath.ToLowerInvariant();
        return CompressedExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     获取存档根目录。压缩包会先解压到 <paramref name="extractDirectory"/>，再从中定位存档根。
    ///     文件夹则直接定位。返回 <c>null</c> 表示无法识别有效的存档结构。
    /// </summary>
    public static string? GetSaveRootDirectory(string extractedDirectory)
    {
        if (string.IsNullOrWhiteSpace(extractedDirectory) || !Directory.Exists(extractedDirectory))
            return null;

        var rootDirectory = Path.GetFullPath(extractedDirectory);
        if (File.Exists(Path.Combine(rootDirectory, "level.dat")))
            return rootDirectory;

        var rootDirectories = Directory.GetDirectories(rootDirectory);
        if (rootDirectories.Length != 1)
            return null;

        var nestedDirectory = Path.GetFullPath(rootDirectories.Single());
        return File.Exists(Path.Combine(nestedDirectory, "level.dat")) ? nestedDirectory : null;
    }

    /// <summary>
    ///     解压压缩存档到指定目录，并返回存档根目录。失败或无法识别时返回 <c>null</c>。
    /// </summary>
    public static async Task<string?> ExtractAndGetSaveRootAsync(
        string compressedFilePath,
        string extractDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(compressedFilePath) || !File.Exists(compressedFilePath))
            return null;

        try
        {
            Directory.CreateDirectory(extractDirectory);
            await Files.ExtractFileAsync(compressedFilePath, extractDirectory, null, cancellationToken)
                .ConfigureAwait(false);
            return GetSaveRootDirectory(extractDirectory);
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, $"解压存档 {compressedFilePath} 失败");
            return null;
        }
    }
}
