using System.Diagnostics;

namespace DualView.Server.Services;

public sealed class GUILauncher : IGUILauncher
{
    public void Launch(string path)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
        };

        // setsid gives the GUI its own session on Linux, without invoking a shell.
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/setsid"))
        {
            startInfo.FileName = "/usr/bin/setsid";
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(path);
        }

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("The GUI launcher did not start a process");
    }
}
