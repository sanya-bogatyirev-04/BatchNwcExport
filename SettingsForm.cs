using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BatchNwcExport
{
    /// <summary>Окно настроек: список моделей, имя вида, папка вывода, опции NWC.</summary>
    public class SettingsForm : Form
    {
        private readonly ExportSettings _s;

        private readonly ListBox _files = new ListBox { SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
        private readonly TextBox _viewNames = new TextBox();
        private readonly TextBox _output = new TextBox();
        private readonly TextBox _suffix = new TextBox();
        private readonly CheckBox _shared = new CheckBox { Text = "Общие координаты (Shared)" };
        private readonly CheckBox _links = new CheckBox { Text = "Экспортировать связи Revit" };
        private readonly CheckBox _props = new CheckBox { Text = "Преобразовывать свойства элементов" };
        private readonly CheckBox _levels = new CheckBox { Text = "Разделять по уровням" };
        private readonly CheckBox _ids = new CheckBox { Text = "Экспортировать ID элементов" };
        private readonly CheckBox _rooms = new CheckBox { Text = "Геометрия помещений" };
        private readonly CheckBox _parts = new CheckBox { Text = "Экспортировать части (Parts)" };
        private readonly CheckBox _worksets = new CheckBox { Text = "Открывать все рабочие наборы" };

        // Резервные копии Revit: Model.0001.rvt
        private static readonly Regex Backup = new Regex(@"\.\d{4}\.rvt$", RegexOptions.IgnoreCase);

        public SettingsForm(ExportSettings settings)
        {
            _s = settings;
            Text = "Пакетный экспорт в Navisworks (.nwc)";
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(640, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            BuildLayout();
            LoadValues();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            root.Controls.Add(new Label { Text = "Модели Revit (.rvt):", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom });

            // Список файлов + кнопки
            var filesRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            filesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _files.Dock = DockStyle.Fill;
            filesRow.Controls.Add(_files, 0, 0);

            var fileBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            fileBtns.Controls.Add(Btn("Добавить файлы…", AddFiles));
            fileBtns.Controls.Add(Btn("Добавить папку…", AddFolder));
            fileBtns.Controls.Add(Btn("Из списка .txt…", AddFromTxt));
            fileBtns.Controls.Add(Btn("Удалить", (_, __) => { foreach (var i in _files.SelectedItems.Cast<object>().ToList()) _files.Items.Remove(i); }));
            fileBtns.Controls.Add(Btn("Очистить", (_, __) => _files.Items.Clear()));
            filesRow.Controls.Add(fileBtns, 1, 0);
            root.Controls.Add(filesRow);

            // Поля
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

            AddRow(grid, "Имя 3D-вида (через ;):", _viewNames, null);
            var browse = new Button { Text = "…", Width = 32 };
            browse.Click += (_, __) =>
            {
                using var d = new FolderBrowserDialog { SelectedPath = _output.Text };
                if (d.ShowDialog(this) == DialogResult.OK)
                    _output.Text = d.SelectedPath;
            };
            AddRow(grid, "Папка для NWC:", _output, browse);
            AddRow(grid, "Суффикс имени ({date}):", _suffix, null);
            root.Controls.Add(grid);

            // Опции
            var opts = new GroupBox { Text = "Параметры NWC", Dock = DockStyle.Top, Height = 120 };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true };
            foreach (var cb in new[] { _shared, _props, _ids, _levels, _links, _rooms, _parts, _worksets })
            {
                cb.AutoSize = true;
                cb.Margin = new Padding(6, 3, 24, 3);
                flow.Controls.Add(cb);
            }
            opts.Controls.Add(flow);
            root.Controls.Add(opts);

            // Кнопки
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 100 };
            var ok = new Button { Text = "Экспортировать", Width = 130 };
            ok.Click += OnOk;
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            root.Controls.Add(bottom);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static Button Btn(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, Width = 140, Height = 28 };
            b.Click += onClick;
            return b;
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control input, Control extra)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            input.Dock = DockStyle.Fill;
            grid.Controls.Add(input, 1, row);
            if (extra != null)
                grid.Controls.Add(extra, 2, row);
        }

        private void LoadValues()
        {
            foreach (var f in _s.Files)
                _files.Items.Add(f);
            _viewNames.Text = _s.ViewNames;
            _output.Text = _s.OutputFolder;
            _suffix.Text = _s.FileSuffix;
            _shared.Checked = _s.SharedCoordinates;
            _links.Checked = _s.ExportLinks;
            _props.Checked = _s.ConvertProperties;
            _levels.Checked = _s.DivideByLevels;
            _ids.Checked = _s.ExportElementIds;
            _rooms.Checked = _s.ExportRoomGeometry;
            _parts.Checked = _s.ExportParts;
            _worksets.Checked = _s.OpenAllWorksets;
        }

        private void AddPaths(System.Collections.Generic.IEnumerable<string> paths)
        {
            var existing = _files.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                if (!p.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) || Backup.IsMatch(p))
                    continue;
                if (existing.Add(p))
                    _files.Items.Add(p);
            }
        }

        private void AddFiles(object _, EventArgs __)
        {
            using var d = new OpenFileDialog { Filter = "Revit (*.rvt)|*.rvt", Multiselect = true };
            if (d.ShowDialog(this) == DialogResult.OK)
                AddPaths(d.FileNames);
        }

        private void AddFolder(object _, EventArgs __)
        {
            using var d = new FolderBrowserDialog { Description = "Папка с моделями (включая подпапки)" };
            if (d.ShowDialog(this) != DialogResult.OK)
                return;
            AddPaths(Directory.EnumerateFiles(d.SelectedPath, "*.rvt", SearchOption.AllDirectories));
        }

        private void AddFromTxt(object _, EventArgs __)
        {
            using var d = new OpenFileDialog { Filter = "Список путей (*.txt)|*.txt" };
            if (d.ShowDialog(this) != DialogResult.OK)
                return;
            AddPaths(File.ReadAllLines(d.FileName).Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0));
        }

        private void OnOk(object _, EventArgs __)
        {
            if (_files.Items.Count == 0)
            { Warn("Добавьте хотя бы одну модель."); return; }
            if (string.IsNullOrWhiteSpace(_viewNames.Text))
            { Warn("Укажите имя 3D-вида."); return; }
            if (string.IsNullOrWhiteSpace(_output.Text))
            { Warn("Укажите папку для NWC."); return; }

            _s.Files = _files.Items.Cast<string>().ToList();
            _s.ViewNames = _viewNames.Text.Trim();
            _s.OutputFolder = _output.Text.Trim();
            _s.FileSuffix = _suffix.Text.Trim();
            _s.SharedCoordinates = _shared.Checked;
            _s.ExportLinks = _links.Checked;
            _s.ConvertProperties = _props.Checked;
            _s.DivideByLevels = _levels.Checked;
            _s.ExportElementIds = _ids.Checked;
            _s.ExportRoomGeometry = _rooms.Checked;
            _s.ExportParts = _parts.Checked;
            _s.OpenAllWorksets = _worksets.Checked;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string text) =>
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
