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
            // Связи в ней не трогаем: это рабочая модель пользователя.
            Document doc = FindOpenDocument(path);
            bool openedHere = doc == null;
            string tempDir = Path.Combine(Path.GetTempPath(), "BatchNwcExport", Guid.NewGuid().ToString("N"));
            string warning = null;

            try
            {
                if (openedHere)
                    doc = OpenForExport(path, tempDir, out warning);

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
                if (warning != null)
                    r.Message += ". " + warning;
                return r;
            }
            finally
            {
                if (openedHere && doc != null && doc.IsValidObject)
                    doc.Close(false);
                if (openedHere)
                    DeleteTempDir(tempDir);
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
        /// Метод для открытия модели. Для моделей с рабочими наборами связи убираются до их загрузки:
        /// открыть со всеми закрытыми наборами → удалить связи → сохранить во временный файл →
        /// открыть его со всеми наборами. Открыть наборы в уже открытом документе Revit API не позволяет.
        /// </summary>
        /// <param name="path">Путь до модели</param>
        /// <param name="tempDir">Папка для временного файла без связей</param>
        /// <param name="warning">Предупреждение для лога, если часть связей удалить не удалось</param>
        /// <returns>Модель</returns>
        private Document OpenForExport(string path, string tempDir, out string warning)
        {
            warning = null;
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);

            if (!BasicFileInfo.Extract(path).IsWorkshared)
            {
                return _app.OpenDocumentFile(modelPath, new OpenOptions { Audit = false });
            }

            // Связи нужны в NWC или пользователь не просил открывать все наборы — открываем как есть.
            if (_s.ExportLinks || !_s.OpenAllWorksets)
            {
                return OpenDetached(modelPath, _s.OpenAllWorksets ? WorksetConfigurationOption.OpenAllWorksets
                                                                  : WorksetConfigurationOption.OpenLastViewed);
            }

            string tempFile = Path.Combine(tempDir, Path.GetFileName(path));
            Document closed = OpenDetached(modelPath, WorksetConfigurationOption.CloseAllWorksets);

            try
            {
                int failed = DelAllLinks(closed);
                if (failed > 0)
                    warning = $"Не удалось удалить связей: {failed}";

                Directory.CreateDirectory(tempDir);
                SaveAsTempCentral(closed, tempFile);
            }
            finally
            {
                if (closed.IsValidObject)
                    closed.Close(false);
            }

            return OpenDetached(ModelPathUtils.ConvertUserVisiblePathToModelPath(tempFile),
                                WorksetConfigurationOption.OpenAllWorksets);
        }

        /// <summary>Удаляет все связи RVT. Возвращает количество связей, которые удалить не удалось.</summary>
        public int DelAllLinks(Document doc)
        {
            // Экземпляры удаляются вместе с типом; вложенные связи уходят вместе с родительской.
            List<ElementId> linkTypeIds = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Where(l => !l.IsNestedLink)
                .Select(l => l.Id)
                .ToList();

            if (linkTypeIds.Count == 0)
                return 0;

            int failed = 0;
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Удаление связей");

                foreach (ElementId id in linkTypeIds)
                {
                    try
                    {
                        doc.Delete(id);
                    }
                    catch
                    {
                        failed++;
                    }
                }

                tx.Commit();
            }

            return failed;
        }

        private static void SaveAsTempCentral(Document doc, string tempFile)
        {
            var options = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };

            // Отсоединённую модель с сохранёнными рабочими наборами можно сохранить только как новый ФХ.
            options.SetWorksharingOptions(new WorksharingSaveAsOptions
            {
                SaveAsCentral = true,
                OpenWorksetsDefault = SimpleWorksetConfiguration.AllWorksets
            });

            doc.SaveAs(ModelPathUtils.ConvertUserVisiblePathToModelPath(tempFile), options);
        }

        private static void DeleteTempDir(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { /* временные файлы не должны ронять экспорт */ }
        }

        private Document OpenDetached(ModelPath modelPath, WorksetConfigurationOption worksets)
        {
            var opts = new OpenOptions
            {
                Audit = false,
                // Отсоединяем от ФХ: центральная модель не блокируется и не синхронизируется.
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            opts.SetOpenWorksetsConfiguration(new WorksetConfiguration(worksets));

            return _app.OpenDocumentFile(modelPath, opts);
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
