# -*- coding: utf-8 -*-
"""
Скрипт для Revit Batch Processor (RBP) — ночной экспорт NWC без участия человека.
RBP сам открывает модели из списка (в настройках RBP: Detach from central, Open all worksets)
и выполняет этот скрипт для каждой. IronPython 2.7 — без f-строк.

Запуск по расписанию: Планировщик заданий Windows →
  BatchRvt.exe --settings_file "C:\BIMTools\rbp_nwc_settings.json"
"""
import clr
import os

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (
    FilteredElementCollector, View3D, NavisworksExportOptions, NavisworksExportScope,
    NavisworksCoordinates, NavisworksParameters, OptionalFunctionalityUtils
)

import revit_script_util
from revit_script_util import Output

# ---- НАСТРОЙКИ ----
VIEW_NAMES = [u"Navisworks", u"NWC"]          # первый найденный
OUTPUT_FOLDER = r"\\server\BIM\NWC"
SUFFIX = u""
SHARED_COORDS = True
EXPORT_LINKS = False
# -------------------


def find_view(doc):
    views = [v for v in FilteredElementCollector(doc).OfClass(View3D) if not v.IsTemplate]
    for name in VIEW_NAMES:
        for v in views:
            if v.Name.lower() == name.lower():
                return v
    return None


def main():
    doc = revit_script_util.GetScriptDocument()
    rvt_path = revit_script_util.GetRevitFilePath()

    if not OptionalFunctionalityUtils.IsNavisworksExporterAvailable():
        Output(u"ОШИБКА: не установлен Navisworks Exporter")
        return

    view = find_view(doc)
    if view is None:
        Output(u"ОШИБКА: не найден вид " + u"; ".join(VIEW_NAMES) + u" в " + rvt_path)
        return

    if not os.path.isdir(OUTPUT_FOLDER):
        os.makedirs(OUTPUT_FOLDER)

    name = os.path.splitext(os.path.basename(rvt_path))[0] + SUFFIX
    target = os.path.join(OUTPUT_FOLDER, name + u".nwc")
    if os.path.exists(target):
        os.remove(target)

    opts = NavisworksExportOptions()
    opts.ExportScope = NavisworksExportScope.View
    opts.ViewId = view.Id
    opts.Coordinates = NavisworksCoordinates.Shared if SHARED_COORDS else NavisworksCoordinates.Internal
    opts.ExportLinks = EXPORT_LINKS
    opts.ConvertElementProperties = True
    opts.DivideFileIntoLevels = True
    opts.ExportElementIds = True
    opts.ExportRoomGeometry = False
    opts.FindMissingMaterials = True
    opts.Parameters = NavisworksParameters.All

    doc.Export(OUTPUT_FOLDER, name, opts)

    if os.path.exists(target):
        Output(u"OK: " + target)
    else:
        Output(u"ОШИБКА: NWC не создан для " + rvt_path)


main()
