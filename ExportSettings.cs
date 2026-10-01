using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BatchNwcExport
{
    /// <summary>Настройки экспорта. Хранятся в %AppData%\BatchNwcExport\settings.json.</summary>
    [DataContract]
    public class ExportSettings
    {
        [DataMember] public List<string> Files;

        /// <summary>Имена 3D-видов через ";" — берётся первый найденный. Сравнение без учёта регистра.</summary>
        [DataMember] public string ViewNames;

        [DataMember] public string OutputFolder;

        /// <summary>Суффикс к имени NWC (например "_KR"). Поддерживает {date} → yyyyMMdd.</summary>
        [DataMember] public string FileSuffix;

        [DataMember] public bool SharedCoordinates;
        [DataMember] public bool ExportLinks;
        [DataMember] public bool ConvertProperties;
        [DataMember] public bool ExportRoomGeometry;
        [DataMember] public bool DivideByLevels;
        [DataMember] public bool ExportElementIds;
        [DataMember] public bool ExportParts;
        [DataMember] public bool FindMissingMaterials;
        [DataMember] public bool OpenAllWorksets;

        public ExportSettings() => SetDefaults();

        // DataContractJsonSerializer не вызывает конструктор — выставляем значения по умолчанию сами,
        // чтобы отсутствующие в старом JSON поля не оказались null/false.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _) => SetDefaults();

        private void SetDefaults()
        {
            Files = new List<string>();
            ViewNames = "Navisworks";
            OutputFolder = "";
            FileSuffix = "";
            SharedCoordinates = true;
            ExportLinks = false;
            ConvertProperties = true;
            ExportRoomGeometry = false;
            DivideByLevels = true;
            ExportElementIds = true;
            ExportParts = false;
            FindMissingMaterials = true;
            OpenAllWorksets = true;
        }

        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "BatchNwcExport", "settings.json");

        public static ExportSettings Load(string path = null)
        {
            path ??= SettingsPath;
            try
            {
                if (!File.Exists(path))
                    return new ExportSettings();
                using var fs = File.OpenRead(path);
                var s = (ExportSettings)new DataContractJsonSerializer(typeof(ExportSettings)).ReadObject(fs);
                s.Files ??= new List<string>();
                return s;
            }
            catch
            {
                return new ExportSettings();
            }
        }

        public void Save(string path = null)
        {
            path ??= SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var ms = new MemoryStream();
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true))
                new DataContractJsonSerializer(typeof(ExportSettings)).WriteObject(w, this);
            File.WriteAllBytes(path, ms.ToArray());
        }

        public string ResolveSuffix() =>
            (FileSuffix ?? "").Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
    }
}
