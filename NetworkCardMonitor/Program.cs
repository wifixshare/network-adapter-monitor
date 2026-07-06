namespace NetworkCardMonitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(
                $"程序遇到错误：\n{eventArgs.Exception.Message}",
                "网卡监视器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        var startInTray = args.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        using var mainForm = new MainForm(startInTray);
        Application.Run(mainForm);
    }
}

// END_OF_SOURCE_FILE
