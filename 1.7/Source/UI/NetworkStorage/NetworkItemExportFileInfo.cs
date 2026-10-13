using System;
using System.IO;
using System.Xml;

namespace SK_Matter_Network
{
    public class NetworkItemExportFileInfo
    {
        public FileInfo FileInfo;
        public string FileName;
        public DateTime LastWriteTime;
        public string GameVersion;
        public int StackCount;
        public int UnitCount;

        public static NetworkItemExportFileInfo FromFile(FileInfo fileInfo)
        {
            var info = new NetworkItemExportFileInfo
            {
                FileInfo = fileInfo,
                FileName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                LastWriteTime = fileInfo.LastWriteTime
            };
            info.LoadData();
            return info;
        }

        public void LoadData()
        {
            var doc = new XmlDocument();
            doc.Load(FileInfo.FullName);

            XmlNode root = doc.DocumentElement;
            GameVersion = root?.SelectSingleNode("meta/gameVersion")?.InnerText;

            XmlNode payload = root?.SelectSingleNode("itemExport");
            int.TryParse(payload?.SelectSingleNode("exportedStackCount")?.InnerText, out StackCount);
            int.TryParse(payload?.SelectSingleNode("exportedUnitCount")?.InnerText, out UnitCount);
        }
    }
}
