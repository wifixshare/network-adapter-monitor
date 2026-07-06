using System.Runtime.InteropServices;
using System.Text;
using NetworkCardMonitor.Services;

namespace NetworkCardMonitor;

internal sealed class SpeedOverlayForm : Form
{
    private const int OverlayWidth = 160;
    private const int OverlayHeight = 46;
    private const int SpeedLineHeight = 26;
    private const int GapFromHiddenIconsButton = 20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopMost = new IntPtr(-1);

    private readonly Label _uploadLabel = new();
    private readonly Label _downloadLabel = new();
    private readonly Action _restoreAction;
    private readonly System.Windows.Forms.Timer _keepAboveTimer = new() { Interval = 500 };
    private bool _disposing;

    public SpeedOverlayForm(Action restoreAction)
    {
        _restoreAction = restoreAction;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(OverlayWidth, OverlayHeight);
        BackColor = Color.FromArgb(30, 30, 30);

        ConfigureSpeedLabel(_uploadLabel, "↑: 0 B/s");
        ConfigureSpeedLabel(_downloadLabel, "↓: 0 B/s");

        Cursor = Cursors.Hand;
        Click += (_, _) => _restoreAction();
        Controls.Add(_uploadLabel);
        Controls.Add(_downloadLabel);
        Resize += (_, _) => LayoutSpeedLabels();
        _keepAboveTimer.Tick += (_, _) =>
        {
            UpdateTaskbarAppearance();
            KeepAboveTaskbar();
        };
        LayoutSpeedLabels();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_disposing)
        {
            return;
        }

        if (Visible)
        {
            _keepAboveTimer.Start();
        }
        else
        {
            _keepAboveTimer.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposing = true;
            _keepAboveTimer.Stop();
            _keepAboveTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void UpdateSpeed(string adapterName, double uploadBytesPerSecond, double downloadBytesPerSecond)
    {
        var upload = NetworkAdapterService.FormatDataRate(uploadBytesPerSecond);
        var download = NetworkAdapterService.FormatDataRate(downloadBytesPerSecond);
        _uploadLabel.Text = $"↑: {upload}";
        _downloadLabel.Text = $"↓: {download}";
        AccessibleName = $"{adapterName}，上传 {upload}，下载 {download}";
    }

    private void ConfigureSpeedLabel(Label label, string text)
    {
        label.AutoSize = false;
        label.BackColor = BackColor;
        label.ForeColor = Color.White;
        label.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Padding = new Padding(4, 0, 0, 0);
        label.Cursor = Cursors.Hand;
        label.Text = text;
        label.AutoEllipsis = false;
        label.Click += (_, _) => _restoreAction();
    }

    private void LayoutSpeedLabels()
    {
        var lineHeight = Math.Min(SpeedLineHeight, Math.Max(1, ClientSize.Height / 2));
        _uploadLabel.Bounds = new Rectangle(0, 0, ClientSize.Width, lineHeight);
        _downloadLabel.Bounds = new Rectangle(
            0,
            lineHeight,
            ClientSize.Width,
            Math.Min(lineHeight, ClientSize.Height - lineHeight));
    }

    public void ShowNearSystemTray()
    {
        Bounds = CalculateBounds();
        UpdateTaskbarAppearance();
        if (!Visible)
        {
            Show();
        }
        else
        {
            Invalidate();
        }
    }

    private void UpdateTaskbarAppearance()
    {
        var background = TrySampleTaskbarColor();
        if (!background.HasValue)
        {
            return;
        }

        var backgroundColor = background.Value;
        var brightness =
            backgroundColor.R * 0.299 +
            backgroundColor.G * 0.587 +
            backgroundColor.B * 0.114;
        var textColor = brightness >= 150 ? Color.Black : Color.White;

        if (BackColor.ToArgb() != backgroundColor.ToArgb())
        {
            BackColor = backgroundColor;
            _uploadLabel.BackColor = backgroundColor;
            _downloadLabel.BackColor = backgroundColor;
        }

        _uploadLabel.ForeColor = textColor;
        _downloadLabel.ForeColor = textColor;
    }

    private Color? TrySampleTaskbarColor()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return null;
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var x = Bounds.Right + 2;
            var sampleYs = new[]
            {
                Bounds.Top + 5,
                Bounds.Top + Bounds.Height / 2,
                Bounds.Bottom - 5
            };
            var colors = new List<Color>();

            foreach (var y in sampleYs)
            {
                var colorReference = GetPixel(screenDc, x, y);
                if (colorReference == uint.MaxValue)
                {
                    continue;
                }

                colors.Add(Color.FromArgb(
                    (int)(colorReference & 0xFF),
                    (int)((colorReference >> 8) & 0xFF),
                    (int)((colorReference >> 16) & 0xFF)));
            }

            if (colors.Count == 0)
            {
                return null;
            }

            var red = colors.Select(color => color.R).OrderBy(value => value).ElementAt(colors.Count / 2);
            var green = colors.Select(color => color.G).OrderBy(value => value).ElementAt(colors.Count / 2);
            var blue = colors.Select(color => color.B).OrderBy(value => value).ElementAt(colors.Count / 2);
            return Color.FromArgb(red, green, blue);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void KeepAboveTaskbar()
    {
        if (!Visible || !IsHandleCreated)
        {
            return;
        }

        SetWindowPos(
            Handle,
            HwndTopMost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private static Rectangle CalculateBounds()
    {
        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out var taskbarRect))
        {
            var taskbar = taskbarRect.ToRectangle();
            var trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
            NativeRect trayRect = default;
            var hasTrayRect = trayHandle != IntPtr.Zero && GetWindowRect(trayHandle, out trayRect);

            if (taskbar.Width > taskbar.Height)
            {
                var hiddenIconsButtonLeft = trayHandle != IntPtr.Zero
                    ? TryGetHiddenIconsButtonLeft(trayHandle, taskbar)
                    : null;
                var anchorLeft = hiddenIconsButtonLeft
                    ?? (hasTrayRect ? trayRect.Left : taskbar.Right - 220);
                var right = anchorLeft - GapFromHiddenIconsButton;
                var x = Math.Max(taskbar.Left, right - OverlayWidth);
                var height = Math.Max(1, taskbar.Height - 2);
                var y = taskbar.Top + 1;
                return new Rectangle(x, y, OverlayWidth, height);
            }
        }

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 680);
        return new Rectangle(
            workingArea.Right - OverlayWidth - 220,
            workingArea.Bottom + 2,
            OverlayWidth,
            OverlayHeight);
    }

    private static int? TryGetHiddenIconsButtonLeft(IntPtr trayHandle, Rectangle taskbar)
    {
        var candidates = new List<Rectangle>();
        EnumChildWindows(
            trayHandle,
            (childHandle, _) =>
            {
                var className = new StringBuilder(256);
                if (GetClassName(childHandle, className, className.Capacity) <= 0)
                {
                    return true;
                }

                if (!string.Equals(className.ToString(), "Button", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!GetWindowRect(childHandle, out var childRect))
                {
                    return true;
                }

                var bounds = childRect.ToRectangle();
                if (bounds.Width is >= 8 and <= 50 &&
                    bounds.Height is >= 8 and <= 60 &&
                    bounds.IntersectsWith(taskbar))
                {
                    candidates.Add(bounds);
                }

                return true;
            },
            IntPtr.Zero);

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.OrderBy(bounds => bounds.Left).First().Left;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentHandle,
        IntPtr childAfter,
        string className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentHandle,
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }
}

// END_OF_SOURCE_FILE
