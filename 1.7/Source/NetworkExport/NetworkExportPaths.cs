using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace SK_Matter_Network
{
    public static class NetworkExportPaths
    {
        public const string FileExtension = ".mnitems";

        public static string RootPath => EnsureDirectory(Path.Combine(GenFilePaths.ConfigFolderPath, "MatterNetwork"));

        public static string ExportsPath => EnsureDirectory(Path.Combine(RootPath, "Exports"));

        public static string TempPath => EnsureDirectory(Path.Combine(GenFilePaths.TempFolderPath, "MatterNetwork"));

        public static string FilePathFor(string exportName)
        {
            string sanitized = GenFile.SanitizedFileName(exportName);
            return Path.Combine(ExportsPath, sanitized + FileExtension);
        }

        public static IEnumerable<FileInfo> AllExportFiles()
        {
            return new DirectoryInfo(ExportsPath)
                .GetFiles("*" + FileExtension)
                .OrderByDescending(f => f.LastWriteTime);
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
