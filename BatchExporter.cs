using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace BatchNwcExport
{
    public class FileResult
    {
        public string SourcePath;
        public bool Success;
        public string ViewName;
        public string OutputPath;
        public string Message;
        public TimeSpan Duration;
    }

    /// <summary>Ядро: открыть модель → найти вид → экспорт NWC → закрыть без сохранения.</summary>
    public class BatchExporter
    {
        private readonly RevitApp _app;
        private readonly ExportSettings _s;

        public BatchExporter(RevitApp app, ExportSettings settings)
        {
            _app = app;
            _s = settings;
        }

        public List<FileResult> Run(Action<int, int, string> progress = null)
        {
            Directory.CreateDirectory(_s.OutputFolder);
            var results = new List<FileResult>();
            var files = _s.Files.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            for (int i = 0; i < files.Count; i++)
            {
                progress?.Invoke(i + 1, files.Count, files[i]);
                var sw = Stopwatch.StartNew();
                FileResult r;
                try
                {
                    r = ExportOne(files[i]);
                }
                catch (Exception ex)
                {
                    r = new FileResult { SourcePath = files[i], Success = false, Message = ex.Message };
                }
                r.Duration = sw.Elapsed;
                results.Add(r);
            }

            WriteLog(results);
            return results;
        }

        public FileResult ExportOne(string path)
        {
            var r = new FileResult { SourcePath = path };

            if (!File.Exists(path))
            {
                r.Message = "Файл не найден";
                return r;
            }

            // Если модель уже открыта в этой сессии Revit — экспортируем из неё и не закрываем.
            Document doc = FindOpenDocument(path);
            bool openedHere = false;

            if (doc == null)
            {
                doc = OpenDetached(path);
                openedHere = true;
            }

            try
            {
                View3D view = FindView(doc);

                if (view == null)
                {
                    r.Message = $"Не найден 3D-вид: {_s.ViewNames}";
                    return r;
                }
                r.ViewName = view.Name;

                string name = Path.GetFileNameWithoutExtension(path) + _s.ResolveSuffix();
                string target = Path.Combine(_s.OutputFolder, name + ".nwc");
                if (File.Exists(target))
                    File.Delete(target);

                doc.Export(_s.OutputFolder, name, BuildOptions(view));

                if (!File.Exists(target))
                {
                    r.Message = "Экспорт завершился, но файл NWC не создан (вид пустой или всё скрыто?)";
                    return r;
                }

                r.Success = true;
                r.OutputPath = target;
                r.Message = $"OK, {new FileInfo(target).Length / 1024.0 / 1024.0:F1} МБ";
                return r;
            }
            finally
            {
                if (openedHere && doc.IsValidObject)
                    doc.Close(false);
            }
        }

        private NavisworksExportOptions BuildOptions(View3D view) => new NavisworksExportOptions
        {
            ExportScope = NavisworksExportScope.View,
            ViewId = view.Id,
            Coordinates = _s.SharedCoordinates ? NavisworksCoordinates.Shared : NavisworksCoordinates.Internal,
            ExportLinks = _s.ExportLinks,
            ConvertElementProperties = _s.ConvertProperties,
            ExportRoomGeometry = _s.ExportRoomGeometry,
            DivideFileIntoLevels = _s.DivideByLevels,
            ExportElementIds = _s.ExportElementIds,
            ExportParts = _s.ExportParts,
            FindMissingMaterials = _s.FindMissingMaterials,
            ExportUrls = false,
            Parameters = NavisworksParameters.All,
        };

        /// <summary>
        /// Метод для открытия модели, рабочие наборы сначала полностью закрыты, 
        /// потом ищем связи и смотрим в каких рабочих наборах они лежат и 
        /// потом открываем все рабочие наборы кроме найденных
        /// </summary>
        /// <param name="path">Путь до модели</param>
        /// <returns>Модель</returns>
        private Document OpenDetached(string path)
        {
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);

            if (!BasicFileInfo.Extract(path).IsWorkshared)
            {
                return _app.OpenDocumentFile(modelPath, new OpenOptions { Audit = false });
            }

            // Проход 1: всё закрыто, связи не грузятся — только узнаём их рабочие наборы.
            var linkWorksets = new HashSet<int>();
            Document probe = _app.OpenDocumentFile(modelPath, DetachedOptions(new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets)));

            try
            {
                // можно искать id рабочих наборов с помощью имени но так наверное более правильнее
                foreach (Element t in new FilteredElementCollector(probe).OfClass(typeof(RevitLinkType)))
                {
                    linkWorksets.Add(t.WorksetId.IntegerValue);
                }
            }
            finally
            {
                probe.Close(false);
            }

            // Проход 2: открываем все пользовательские наборы, кроме наборов со связями.
            IList<WorksetId> toOpen = WorksharingUtils.GetUserWorksetInfo(modelPath)
                                                      .Select(w => w.Id)
                                                      .Where(id => !linkWorksets.Contains(id.IntegerValue))
                                                      .ToList();

            var cfg = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
            cfg.Open(toOpen);

            return _app.OpenDocumentFile(modelPath, DetachedOptions(cfg));
        }

        private static OpenOptions DetachedOptions(WorksetConfiguration cfg)
        {
            var opts = new OpenOptions
            {
                Audit = false,
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            opts.SetOpenWorksetsConfiguration(cfg);

            return opts;
        }

        private Document FindOpenDocument(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (Document d in _app.Documents)
            {
                if (d.IsLinked || string.IsNullOrEmpty(d.PathName))
                    continue;
                try
                {
                    if (string.Equals(Path.GetFullPath(d.PathName), full, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
                catch { /* облачные/некорректные пути */ }
            }
            return null;
        }

        private View3D FindView(Document doc)
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .ToList();

            var names = (_s.ViewNames ?? "")
                .Split(';')
                .Select(n => n.Trim())
                .Where(n => n.Length > 0);

            foreach (var n in names)
            {
                var v = views.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                if (v != null)
                    return v;
            }
            return null;
        }

        private void WriteLog(List<FileResult> results)
        {
            try
            {
                string log = Path.Combine(_s.OutputFolder, $"nwc_export_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var lines = new List<string> { "Статус;Файл;Вид;NWC;Время, с;Сообщение" };
                lines.AddRange(results.Select(r => string.Join(";",
                    r.Success ? "OK" : "ОШИБКА",
                    r.SourcePath, r.ViewName ?? "", r.OutputPath ?? "",
                    r.Duration.TotalSeconds.ToString("F0"),
                    (r.Message ?? "").Replace(";", ",").Replace("\n", " "))));
                // UTF-8 с BOM — чтобы Excel корректно открыл кириллицу
                File.WriteAllLines(log, lines, new System.Text.UTF8Encoding(true));
            }
            catch { /* лог не должен ронять экспорт */ }
        }
    }
}
