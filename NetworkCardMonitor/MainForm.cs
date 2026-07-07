using System.Net.NetworkInformation;
using System.Diagnostics;
using NetworkCardMonitor.Models;
using NetworkCardMonitor.Services;

namespace NetworkCardMonitor;

internal sealed class MainForm : Form
{
    private readonly ListView _adapterList = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly CheckBox _startupCheckBox = new();
    private readonly Button _minimizeToTrayButton = new();
    private readonly Button _openConnectionsButton = new();
    private readonly Button _refreshButton = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2_000 };
    private readonly System.Windows.Forms.Timer _networkChangeTimer = new() { Interval = 750 };
    private readonly System.Windows.Forms.Timer _idleMonitorTimer = new() { Interval = 30_000 };
    private readonly ImageList _statusImages = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly SpeedOverlayForm _speedOverlay;
    private static readonly TimeSpan IdlePauseAfter = TimeSpan.FromMinutes(5);
    private const int ActiveIdleCheckInterval = 30_000;
    private const int PausedIdleCheckInterval = 5_000;
    private IReadOnlyList<NetworkAdapterInfo> _adapters = Array.Empty<NetworkAdapterInfo>();
    private int _sortColumn = -1;
    private bool _sortAscending = true;
    private bool _refreshing;
    private bool _updatingStartupCheckBox;
    private bool _isInTray;
    private bool _refreshPausedForIdle;
    private string _lastNotifyIconText = string.Empty;
    private readonly bool _startInTray;

    public MainForm(bool startInTray = false)
    {
        _startInTray = startInTray;
        Text = "网卡监视器 wifix";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 400);
        Size = new Size(900, 500);
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(248, 249, 251);
        Icon = SystemIcons.Application;

        if (_startInTray)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        _speedOverlay = new SpeedOverlayForm(RestoreFromTray);
        ConfigureTray();
        BuildInterface();
        RegisterEvents();

        try
        {
            StartupService.EnsureEnabledOnFirstRun();
            UpdateStartupCheckBox();
        }
        catch (Exception exception)
        {
            _startupCheckBox.Checked = false;
            _startupCheckBox.Text = "开机自动启动（设置失败）";
            _startupCheckBox.ForeColor = Color.Firebrick;
            _updatedLabel.Text = exception.Message;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshAdapters();
        if (!_refreshPausedForIdle)
        {
            _refreshTimer.Start();
        }

        _idleMonitorTimer.Start();

        if (_startInTray)
        {
            BeginInvoke(new Action(() =>
            {
                Opacity = 1;
                MinimizeToTray();
            }));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _networkChangeTimer.Dispose();
            _idleMonitorTimer.Dispose();
            _statusImages.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _speedOverlay.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = Color.White,
            Padding = new Padding(16, 10, 16, 8)
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "网络连接",
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 31, 36),
            Location = new Point(16, 10)
        };

        _summaryLabel.AutoSize = true;
        _summaryLabel.Text = "正在读取网卡状态…";
        _summaryLabel.ForeColor = Color.FromArgb(92, 99, 112);
        _summaryLabel.Location = new Point(18, 45);

        _refreshButton.Text = "立即刷新";
        _refreshButton.AutoSize = true;
        _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refreshButton.Location = new Point(760, 12);
        _refreshButton.Padding = new Padding(6, 1, 6, 1);
        _refreshButton.FlatStyle = FlatStyle.System;

        _openConnectionsButton.Text = "打开网络连接";
        _openConnectionsButton.AutoSize = true;
        _openConnectionsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _openConnectionsButton.Location = new Point(650, 12);
        _openConnectionsButton.Padding = new Padding(6, 1, 6, 1);
        _openConnectionsButton.FlatStyle = FlatStyle.System;

        _minimizeToTrayButton.Text = "最小化到托盘";
        _minimizeToTrayButton.AutoSize = true;
        _minimizeToTrayButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _minimizeToTrayButton.Location = new Point(530, 12);
        _minimizeToTrayButton.Padding = new Padding(6, 1, 6, 1);
        _minimizeToTrayButton.FlatStyle = FlatStyle.System;

        _startupCheckBox.Text = "开机自动启动";
        _startupCheckBox.AutoSize = true;
        _startupCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _startupCheckBox.Location = new Point(760, 52);

        header.Controls.Add(title);
        header.Controls.Add(_summaryLabel);
        header.Controls.Add(_minimizeToTrayButton);
        header.Controls.Add(_openConnectionsButton);
        header.Controls.Add(_refreshButton);
        header.Controls.Add(_startupCheckBox);
        header.Resize += (_, _) =>
        {
            _refreshButton.Left = header.ClientSize.Width - _refreshButton.Width - 16;
            _minimizeToTrayButton.Left = _refreshButton.Left - _minimizeToTrayButton.Width - 8;
            _openConnectionsButton.Left = _minimizeToTrayButton.Left - _openConnectionsButton.Width - 8;
            _startupCheckBox.Left = header.ClientSize.Width - _startupCheckBox.Width - 18;
        };

        _statusImages.ColorDepth = ColorDepth.Depth32Bit;
        _statusImages.ImageSize = new Size(16, 16);
        _statusImages.Images.Add("connected", SystemIcons.Information.ToBitmap());
        _statusImages.Images.Add("disconnected", SystemIcons.Error.ToBitmap());

        _adapterList.Dock = DockStyle.Fill;
        _adapterList.View = View.Details;
        _adapterList.FullRowSelect = true;
        _adapterList.GridLines = true;
        _adapterList.HideSelection = false;
        _adapterList.MultiSelect = false;
        _adapterList.ShowItemToolTips = true;
        _adapterList.SmallImageList = _statusImages;
        _adapterList.BackColor = Color.White;
        _adapterList.BorderStyle = BorderStyle.None;
        _adapterList.Columns.Add("网卡名称", 270);
        _adapterList.Columns.Add("状态", 72);
        _adapterList.Columns.Add("连接速率", 112);
        _adapterList.Columns.Add("实时网速", 265);
        _adapterList.Columns.Add("IPv4 地址", 150);
        _adapterList.Columns.Add("IPv6 地址", 210);
        _adapterList.Columns.Add("类型", 90);
        _adapterList.Columns.Add("默认网关", 125);
        _adapterList.Columns.Add("MAC 地址", 130);

        var listContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 0),
            BackColor = BackColor
        };
        listContainer.Controls.Add(_adapterList);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = BackColor,
            Padding = new Padding(16, 10, 16, 6)
        };
        _updatedLabel.AutoSize = true;
        _updatedLabel.Text = "每 2 秒自动刷新";
        _updatedLabel.ForeColor = Color.FromArgb(92, 99, 112);
        footer.Controls.Add(_updatedLabel);

        Controls.Add(listContainer);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private void RegisterEvents()
    {
        _refreshButton.Click += (_, _) => ResumeFromIdle(refreshNow: true);
        _minimizeToTrayButton.Click += (_, _) => MinimizeToTray();
        _openConnectionsButton.Click += (_, _) => OpenNetworkConnections();
        _refreshTimer.Tick += (_, _) => RefreshAdapters();
        _networkChangeTimer.Tick += (_, _) =>
        {
            _networkChangeTimer.Stop();
            RefreshAdapters();
        };
        _idleMonitorTimer.Tick += (_, _) => CheckIdleState();
        _adapterList.ColumnClick += AdapterList_ColumnClick;
        _startupCheckBox.CheckedChanged += StartupCheckBox_CheckedChanged;
        NetworkChange.NetworkAddressChanged += NetworkChange_Changed;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_Changed;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _networkChangeTimer.Stop();
            _idleMonitorTimer.Stop();
            NetworkChange.NetworkAddressChanged -= NetworkChange_Changed;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_Changed;
        };
    }

    private void OpenNetworkConnections()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "ncpa.cpl",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法打开网络连接页面：\n{exception.Message}",
                "网卡监视器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void NetworkChange_Changed(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (ShouldPauseForIdle())
            {
                PauseForIdle();
                return;
            }

            _networkChangeTimer.Stop();
            _networkChangeTimer.Start();
        }));
    }

    private void RefreshAdapters(bool force = false)
    {
        if (_refreshing || IsDisposed)
        {
            return;
        }

        if (!force && ShouldPauseForIdle())
        {
            PauseForIdle();
            return;
        }

        _refreshing = true;
        _refreshButton.Enabled = false;

        try
        {
            _adapters = NetworkAdapterService.GetAdapters();
            if (!_isInTray)
            {
                UpdateAdapterList(GetSortedAdapters());
            }

            UpdateTraySpeed();

            var connected = _adapters.Count(adapter => adapter.IsConnected);
            if (!_isInTray)
            {
                _summaryLabel.Text = $"共 {_adapters.Count} 个网卡，{connected} 个已连接";
                _updatedLabel.Text = $"每 2 秒自动刷新 · 上次更新 {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception exception)
        {
            _summaryLabel.Text = "读取网卡状态失败";
            _updatedLabel.Text = exception.Message;
        }
        finally
        {
            _refreshButton.Enabled = true;
            _refreshing = false;
        }
    }

    private void CheckIdleState()
    {
        if (IsDisposed)
        {
            return;
        }

        if (ShouldPauseForIdle())
        {
            PauseForIdle();
            return;
        }

        if (_refreshPausedForIdle)
        {
            ResumeFromIdle(refreshNow: true);
        }
    }

    private bool ShouldPauseForIdle()
    {
        var idleTime = LastInputService.GetIdleTime();
        return idleTime.HasValue && idleTime.Value >= IdlePauseAfter;
    }

    private void PauseForIdle()
    {
        if (_refreshPausedForIdle)
        {
            return;
        }

        _refreshPausedForIdle = true;
        _refreshTimer.Stop();
        _networkChangeTimer.Stop();
        _idleMonitorTimer.Interval = PausedIdleCheckInterval;
        _refreshButton.Enabled = true;

        if (_isInTray)
        {
            _speedOverlay.UpdatePaused();
            SetNotifyIconText("网卡监视器 · 已休眠");
        }
        else
        {
            _updatedLabel.Text = "电脑已空闲超过 5 分钟，已暂停刷新 · 鼠标或键盘操作后自动恢复";
        }
    }

    private void ResumeFromIdle(bool refreshNow)
    {
        if (!_refreshPausedForIdle)
        {
            if (!_refreshTimer.Enabled)
            {
                _refreshTimer.Start();
            }

            if (refreshNow)
            {
                RefreshAdapters(force: true);
            }

            return;
        }

        _refreshPausedForIdle = false;
        _idleMonitorTimer.Interval = ActiveIdleCheckInterval;
        _refreshTimer.Start();

        if (!_isInTray)
        {
            _updatedLabel.Text = "已恢复刷新，正在读取网卡状态…";
        }

        if (refreshNow)
        {
            RefreshAdapters(force: true);
        }
    }

    private void ConfigureTray()
    {
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = "网卡监视器";
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        var openItem = new ToolStripMenuItem("打开主窗口");
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitFromTray();
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = _trayMenu;
    }

    private void MinimizeToTray()
    {
        _isInTray = true;
        _notifyIcon.Visible = true;
        UpdateTraySpeed();
        Hide();
        _speedOverlay.ShowNearSystemTray();
    }

    private void RestoreFromTray()
    {
        if (IsDisposed)
        {
            return;
        }

        _isInTray = false;
        _speedOverlay.Hide();
        _notifyIcon.Visible = false;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        UpdateAdapterList(GetSortedAdapters());
        var connected = _adapters.Count(adapter => adapter.IsConnected);
        _summaryLabel.Text = $"共 {_adapters.Count} 个网卡，{connected} 个已连接";
        _updatedLabel.Text = $"每 2 秒自动刷新 · 上次更新 {DateTime.Now:HH:mm:ss}";
    }

    private void ExitFromTray()
    {
        _isInTray = false;
        _speedOverlay.Hide();
        _notifyIcon.Visible = false;
        Close();
    }

    private void UpdateTraySpeed()
    {
        if (!_isInTray)
        {
            return;
        }

        var fastest = _adapters
            .OrderByDescending(adapter => adapter.TotalBytesPerSecond)
            .FirstOrDefault();

        var adapterName = fastest?.DisplayName ?? "没有可用网卡";
        var upload = fastest?.SendBytesPerSecond ?? 0;
        var download = fastest?.ReceiveBytesPerSecond ?? 0;
        _speedOverlay.UpdateSpeed(adapterName, upload, download);
        _speedOverlay.KeepAboveTaskbar();

        var tooltip = $"{adapterName}  ↑ {NetworkAdapterService.FormatDataRate(upload)}  ↓ {NetworkAdapterService.FormatDataRate(download)}";
        var notifyText = tooltip.Length <= 63 ? tooltip : tooltip[..63];
        SetNotifyIconText(notifyText);
    }

    private void SetNotifyIconText(string text)
    {
        if (!string.Equals(_lastNotifyIconText, text, StringComparison.Ordinal))
        {
            _notifyIcon.Text = text;
            _lastNotifyIconText = text;
        }
    }

    private void AdapterList_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column != 0 && e.Column != 2 && e.Column != 3)
        {
            return;
        }

        if (_sortColumn == e.Column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = e.Column;
            _sortAscending = true;
        }

        UpdateSortHeaders();
        UpdateAdapterList(GetSortedAdapters());
    }

    private IReadOnlyList<NetworkAdapterInfo> GetSortedAdapters()
    {
        if (_sortColumn < 0)
        {
            return _adapters.ToArray();
        }

        var sorted = _adapters.ToList();

        sorted.Sort((left, right) =>
        {
            int comparison;
            if (_sortColumn == 0)
            {
                comparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            }
            else if (_sortColumn == 2)
            {
                comparison = left.SpeedBitsPerSecond.CompareTo(right.SpeedBitsPerSecond);
            }
            else if (_sortColumn == 3)
            {
                comparison = left.TotalBytesPerSecond.CompareTo(right.TotalBytesPerSecond);
            }
            else
            {
                comparison = 0;
            }

            if (comparison == 0 && (_sortColumn == 2 || _sortColumn == 3))
            {
                comparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            }

            return _sortAscending ? comparison : -comparison;
        });

        return sorted;
    }

    private void UpdateSortHeaders()
    {
        _adapterList.Columns[0].Text = "网卡名称";
        _adapterList.Columns[2].Text = "连接速率";
        _adapterList.Columns[3].Text = "实时网速";

        if (_sortColumn >= 0)
        {
            var direction = _sortAscending ? "升序" : "降序";
            _adapterList.Columns[_sortColumn].Text += $"（{direction}）";
        }
    }

    private void UpdateAdapterList(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var selectedId = _adapterList.SelectedItems.Count > 0
            ? _adapterList.SelectedItems[0].Tag as string
            : null;

        _adapterList.BeginUpdate();
        try
        {
            _adapterList.Items.Clear();
            foreach (var adapter in adapters)
            {
                var item = new ListViewItem(adapter.DisplayName, adapter.IsConnected ? "connected" : "disconnected")
                {
                    Tag = adapter.Id,
                    ToolTipText = $"连接名称：{adapter.Name}"
                };
                item.SubItems.Add(adapter.Status);
                item.SubItems.Add(adapter.Speed);
                item.SubItems.Add(adapter.RealTimeSpeed);
                item.SubItems.Add(adapter.IPv4);
                item.SubItems.Add(adapter.IPv6);
                item.SubItems.Add(adapter.AdapterType);
                item.SubItems.Add(adapter.Gateway);
                item.SubItems.Add(adapter.MacAddress);
                _adapterList.Items.Add(item);

                if (adapter.Id == selectedId)
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _adapterList.EndUpdate();
        }
    }

    private void StartupCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingStartupCheckBox)
        {
            return;
        }

        try
        {
            StartupService.SetEnabled(_startupCheckBox.Checked);
            _startupCheckBox.Text = "开机自动启动";
            _startupCheckBox.ForeColor = SystemColors.ControlText;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"修改开机启动设置失败：\n{exception.Message}",
                "网卡监视器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            UpdateStartupCheckBox();
        }
    }

    private void UpdateStartupCheckBox()
    {
        _updatingStartupCheckBox = true;
        try
        {
            _startupCheckBox.Checked = StartupService.IsEnabled();
        }
        finally
        {
            _updatingStartupCheckBox = false;
        }
    }
}

// END_OF_SOURCE_FILE
