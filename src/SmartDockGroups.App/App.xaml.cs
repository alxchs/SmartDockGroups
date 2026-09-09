using System.Windows;
using System.Windows.Forms;
using SmartDockGroups.App.Services;
using SmartDockGroups.Core.Configuration;
using SmartDockGroups.Core.Models;
using Application = System.Windows.Application;

namespace SmartDockGroups.App;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        var configuration = configurationStore.Load();

        var menu = BuildTrayMenu(configuration);

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "SmartDockGroups",
            ContextMenuStrip = menu
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                menu.Show(Cursor.Position);
            }
        };
    }

    private static ContextMenuStrip BuildTrayMenu(LauncherConfiguration configuration)
    {
        var menu = new ContextMenuStrip();

        foreach (var item in configuration.Items)
        {
            menu.Items.Add(item.Name, null, (_, _) => LaunchExecutor.Execute(item));
        }

        if (configuration.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Sair", null, (_, _) => Current.Shutdown());

        return menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
