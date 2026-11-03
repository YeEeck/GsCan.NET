using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace CanBench
{
    public sealed class MainForm : Form
    {
        private readonly CanTransport _bus = new CanTransport();
        private readonly BindingList<CanItem> _items = new BindingList<CanItem>();
        private readonly Timer _timer = new Timer();
        private readonly Dictionary<int, DateTime> _nextDue = new Dictionary<int, DateTime>();
        private readonly List<BusFrame> _trace = new List<BusFrame>();
        private bool _tracePaused;
        private string? _currentFile;

        // 控件
        private ComboBox _cboDevice = null!;
        private Button _btnRefresh = null!, _btnOpen = null!, _btnClose = null!;
        private NumericUpDown _numBitrate0 = null!, _numBitrate1 = null!;
        private CheckBox _chkListen0 = null!, _chkListen1 = null!;
        private Button _btnStart0 = null!, _btnStop0 = null!, _btnStart1 = null!, _btnStop1 = null!;
        private Label _lblStatus = null!;

        private DataGridView _grid = null!;
        private ListBox _traceList = null!;
        private DataGridView _latestView = null!;
        private TabControl _tabs = null!;
        private TextBox _txtFilter = null!;
        private CheckBox _chkPauseTrace = null!;
        private Label _lblStats = null!;

        private readonly LatestAggregator _latest = new LatestAggregator();
        private IdFilter _filter = new IdFilter();
        private List<LatestEntry> _latestRows = new List<LatestEntry>();
        private readonly BindingList<LatestEntry> _latestRowsBinding = new BindingList<LatestEntry>();
        private readonly HashSet<LatestEntry> _dirtyEntries = new HashSet<LatestEntry>();
        private DateTime _lastLatestFlush = DateTime.MinValue;
        private bool _autoScrollTrace = true;
        private int _frameCount;
        private int _droppedCount;


        public MainForm()
        {
            Text = "CanBench — CAN 命令台（导入/导出 · 备注 · gs_usb 直发）";
            Width = 1360;
            Height = 860;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);

            BuildUi();

            _grid.DataSource = _items;
            _items.ListChanged += (_, __) => UpdateStats();

            _timer.Interval = 20;
            _timer.Tick += OnTimer;
            _timer.Start();

            RefreshDevices();
            UpdateStats();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            Controls.Add(root);

            // ---- 设备与通道
            var devPanel = new GroupBox { Text = "设备 / 通道", Dock = DockStyle.Fill, Padding = new Padding(8) };
            root.Controls.Add(devPanel, 0, 0);

            var dp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3 };
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));    // 行标题
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));   // 波特率
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));    // 只听
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));   // 启动/停止
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));    // 设备下拉
            dp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));   // 设备按钮
            dp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            dp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            dp.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            devPanel.Controls.Add(dp);

            // 第 0 行：设备下拉 + 刷新/打开/关闭
            var lblDev = new Label { Text = "设备", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            dp.Controls.Add(lblDev, 0, 0);

            _cboDevice = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 6, 3),
            };
            dp.Controls.Add(_cboDevice, 1, 0);
            dp.SetColumnSpan(_cboDevice, 4);

            var devBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _btnRefresh = new Button { Text = "刷新", Width = 62, Height = 24 };
            _btnOpen = new Button { Text = "打开", Width = 62, Height = 24 };
            _btnClose = new Button { Text = "关闭", Width = 62, Height = 24, Enabled = false };
            devBtns.Controls.AddRange(new Control[] { _btnRefresh, _btnOpen, _btnClose });
            dp.Controls.Add(devBtns, 5, 0);

            // 第 1 行：CH0
            var lblCh0 = new Label { Text = "CH0", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            dp.Controls.Add(lblCh0, 0, 1);
            _numBitrate0 = MakeBitrateBox();
            dp.Controls.Add(_numBitrate0, 1, 1);
            _chkListen0 = new CheckBox { Text = "只听", Dock = DockStyle.Fill, Margin = new Padding(0) };
            dp.Controls.Add(_chkListen0, 2, 1);
            var flow0 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _btnStart0 = new Button { Text = "启动 CH0", Width = 90, Height = 24 };
            _btnStop0 = new Button { Text = "停止", Width = 62, Height = 24, Enabled = false };
            flow0.Controls.AddRange(new Control[] { _btnStart0, _btnStop0 });
            dp.Controls.Add(flow0, 3, 1);
            dp.SetColumnSpan(flow0, 2);

            // 第 2 行：CH1
            var lblCh1 = new Label { Text = "CH1", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            dp.Controls.Add(lblCh1, 0, 2);
            _numBitrate1 = MakeBitrateBox();
            dp.Controls.Add(_numBitrate1, 1, 2);
            _chkListen1 = new CheckBox { Text = "只听", Dock = DockStyle.Fill, Checked = true, Margin = new Padding(0) };
            dp.Controls.Add(_chkListen1, 2, 2);
            var flow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _btnStart1 = new Button { Text = "启动 CH1", Width = 90, Height = 24 };
            _btnStop1 = new Button { Text = "停止", Width = 62, Height = 24, Enabled = false };
            flow1.Controls.AddRange(new Control[] { _btnStart1, _btnStop1 });
            dp.Controls.Add(flow1, 3, 2);
            dp.SetColumnSpan(flow1, 2);

            // ---- 命令表工具条
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2) };
            root.Controls.Add(bar, 0, 1);

            bar.Controls.Add(MakeBtn("新建", (_, __) => NewFile()));
            bar.Controls.Add(MakeBtn("打开", (_, __) => OpenFile()));
            bar.Controls.Add(MakeBtn("保存", (_, __) => SaveFile(false)));
            bar.Controls.Add(MakeBtn("另存为", (_, __) => SaveFile(true)));
            bar.Controls.Add(new Label { Text = "|", Width = 12, TextAlign = ContentAlignment.MiddleCenter });
            bar.Controls.Add(MakeBtn("导入 CSV", (_, __) => ImportCsv()));
            bar.Controls.Add(MakeBtn("导出 CSV", (_, __) => ExportCsv()));
            bar.Controls.Add(new Label { Text = "|", Width = 12, TextAlign = ContentAlignment.MiddleCenter });
            bar.Controls.Add(MakeBtn("添加行", (_, __) => AddRow()));
            bar.Controls.Add(MakeBtn("复制行", (_, __) => DuplicateRow()));
            bar.Controls.Add(MakeBtn("删除行", (_, __) => RemoveRow()));
            bar.Controls.Add(MakeBtn("上移", (_, __) => MoveRow(-1)));
            bar.Controls.Add(MakeBtn("下移", (_, __) => MoveRow(1)));
            bar.Controls.Add(new Label { Text = "|", Width = 12, TextAlign = ContentAlignment.MiddleCenter });
            bar.Controls.Add(MakeBtn("发送选中", (_, __) => SendSelected()));
            bar.Controls.Add(MakeBtn("全部停止", (_, __) => StopAllCyclic()));

            // ---- 命令表
            var gridBox = new GroupBox { Text = "命令表（备注列可写作用；勾选「周期」后按周期发送）", Dock = DockStyle.Fill, Padding = new Padding(6) };
            root.Controls.Add(gridBox, 0, 2);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersWidth = 30,
                BackgroundColor = SystemColors.Window,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 26,
                // 默认 EditOnKeystrokeOrF2 下双击不进编辑，必须显式改
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            };
            gridBox.Controls.Add(_grid);

            _grid.Columns.Add(Col("Name", "备注 / 作用", 26, readOnly: false));
            _grid.Columns.Add(Col("Channel", "CH", 4, readOnly: false));
            _grid.Columns.Add(Col("IdHex", "ID", 11, readOnly: false));
            _grid.Columns.Add(BoolCol("Extended", "扩展", 5));
            _grid.Columns.Add(BoolCol("Remote", "RTR", 5));
            _grid.Columns.Add(Col("Length", "DLC", 4, readOnly: true));
            _grid.Columns.Add(Col("DataHex", "数据（十六进制）", 22, readOnly: false));
            _grid.Columns.Add(Col("PeriodMs", "周期ms", 6, readOnly: false));
            _grid.Columns.Add(BoolCol("Enabled", "启用", 5));

            var sendCol = new DataGridViewButtonColumn
            {
                Name = "Send",
                HeaderText = "发送",
                Text = "发送",
                UseColumnTextForButtonValue = true,
                FillWeight = 6,
                MinimumWidth = 48,
                FlatStyle = FlatStyle.System,
                ReadOnly = true,
            };
            _grid.Columns.Add(sendCol);

            _grid.CellContentClick += OnGridButton;
            _grid.CellValidating += OnCellValidating;
            _grid.DataError += (_, e) => { e.ThrowException = false; };

            // ---- Trace / Latest
            var traceBox = new GroupBox { Text = "帧监视", Dock = DockStyle.Fill, Padding = new Padding(6) };
            root.Controls.Add(traceBox, 0, 3);

            var traceTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            _chkPauseTrace = new CheckBox { Text = "暂停", Width = 62, Padding = new Padding(0, 6, 0, 0) };
            _chkPauseTrace.CheckedChanged += (_, __) => _tracePaused = _chkPauseTrace.Checked;
            traceTop.Controls.Add(_chkPauseTrace);
            traceTop.Controls.Add(MakeBtn("清空", (_, __) => ClearTrace()));
            traceTop.Controls.Add(MakeBtn("保存", (_, __) => SaveTrace()));

            traceTop.Controls.Add(new Label { Text = "  ID 过滤", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
            _txtFilter = new TextBox { Width = 190, Margin = new Padding(2, 4, 2, 2) };
            _txtFilter.TextChanged += (_, __) => ApplyFilter();
            traceTop.Controls.Add(_txtFilter);

            traceTop.Controls.Add(new Label
            {
                Text = "（0x100 / 0x100-0x1FF / !0x01100001，空=全部）",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(4, 8, 0, 0),
            });

            _lblStats = new Label { AutoSize = true, Padding = new Padding(12, 10, 0, 0) };
            traceTop.Controls.Add(_lblStats);

            // Latest：按 ID 聚合，心跳帧也不会刷屏
            _latestView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeight = 24,
                Font = new Font("Consolas", 9f),
            };
            _latestView.Columns.Add(TextCol("Channel", "CH", 4));
            _latestView.Columns.Add(TextCol("Direction", "方向", 9));
            _latestView.Columns.Add(TextCol("IdHex", "ID", 12));
            _latestView.Columns.Add(TextCol("Flags", "标志", 5));
            _latestView.Columns.Add(TextCol("Length", "长度", 5));
            _latestView.Columns.Add(TextCol("DataHex", "数据", 26));
            _latestView.Columns.Add(TextCol("Count", "计数", 8));
            _latestView.Columns.Add(TextCol("PeriodText", "周期", 9));

            // 持久绑定：靠条目的 PropertyChanged 只重画变化行，不整表重绑
            _latestView.DataSource = _latestRowsBinding;

            _traceList = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                HorizontalScrollbar = true,
                IntegralHeight = false,
            };

            _tabs = new TabControl { Dock = DockStyle.Fill };
            var pageLatest = new TabPage("Latest（按 ID 汇总）");
            var pageTrace = new TabPage("Trace（按到达顺序）");
            pageLatest.Controls.Add(_latestView);
            pageTrace.Controls.Add(_traceList);
            _tabs.TabPages.Add(pageLatest);
            _tabs.TabPages.Add(pageTrace);
            _tabs.SelectedIndex = 0;

            // 必须先加 Fill 再加 Top，否则 Top 会被填满区域的控件盖住
            traceBox.Controls.Add(_tabs);
            traceBox.Controls.Add(traceTop);

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "就绪",
            };
            Controls.Add(_lblStatus);
            _lblStatus.BringToFront();

            // 事件
            _btnRefresh.Click += (_, __) => RefreshDevices();
            _btnOpen.Click += (_, __) => OpenDevice();
            _btnClose.Click += (_, __) => CloseDevice();
            _btnStart0.Click += (_, __) => StartChannel(0, _numBitrate0, _chkListen0, _btnStart0, _btnStop0);
            _btnStart1.Click += (_, __) => StartChannel(1, _numBitrate1, _chkListen1, _btnStart1, _btnStop1);
            _btnStop0.Click += (_, __) => StopChannel(0, _btnStart0, _btnStop0);
            _btnStop1.Click += (_, __) => StopChannel(1, _btnStart1, _btnStop1);
        }

        private Button MakeBtn(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = false, Width = 78, Height = 28 };
            b.Click += onClick;
            return b;
        }

        private static NumericUpDown MakeBitrateBox() => new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1000,
            Maximum = 1000000,
            Increment = 100000,
            Value = 500000,
            ThousandsSeparator = true,
        };

        private static DataGridViewTextBoxColumn Col(string prop, string header, int width, bool readOnly)
            => new DataGridViewTextBoxColumn
            {
                DataPropertyName = prop,
                HeaderText = header,
                FillWeight = width,
                MinimumWidth = Math.Min(width, 60),
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };

        /// <summary>布尔列用复选框显示，可直接勾。</summary>
        private static DataGridViewCheckBoxColumn BoolCol(string prop, string header, int width)
            => new DataGridViewCheckBoxColumn
            {
                DataPropertyName = prop,
                HeaderText = header,
                FillWeight = width,
                MinimumWidth = Math.Min(width, 40),
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };

        /// <summary>Latest 表用的只读文本列（FillWeight 是比例，不是像素）。</summary>
        private static DataGridViewTextBoxColumn TextCol(string prop, string header, int weight)
            => new DataGridViewTextBoxColumn
            {
                DataPropertyName = prop,
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = 40,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };

        // ------------------------------------------------------------------ 设备

        private void RefreshDevices()
        {
            _cboDevice.Items.Clear();
            try
            {
                foreach (var d in CanTransport.Scan()) _cboDevice.Items.Add(d);
                if (_cboDevice.Items.Count > 0) _cboDevice.SelectedIndex = 0;
                SetStatus($"找到 {_cboDevice.Items.Count} 个 gs_usb 设备");
            }
            catch (Exception ex)
            {
                SetStatus("扫描失败：" + ex.Message);
            }
        }

        private void OpenDevice()
        {
            if (_cboDevice.SelectedItem is not CanTransport.DeviceEntry d)
            {
                SetStatus("没有选中设备");
                return;
            }
            try
            {
                var msg = _bus.Open(d.Index);
                _btnOpen.Enabled = false;
                _btnClose.Enabled = true;
                SetStatus(msg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(ex.Message);
            }
        }

        private void CloseDevice()
        {
            StopAllCyclic();
            _bus.Close();
            _btnOpen.Enabled = true;
            _btnClose.Enabled = false;
            _btnStart0.Enabled = _btnStart1.Enabled = true;
            _btnStop0.Enabled = _btnStop1.Enabled = false;
            SetStatus("已关闭设备");
        }

        private void StartChannel(int ch, NumericUpDown rate, CheckBox listen, Button start, Button stop)
        {
            try
            {
                _bus.StartChannel(ch, (uint)rate.Value, listen.Checked);
                start.Enabled = false;
                stop.Enabled = true;
                SetStatus($"CH{ch} 已启动 @ {rate.Value:N0} bit/s{(listen.Checked ? "（只听，不会发帧）" : "")}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(ex.Message);
            }
        }

        private void StopChannel(int ch, Button start, Button stop)
        {
            try
            {
                _bus.StopChannel(ch);
                start.Enabled = true;
                stop.Enabled = false;
                SetStatus($"CH{ch} 已停止");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        // ------------------------------------------------------------------ 文件

        private void NewFile()
        {
            _items.Clear();
            _currentFile = null;
            AddRow();
            SetStatus("已新建");
        }

        private void OpenFile()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "CAN 命令表 (*.list;*.csv)|*.list;*.csv|命令帧表 (*.list)|*.list|CSV (*.csv)|*.csv|所有文件|*.*",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                LoadFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void LoadFile(string path)
        {
            List<CanItem> loaded;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                loaded = CsvFile.Load(path);
            else
                loaded = ListFile.Load(path);

            _items.Clear();
            foreach (var it in loaded) _items.Add(it);
            if (_items.Count == 0) AddRow();
            _currentFile = path;
            UpdateStats();
            SetStatus($"已载入 {_items.Count} 条：{Path.GetFileName(path)}");
        }

        private void SaveFile(bool saveAs)
        {
            if (!saveAs && _currentFile != null)
            {
                WriteFile(_currentFile);
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Filter = "命令帧表 (*.list)|*.list|CSV (*.csv)|*.csv",
                FileName = _currentFile != null ? Path.GetFileName(_currentFile) : "commands.list",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            WriteFile(dlg.FileName);
        }

        private void WriteFile(string path)
        {
            try
            {
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    CsvFile.Save(path, _items);
                else
                    ListFile.Save(path, _items);
                _currentFile = path;
                SetStatus($"已保存：{Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportCsv()
        {
            using var dlg = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv|所有文件|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var loaded = CsvFile.Load(dlg.FileName);
                _items.Clear();
                foreach (var it in loaded) _items.Add(it);
                if (_items.Count == 0) AddRow();
                UpdateStats();
                SetStatus($"已导入 CSV {_items.Count} 条");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "导入 CSV 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportCsv()
        {
            using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "commands.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                CsvFile.Save(dlg.FileName, _items);
                SetStatus($"已导出 CSV：{Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveTrace()
        {
            if (_trace.Count == 0) { SetStatus("Trace 为空"); return; }
            using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "trace.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                CsvFile.SaveTrace(dlg.FileName, _trace);
                SetStatus($"已保存 Trace：{_trace.Count} 行");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------ 行操作

        private void AddRow()
        {
            var last = _items.Count > 0 ? _items[_items.Count - 1] : null;
            _items.Add(new CanItem
            {
                Name = "新命令",
                Id = last?.Id ?? 0x02000002u,
                Extended = true,
                Data = Array.Empty<byte>(),
                Channel = last?.Channel ?? 0,
            });
            UpdateStats();
        }

        private void DuplicateRow()
        {
            var idx = SelectedIndex();
            if (idx < 0) return;
            _items.Insert(idx + 1, _items[idx].Clone());
            UpdateStats();
        }

        private void RemoveRow()
        {
            var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Index).OrderByDescending(i => i).ToList();
            if (selected.Count == 0) return;
            foreach (var i in selected)
                if (i >= 0 && i < _items.Count) _items.RemoveAt(i);
            if (_items.Count == 0) AddRow();
            UpdateStats();
        }

        private void MoveRow(int delta)
        {
            var idx = SelectedIndex();
            if (idx < 0) return;
            int target = idx + delta;
            if (target < 0 || target >= _items.Count) return;
            var item = _items[idx];
            _items.RemoveAt(idx);
            _items.Insert(target, item);
            _grid.ClearSelection();
            _grid.Rows[target].Selected = true;
        }

        private int SelectedIndex()
        {
            if (_grid.CurrentRow == null) return -1;
            return _grid.CurrentRow.Index;
        }

        // ------------------------------------------------------------------ 编辑校验

        private void OnCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _items.Count) return;
            var item = _items[e.RowIndex];
            var col = _grid.Columns[e.ColumnIndex].DataPropertyName;
            var text = e.FormattedValue?.ToString() ?? "";

            try
            {
                switch (col)
                {
                    case "IdHex":
                        item.IdHex = text;                  // setter 内部做解析与范围检查
                        break;
                    case "DataHex":
                        item.Data = DataCodec.ParseHex(text);
                        break;
                    case "PeriodMs":
                        item.PeriodMs = int.TryParse(text, out var p) ? Math.Max(0, p) : 0;
                        break;
                    case "Channel":
                        item.Channel = int.TryParse(text, out var c) ? (c == 1 ? 1 : 0) : 0;
                        break;
                    case "Extended":
                        item.Extended = ParseBool(text, true);
                        break;
                    case "Remote":
                        item.Remote = ParseBool(text, false);
                        break;
                    case "Enabled":
                        item.Enabled = ParseBool(text, false);
                        break;
                }
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                SetStatus($"{col} 格式错误：{ex.Message}");
            }
        }

        private static bool ParseBool(string text, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            text = text.Trim();
            if (text is "1" or "是" or "True" or "true" or "Y" or "y") return true;
            if (text is "0" or "否" or "False" or "false" or "N" or "n") return false;
            return fallback;
        }

        public static uint ParseId(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return 0;
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var s = hex ? text.Substring(2) : text;
            uint v;
            if (hex)
            {
                if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                    throw new FormatException("ID 不是合法的十六进制数");
            }
            else
            {
                if (!uint.TryParse(s, out v))
                    throw new FormatException("ID 不是合法的数字（十六进制请以 0x 开头）");
            }
            if (v > 0x1FFFFFFF) throw new FormatException("扩展帧 ID 不能超过 0x1FFFFFFF");
            return v;
        }

        // ------------------------------------------------------------------ 发送

        private void OnGridButton(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Send") return;
            SendRow(e.RowIndex);
        }

        private void SendSelected()
        {
            var idx = SelectedIndex();
            if (idx >= 0) SendRow(idx);
        }

        private void SendRow(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            var item = _items[index];
            try
            {
                _bus.Send(item);
                SetStatus($"已发送：{item.Name}  {item.IdHex}  [{item.DataHex}]");
            }
            catch (Exception ex)
            {
                SetStatus("发送失败：" + ex.Message);
            }
        }

        private void StopAllCyclic()
        {
            _nextDue.Clear();
            foreach (var it in _items) it.Enabled = false;
            _grid.Refresh();
        }

        private void OnTimer(object? sender, EventArgs e)
        {
            PumpTrace();

            // 周期发送
            if (!_bus.IsOpen) return;
            var now = DateTime.Now;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (!it.Enabled || it.PeriodMs <= 0) { _nextDue.Remove(i); continue; }

                if (!_nextDue.TryGetValue(i, out var due) || due <= now)
                {
                    try { _bus.Send(it); }
                    catch (Exception ex)
                    {
                        it.Enabled = false;
                        SetStatus($"周期发送已停止（{it.Name}）：{ex.Message}");
                        continue;
                    }
                    _nextDue[i] = now.AddMilliseconds(it.PeriodMs);
                }
            }
        }

        private void PumpTrace()
        {
            var incoming = _bus.Drain();
            if (incoming.Count == 0) { FlushLatestIfDue(); return; }

            // 过滤后分别喂给 Trace 和 Latest
            var passed = new List<BusFrame>(incoming.Count);
            var touched = new HashSet<LatestEntry>();
            foreach (var f in incoming)
            {
                if (!_filter.Match(f.Id)) continue;
                passed.Add(f);
                _trace.Add(f);

                var entry = _latest.Add(f, out bool isNew);
                if (isNew) InsertLatestRow(entry);   // 只在出现新 ID 时动表格结构
                else touched.Add(entry);             // 已有 ID 只标记，稍后单行重画
            }
            _frameCount += incoming.Count;
            _droppedCount += incoming.Count - passed.Count;

            foreach (var e in touched) _dirtyEntries.Add(e);

            if (_trace.Count > 20000)
            {
                int drop = _trace.Count - 15000;
                _trace.RemoveRange(0, drop);
                if (!_tracePaused) RebuildTraceList();
                else UpdateStats();
                FlushLatestIfDue();
                return;
            }

            if (_tracePaused) { UpdateStats(); FlushLatestIfDue(); return; }

            if (passed.Count > 0)
            {
                _traceList.BeginUpdate();
                foreach (var f in passed) _traceList.Items.Add(FormatTrace(f));
                while (_traceList.Items.Count > 2000) _traceList.Items.RemoveAt(0);
                if (_autoScrollTrace) _traceList.TopIndex = Math.Max(0, _traceList.Items.Count - 1);
                _traceList.EndUpdate();
            }
            UpdateStats();
            FlushLatestIfDue();
        }

        /// <summary>新 ID：按排序位置插一行，不动其它行。</summary>
        private void InsertLatestRow(LatestEntry entry)
        {
            int lo = 0, hi = _latestRows.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (LatestAggregator.Compare(_latestRows[mid], entry) < 0) lo = mid + 1;
                else hi = mid;
            }
            _latestRows.Insert(lo, entry);
            _latestRowsBinding.Insert(lo, entry);
        }

        /// <summary>
        /// 只重画这一轮真正变化的行（靠 PropertyChanged），不做整表重绑。
        /// 每 250ms 触发一次；暂停时完全不刷。
        /// </summary>
        private void FlushLatestIfDue()
        {
            if (_dirtyEntries.Count == 0) return;
            if (_tracePaused) return;
            if ((DateTime.Now - _lastLatestFlush).TotalMilliseconds < 250) return;

            _lastLatestFlush = DateTime.Now;

            _latestView.SuspendLayout();
            foreach (var e in _dirtyEntries) e.NotifyChanged();   // 逐条目通知 -> 只重画对应行
            _latestView.ResumeLayout();
            _dirtyEntries.Clear();
        }

        /// <summary>整表重建（仅用于过滤条件变更、清空这类真的全变了的场合）。</summary>
        private void RefreshLatestView()
        {
            var rows = _latest.Snapshot();
            rows.Sort(LatestAggregator.Compare);

            int top = _latestView.FirstDisplayedScrollingRowIndex;
            string? selKey = _latestView.CurrentRow?.DataBoundItem is LatestEntry sel ? sel.Key : null;

            _latestRows = rows;
            _latestRowsBinding.RaiseListChangedEvents = false;
            _latestRowsBinding.Clear();
            foreach (var r in rows) _latestRowsBinding.Add(r);
            _latestRowsBinding.RaiseListChangedEvents = true;
            _latestRowsBinding.ResetBindings();

            if (selKey != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Key == selKey)
                    {
                        _latestView.ClearSelection();
                        _latestView.Rows[i].Selected = true;
                        break;
                    }
                }
            }
            if (top >= 0 && top < _latestView.Rows.Count)
            {
                try { _latestView.FirstDisplayedScrollingRowIndex = top; } catch { }
            }
        }

        private void ApplyFilter()
        {
            try
            {
                _filter = IdFilter.Parse(_txtFilter.Text);
                _txtFilter.BackColor = SystemColors.Window;
                SetStatus(_filter.IsEmpty ? "过滤已清除" : $"过滤生效：{_txtFilter.Text.Trim()}");
            }
            catch (Exception ex)
            {
                _txtFilter.BackColor = Color.MistyRose;
                SetStatus("过滤条件有误：" + ex.Message);
                return;
            }

            // 过滤条件变了，整表都要重算
            _latest.Clear();
            _trace.Clear();
            _traceList.Items.Clear();
            _dirtyEntries.Clear();

            _droppedCount = 0;
            UpdateStats();
            RefreshLatestView();
        }

        private void ClearTrace()
        {
            _trace.Clear();
            _latest.Clear();
            _traceList.Items.Clear();
            _dirtyEntries.Clear();
            _latestRows.Clear();
            _latestRowsBinding.Clear();
            _frameCount = 0;
            _droppedCount = 0;
            UpdateStats();
        }

        private void RebuildTraceList()
        {
            _traceList.BeginUpdate();
            _traceList.Items.Clear();
            foreach (var f in _trace.TakeLast(2000))
            {
                if (_filter.Match(f.Id)) _traceList.Items.Add(FormatTrace(f));
            }
            _traceList.EndUpdate();
            UpdateStats();
        }

        private static string FormatTrace(BusFrame f)
        {
            var when = f.At.ToString("HH:mm:ss.fff");
            if (f.Error)
                return $"{when}  CH{f.Channel}  ← 错误!  {f.ErrorText}";

            var dir = f.Echo ? "发送 →" : "← 接收";
            var flags = f.Extended ? "EXT" : "STD";
            return $"{when}  CH{f.Channel}  {dir}  {f.IdHex}  {flags}  DLC={f.Data.Length}  {f.DataHex}";
        }

        private void UpdateStats()
        {
            int rx = 0, echo = 0, err = 0, cyclic = 0;
            foreach (var f in _trace)
            {
                if (f.Error) err++;
                else if (f.Echo) echo++;
                else rx++;
            }
            foreach (var it in _items) if (it.Enabled && it.PeriodMs > 0) cyclic++;

            var text = $"命令 {_items.Count} 条 · 周期 {cyclic} 条   |   Rx {rx} · Echo {echo} · Error {err} · ID {_latest.Count}";
            if (_droppedCount > 0) text += $"   |   已过滤 {_droppedCount}";
            _lblStats.Text = text;
        }

        private void SetStatus(string text) => _lblStatus.Text = text;

        /// <summary>--auto：自动打开设备并启动两路通道，供自检/演示用。</summary>
        public void AutoStart()
        {
            try
            {
                if (_cboDevice.Items.Count == 0) { SetStatus("没有可用设备"); return; }
                _cboDevice.SelectedIndex = 0;
                OpenDevice();
                StartChannel(0, _numBitrate0, _chkListen0, _btnStart0, _btnStop0);
                StartChannel(1, _numBitrate1, _chkListen1, _btnStart1, _btnStop1);
                _tabs.SelectedIndex = 0;      // 默认停在 Latest

                // 自检用：没载入命令时补一条查询，让界面有东西可看
                if (_items.Count == 0)
                {
                    _items.Add(new CanItem { Name = "查询版本", Id = 0x02000002u, Extended = true, Channel = 0 });
                }
                SendRow(0);
            }
            catch (Exception ex)
            {
                SetStatus("自动启动失败：" + ex.Message);
            }
        }

        /// <summary>自检用：返回当前 Latest 表的文字快照。</summary>
        public string LatestDump()
        {
            var sb = new StringBuilder();
            foreach (var r in _latestRows)
                sb.AppendLine($"CH{r.Channel} {r.Kind,-5} {r.IdHex} {r.Flags} DLC={r.Length} " +
                              $"[{r.DataHex}] 计数={r.Count} 周期={r.PeriodText}");
            return sb.ToString();
        }

        public int LatestRowCount => _latestRows.Count;
        public int TotalFrames => _frameCount;
        public int FilteredOut => _droppedCount;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            StopAllCyclic();
            _bus.Dispose();
            base.OnFormClosing(e);
        }
    }
}
