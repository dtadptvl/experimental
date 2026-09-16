namespace KiloSessionManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            MessageBox.Show(
                "Kilo Session Manager requires Windows 10 version 1809 or newer because it uses ConPTY.",
                "Unsupported Windows version",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
