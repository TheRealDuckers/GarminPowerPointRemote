using System.Windows.Forms;

namespace PptRemoteServer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "GarminPowerPointRemote.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PPT Remote is already running. Look for it in the system tray.",
                "PPT Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startHidden: args.Contains("--minimized")));
    }
}
