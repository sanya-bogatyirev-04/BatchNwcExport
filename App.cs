using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.Reflection;

namespace BatchNwcExport
{
    /// <summary>Флаг пакетного режима: пока он поднят, предупреждения и диалоги гасятся автоматически.</summary>
    internal static class BatchMode
    {
        public static bool IsRunning;
    }

    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            const string tab = "BIM Tools";
            try
            { app.CreateRibbonTab(tab); }
            catch { /* вкладка уже есть */ }

            var panel = app.CreateRibbonPanel(tab, "Экспорт");
            var btn = new PushButtonData("BatchNwcExport", "Пакетный\nэкспорт NWC",
                Assembly.GetExecutingAssembly().Location, typeof(ExportCommand).FullName)
            {
                ToolTip = "Экспорт в Navisworks (.nwc) из заданного 3D-вида для списка моделей"
            };
            panel.AddItem(btn);

            app.ControlledApplication.FailuresProcessing += OnFailuresProcessing;
            app.DialogBoxShowing += OnDialogBoxShowing;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            app.ControlledApplication.FailuresProcessing -= OnFailuresProcessing;
            app.DialogBoxShowing -= OnDialogBoxShowing;
            return Result.Succeeded;
        }

        // Удаляем предупреждения, чтобы открытие моделей не останавливалось.
        private static void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            if (!BatchMode.IsRunning)
                return;
            var fa = e.GetFailuresAccessor();
            bool hadWarnings = false;
            foreach (var msg in fa.GetFailureMessages())
            {
                if (msg.GetSeverity() == FailureSeverity.Warning)
                {
                    fa.DeleteWarning(msg);
                    hadWarnings = true;
                }
            }
            if (hadWarnings)
                e.SetProcessingResult(FailureProcessingResult.Continue);
        }

        // Автоответы на типовые диалоги при открытии моделей.
        private static void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            if (!BatchMode.IsRunning)
                return;

            switch (e.DialogId)
            {
                case "TaskDialog_Unresolved_References":
                    e.OverrideResult(1002); // «Игнорировать и продолжить открытие»
                    break;
                case "TaskDialog_Missing_Third_Party_Updaters":
                case "TaskDialog_Missing_Third_Party_Updater":
                    e.OverrideResult(1001); // «Продолжить работу с файлом»
                    break;
                default:
                    e.OverrideResult(1);    // IDOK
                    break;
            }
        }
    }
}
