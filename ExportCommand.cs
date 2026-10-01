using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace BatchNwcExport
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
            {
                TaskDialog.Show("Экспорт NWC",
                    "Не установлен Navisworks NWC Export Utility для этой версии Revit.\n" +
                    "Установите его (идёт с Navisworks или скачивается отдельно с сайта Autodesk).");
                return Result.Failed;
            }

            var settings = ExportSettings.Load();

            using (var form = new SettingsForm(settings))
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;
            }
            settings.Save();

            var uiApp = data.Application;
            BatchMode.IsRunning = true;
            try
            {
                var results = new BatchExporter(uiApp.Application, settings)
                    .Run((i, n, file) => uiApp.Application.WriteJournalComment($"NWC {i}/{n}: {file}", false));

                int ok = results.Count(r => r.Success);
                var td = new TaskDialog("Экспорт NWC")
                {
                    MainInstruction = $"Готово: {ok} из {results.Count}",
                    MainContent = string.Join("\n", results.Where(r => !r.Success)
                        .Select(r => $"✗ {System.IO.Path.GetFileName(r.SourcePath)} — {r.Message}")),
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть папку с NWC");
                if (td.Show() == TaskDialogResult.CommandLink1)
                    Process.Start("explorer.exe", $"\"{settings.OutputFolder}\"");

                return Result.Succeeded;
            }
            finally
            {
                BatchMode.IsRunning = false;
            }
        }
    }
}
