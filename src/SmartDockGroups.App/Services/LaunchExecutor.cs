using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using SmartDockGroups.Core.Models;

namespace SmartDockGroups.App.Services;

internal static class LaunchExecutor
{
    public static void Execute(LaunchItem item)
    {
        try
        {
            Process.Start(BuildStartInfo(item));
        }
        catch (Win32Exception)
        {
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static ProcessStartInfo BuildStartInfo(LaunchItem item)
    {
        var target = item.Target;
        if (item.Type != LaunchItemType.Command && !target.Contains("://") && !File.Exists(target) && !Directory.Exists(target))
        {
            if (ShellCommands.TryResolveTarget(item, out var resolved))
            {
                target = resolved;
            }
        }

        var startInfo = item.Type == LaunchItemType.Command
            ? BuildCommandStartInfo(item)
            : new ProcessStartInfo(target) { Arguments = item.Arguments ?? string.Empty };

        startInfo.UseShellExecute = true;

        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            startInfo.WorkingDirectory = item.WorkingDirectory;
        }

        switch (item.ExecutionMode)
        {
            case ExecutionMode.Minimized:
                startInfo.WindowStyle = ProcessWindowStyle.Minimized;
                break;
            case ExecutionMode.Administrator:
                startInfo.Verb = "runas";
                break;
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildCommandStartInfo(LaunchItem item)
    {
        var startInfo = new ProcessStartInfo("cmd.exe");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(item.Target);

        if (!string.IsNullOrWhiteSpace(item.Arguments))
        {
            startInfo.ArgumentList.Add(item.Arguments);
        }

        return startInfo;
    }
}
