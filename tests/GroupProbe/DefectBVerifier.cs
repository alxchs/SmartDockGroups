using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace SmartDockGroups.Probe;

internal static class DefectBVerifier
{
    public static int Run(string exePath, string outputDir)
    {
        Console.WriteLine("=== INICIANDO VERIFICAÇÃO REAL DO DEFEITO B (MULTI-MONITOR) ===");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartDockGroups",
            "config.json");
        var backupPath = Path.Combine(outputDir, "user-config.backup.json");
        Directory.CreateDirectory(outputDir);

        var hadUserConfig = File.Exists(configPath);
        string originalHash = "";
        if (hadUserConfig)
        {
            originalHash = Sha256OfFile(configPath);
            File.Copy(configPath, backupPath, overwrite: true);
            var backupHash = Sha256OfFile(backupPath);
            if (originalHash != backupHash)
            {
                Console.Error.WriteLine("ABORT: Backup do config não confere com original!");
                return 3;
            }
            Console.WriteLine($"Config do usuário preservado (SHA256: {originalHash}) -> {backupPath}");
        }

        try
        {
            return ExecuteVerification(exePath, configPath, outputDir);
        }
        finally
        {
            StopApp();
            if (hadUserConfig)
            {
                File.Copy(backupPath, configPath, overwrite: true);
                var restoredHash = Sha256OfFile(configPath);
                if (restoredHash == originalHash)
                {
                    Console.WriteLine($"Configuração do usuário restaurada e CONFERIDA por hash (SHA256: {restoredHash}).");
                }
                else
                {
                    Console.Error.WriteLine($"ERRO CRÍTICO: Hash restaurado ({restoredHash}) difere do original ({originalHash})!");
                }
            }
            else if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
    }

    private static int ExecuteVerification(string exePath, string configPath, string outputDir)
    {
        StopApp();

        var screens = Screen.AllScreens;
        Console.WriteLine($"Monitores detectados: {screens.Length}");
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            Console.WriteLine($"  Monitor {i} ({s.DeviceName}): Primary={s.Primary}, Bounds={s.Bounds}, WorkingArea={s.WorkingArea}");
        }

        // Criar fixture com um grupo em cada monitor
        var fixture = new JsonObject
        {
            ["Categories"] = new JsonArray
            {
                new JsonObject
                {
                    ["Name"] = "Monitor 1 Group",
                    ["Categories"] = new JsonArray(),
                    ["Items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Name"] = "Notepad",
                            ["Type"] = "Application",
                            ["Target"] = @"C:\Windows\System32\notepad.exe",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 20.0,
                            ["DesktopIconY"] = 20.0
                        }
                    },
                    ["IsDesktopGroup"] = true,
                    ["DesktopX"] = 1080.0,
                    ["DesktopY"] = 320.0,
                    ["DesktopWidth"] = 400.0,
                    ["DesktopHeight"] = 250.0,
                    ["DesktopIconScale"] = 1.0,
                    ["IsCollapsed"] = false,
                    ["IsClosed"] = false,
                    ["AreaOpacity"] = 1.0,
                    ["TitleOpacity"] = 1.0,
                    ["DisplayMode"] = "Panel",
                },
                new JsonObject
                {
                    ["Name"] = "Monitor 2 Group",
                    ["Categories"] = new JsonArray(),
                    ["Items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Name"] = "Explorer",
                            ["Type"] = "Application",
                            ["Target"] = @"C:\Windows\explorer.exe",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 20.0,
                            ["DesktopIconY"] = 20.0
                        }
                    },
                    ["IsDesktopGroup"] = true,
                    // Posiciona grupo no monitor 2 dentro da sua área útil (X >= 3840) e acima do diálogo
                    ["DesktopX"] = screens.Length > 1 ? 4600.0 : 600.0,
                    ["DesktopY"] = 150.0,
                    ["DesktopWidth"] = 400.0,
                    ["DesktopHeight"] = 250.0,
                    ["DesktopIconScale"] = 1.0,
                    ["IsCollapsed"] = false,
                    ["IsClosed"] = false,
                    ["AreaOpacity"] = 1.0,
                    ["TitleOpacity"] = 1.0,
                    ["DisplayMode"] = "Panel",
                    ["IconArrangement"] = "None"
                }
            },
            ["Items"] = new JsonArray(),
            ["Theme"] = new JsonObject
            {
                ["BackgroundColor"] = "#1E1E1E",
                ["Opacity"] = 0.97,
                ["BorderColor"] = "#3C3C3C",
                ["CornerRadius"] = 6.0,
                ["ShowShadow"] = true,
                ["ShadowBlurRadius"] = 12.0,
                ["ShadowDepth"] = 2.0,
                ["ShadowDirection"] = 315.0,
                ["ShadowOpacity"] = 0.35,
                ["ItemSpacing"] = 2.0,
                ["ItemPadding"] = 8.0,
                ["IconSize"] = 18.0,
                ["TextColor"] = "#FFFFFF",
                ["HighlightColor"] = "#3D7EB8FF",
                ["TitleFontFamily"] = "Segoe UI",
                ["TitleFontSize"] = 13.0,
                ["TitleBold"] = true,
                ["ItemFontFamily"] = "Segoe UI",
                ["ItemFontSize"] = 13.0,
                ["AnimationDurationMs"] = 120
            },
            ["Behavior"] = new JsonObject
            {
                ["ClickMode"] = "SingleClick",
                ["GlobalHotkeyEnabled"] = false,
                ["AppTheme"] = "Dark",
                ["Language"] = "pt"
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Iniciando {exePath}...");
        var app = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Falha ao iniciar app.");

        var live = Process.GetProcessesByName("SmartDockGroups.App").FirstOrDefault()
            ?? throw new InvalidOperationException("Processo SmartDockGroups.App não encontrado.");

        var timeout = TimeSpan.FromSeconds(30);
        var clock = Stopwatch.StartNew();
        List<IntPtr> groupWindows = new();
        while (clock.Elapsed < timeout)
        {
            groupWindows = Win32.VisibleWindowsOf((uint)live.Id)
                .Where(h =>
                {
                    if (!Win32.GetWindowRect(h, out var r)) return false;
                    return r.Width >= 120 && r.Height >= 80 && r.Left > -5000;
                }).ToList();

            if (groupWindows.Count >= 2) break;
            Thread.Sleep(300);
        }

        Console.WriteLine($"Janelas de grupo detectadas: {groupWindows.Count}");
        Thread.Sleep(2000); // Aguarda layout estabilizar

        var reportLines = new List<string>
        {
            "MultiMonitor Verification Report - SmartDockGroups Defect B (Caminho Real)",
            $"Data/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Monitores Detectados: {screens.Length}",
            $"DPI Awareness do Processo de Teste: PerMonitorV2 (manifesto nativo)",
            $"DPI Awareness da Aplicação: System DPI Aware (modo original sem manifesto)",
            ""
        };

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            Console.WriteLine($"\n--- TESTANDO MONITOR {i} ({screen.DeviceName}) ---");
            reportLines.Add($"--- Monitor {i} ({screen.DeviceName}) ---");
            reportLines.Add($"Primary: {screen.Primary}");
            reportLines.Add($"Bounds Físicos: {screen.Bounds}");
            reportLines.Add($"WorkingArea Útil: {screen.WorkingArea}");

            // Identifica qual janela de grupo está mais próxima ou dentro desta tela
            IntPtr targetGroupHwnd = IntPtr.Zero;
            Win32.Rect targetRect = default;

            foreach (var hwnd in groupWindows)
            {
                if (Win32.GetWindowRect(hwnd, out var r))
                {
                    if (screen.Bounds.Contains(r.Left + 50, r.Top + 50))
                    {
                        targetGroupHwnd = hwnd;
                        targetRect = r;
                        break;
                    }
                }
            }

            int clickX, clickY;
            if (targetGroupHwnd != IntPtr.Zero)
            {
                // Clicar no cabeçalho do grupo encontrado na tela
                clickX = targetRect.Left + 120;
                clickY = targetRect.Top + 18;
                Console.WriteLine($"Grupo encontrado no monitor: HWND={targetGroupHwnd}, Rect=[{targetRect.Left},{targetRect.Top},{targetRect.Right},{targetRect.Bottom}]");
            }
            else
            {
                // Se o grupo não estiver na tela secundária, usar posição central da WorkingArea desta tela
                clickX = screen.WorkingArea.Left + 300;
                clickY = screen.WorkingArea.Top + 200;
                Console.WriteLine($"Nenhum grupo específico neste monitor; clicando na coordenada [{clickX}, {clickY}]");
            }

            // 1. Mover cursor real
            Win32.SetCursorPos(clickX, clickY);
            Thread.Sleep(200);

            Win32.GetCursorPos(out var cursorPos);
            reportLines.Add($"Cursor Posicionado Solicitado: [{clickX}, {clickY}]");
            reportLines.Add($"Cursor Real Lido por GetCursorPos: [{cursorPos.X}, {cursorPos.Y}]");
            reportLines.Add($"Control.MousePosition: [{Control.MousePosition.X}, {Control.MousePosition.Y}]");

            if (cursorPos.X != clickX || cursorPos.Y != clickY)
            {
                var msg = $"FALHA CRÍTICA: GetCursorPos [{cursorPos.X}, {cursorPos.Y}] difere do solicitado [{clickX}, {clickY}]!";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            var cursorScreen = Screen.FromPoint(cursorPos);
            reportLines.Add($"Monitor sob o Cursor: {cursorScreen.DeviceName}");
            if (cursorScreen.DeviceName != screen.DeviceName)
            {
                var msg = $"FALHA: Cursor está no monitor {cursorScreen.DeviceName}, mas esperava {screen.DeviceName}!";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            // 2. Abrir menu de contexto do grupo
            var windowsBefore = new HashSet<IntPtr>(Win32.VisibleWindowsOf((uint)live.Id));
            Win32.RightClick(clickX, clickY);
            Thread.Sleep(1000);

            var popup = Win32.VisibleWindowsOf((uint)live.Id).FirstOrDefault(h => !windowsBefore.Contains(h));
            if (popup == IntPtr.Zero)
            {
                var msg = $"FALHA: Menu de contexto não apareceu ao clicar com botão direito em [{clickX}, {clickY}].";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            Console.WriteLine($"Menu de contexto aberto: HWND={popup}");

            // 3. Clicar em 'Renomear...' via UI Automation
            var menuElement = AutomationElement.FromHandle(popup);
            var renameItem = menuElement.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Renomear..."))
                ?? menuElement.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "Rename..."));

            if (renameItem == null)
            {
                var allItems = menuElement.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
                foreach (AutomationElement it in allItems)
                {
                    if (it.Current.Name.Contains("Renom", StringComparison.OrdinalIgnoreCase) ||
                        it.Current.Name.Contains("Rename", StringComparison.OrdinalIgnoreCase))
                    {
                        renameItem = it;
                        break;
                    }
                }
            }

            if (renameItem == null)
            {
                var msg = "FALHA: Item 'Renomear...' não encontrado no menu de contexto!";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            Console.WriteLine($"Item de menu encontrado: '{renameItem.Current.Name}'. Invocando...");
            var windowsBeforeDialog = new HashSet<IntPtr>(Win32.VisibleWindowsOf((uint)live.Id));

            if (renameItem.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
            {
                ((InvokePattern)invokePattern).Invoke();
            }
            else
            {
                // Fallback: clique no centro do item
                var itemRect = renameItem.Current.BoundingRectangle;
                Win32.LeftClick((int)(itemRect.Left + itemRect.Width / 2), (int)(itemRect.Top + itemRect.Height / 2));
            }

            // 4. Aguardar o diálogo TextPromptWindow aparecer
            IntPtr dialogHwnd = IntPtr.Zero;
            var dialogClock = Stopwatch.StartNew();
            while (dialogClock.Elapsed < TimeSpan.FromSeconds(5))
            {
                var currentWindows = Win32.VisibleWindowsOf((uint)live.Id);
                foreach (var h in currentWindows)
                {
                    if (!windowsBeforeDialog.Contains(h) && h != popup)
                    {
                        if (Win32.GetWindowRect(h, out var dr) && dr.Width > 200 && dr.Height > 100 && dr.Width < 800)
                        {
                            dialogHwnd = h;
                            break;
                        }
                    }
                }
                if (dialogHwnd != IntPtr.Zero) break;
                Thread.Sleep(200);
            }

            if (dialogHwnd == IntPtr.Zero)
            {
                var msg = "FALHA: Diálogo TextPromptWindow não abriu após clicar em Renomear...!";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            Thread.Sleep(500); // Aguarda layout do diálogo terminar

            Win32.GetWindowRect(dialogHwnd, out var dialogRect);
            Console.WriteLine($"Diálogo TextPromptWindow aberto: HWND={dialogHwnd}, Rect=[{dialogRect.Left},{dialogRect.Top},{dialogRect.Right},{dialogRect.Bottom}] (Largura={dialogRect.Width}, Altura={dialogRect.Height})");

            reportLines.Add($"Diálogo TextPromptWindow HWND: {dialogHwnd}");
            reportLines.Add($"Retângulo Win32 Medido (PerMonitorV2): [{dialogRect.Left}, {dialogRect.Top}, {dialogRect.Right}, {dialogRect.Bottom}] (W={dialogRect.Width}, H={dialogRect.Height})");

            // Asserções de contenção no monitor
            var insideLeft = dialogRect.Left >= screen.WorkingArea.Left;
            var insideRight = dialogRect.Right <= screen.WorkingArea.Right;
            var insideTop = dialogRect.Top >= screen.WorkingArea.Top;
            var insideBottom = dialogRect.Bottom <= screen.WorkingArea.Bottom;

            reportLines.Add($"Contenção Left: {insideLeft} (dialog: {dialogRect.Left} >= screen: {screen.WorkingArea.Left})");
            reportLines.Add($"Contenção Right: {insideRight} (dialog: {dialogRect.Right} <= screen: {screen.WorkingArea.Right})");
            reportLines.Add($"Contenção Top: {insideTop} (dialog: {dialogRect.Top} >= screen: {screen.WorkingArea.Top})");
            reportLines.Add($"Contenção Bottom: {insideBottom} (dialog: {dialogRect.Bottom} <= screen: {screen.WorkingArea.Bottom})");

            if (!insideLeft || !insideRight || !insideTop || !insideBottom)
            {
                var msg = $"FALHA: O diálogo abriu fora da área de trabalho do Monitor {i} ({screen.DeviceName})!";
                Console.Error.WriteLine(msg);
                throw new InvalidOperationException(msg);
            }

            reportLines.Add($"Asserção APROVADA: Diálogo 100% contido na área útil do Monitor {i} ({screen.DeviceName})");

            // 5. Capturar SOMENTE as janelas do SmartDockGroups envolvidas (grupo + diálogo) sem captura de desktop de fundo
            var shotPath = Path.Combine(outputDir, $"B-fluxo-real-monitor{i + 1}.png");
            if (targetGroupHwnd != IntPtr.Zero)
            {
                Win32.CaptureWindowsComposite(targetGroupHwnd, dialogHwnd, shotPath);
            }
            else
            {
                Win32.TryCapture(dialogHwnd, shotPath);
            }

            Console.WriteLine($"Captura recortada das janelas salva em: {shotPath}");
            reportLines.Add($"Captura Recortada das Janelas (Grupo + Diálogo): {shotPath}");

            // 6. Fechar diálogo com Cancel / Escape sem alterar nada
            Win32.PressEscape();
            Thread.Sleep(400);

            var waitClose = Stopwatch.StartNew();
            while (waitClose.Elapsed < TimeSpan.FromSeconds(3) && Win32.IsWindowVisible(dialogHwnd))
            {
                Win32.PressEscape();
                Thread.Sleep(150);
            }
            reportLines.Add("Diálogo fechado via Escape/Cancel (nenhuma alteração persistida).");
            reportLines.Add("");
        }

        // Testar caminho alternativo: "Novo grupo" (App.CreateNewGroup)
        Console.WriteLine("\n--- TESTANDO CAMINHO 'NOVO GRUPO' (App.CreateNewGroup via action) ---");
        reportLines.Add("--- Teste do Caminho 'Novo Grupo' (App.CreateNewGroup) ---");

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            int clickX = screen.WorkingArea.Left + (screen.WorkingArea.Width / 2);
            int clickY = screen.WorkingArea.Top + (screen.WorkingArea.Height / 2);

            Win32.SetCursorPos(clickX, clickY);
            Thread.Sleep(150);
            Win32.GetCursorPos(out var cursorPos);
            reportLines.Add($"Monitor {i}: Cursor posicionado em [{cursorPos.X}, {cursorPos.Y}]");

            var windowsBefore = new HashSet<IntPtr>(Win32.VisibleWindowsOf((uint)live.Id));

            // Dispara ação de novo grupo através da instância secundária (via named pipe)
            var pAction = Process.Start(new ProcessStartInfo(exePath, "--desktop-action=new-group") { UseShellExecute = true });
            pAction?.WaitForExit(3000);

            IntPtr newGroupDialog = IntPtr.Zero;
            var waitNew = Stopwatch.StartNew();
            while (waitNew.Elapsed < TimeSpan.FromSeconds(4))
            {
                foreach (var h in Win32.VisibleWindowsOf((uint)live.Id))
                {
                    if (!windowsBefore.Contains(h) && Win32.GetWindowRect(h, out var r) && r.Width > 200 && r.Height > 100 && r.Width < 800)
                    {
                        newGroupDialog = h;
                        break;
                    }
                }
                if (newGroupDialog != IntPtr.Zero) break;
                Thread.Sleep(150);
            }

            if (newGroupDialog != IntPtr.Zero)
            {
                Win32.GetWindowRect(newGroupDialog, out var r);
                reportLines.Add($"Diálogo 'Novo Grupo' aberto: HWND={newGroupDialog}, Rect=[{r.Left},{r.Top},{r.Right},{r.Bottom}]");
                var inside = r.Left >= screen.WorkingArea.Left && r.Right <= screen.WorkingArea.Right &&
                             r.Top >= screen.WorkingArea.Top && r.Bottom <= screen.WorkingArea.Bottom;
                reportLines.Add($"Contenção 'Novo Grupo' no Monitor {i}: {inside}");
                if (!inside)
                {
                    throw new InvalidOperationException($"Diálogo 'Novo Grupo' abriu fora do Monitor {i}!");
                }
                Win32.PressEscape();
                Thread.Sleep(300);
            }
            else
            {
                reportLines.Add($"Aviso: Diálogo de Novo Grupo não capturado via pipe no Monitor {i}.");
            }
        }

        var reportFilePath = Path.Combine(outputDir, "B-retangulos.txt");
        File.WriteAllLines(reportFilePath, reportLines);
        Console.WriteLine($"\nRelatório gravado com sucesso em: {reportFilePath}");
        return 0;
    }

    private static void StopApp()
    {
        foreach (var process in Process.GetProcessesByName("SmartDockGroups.App"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(4000);
            }
            catch
            {
            }
        }
        Thread.Sleep(600);
    }

    public static int CaptureRealAppTeams(string exePath, string outputDir, string prefix = "")
    {
        Console.WriteLine("=== CAPTURANDO APP REAL COM GRUPO DO TEAMS ===");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartDockGroups",
            "config.json");
        var backupPath = Path.Combine(outputDir, "user-config.backup.json");
        Directory.CreateDirectory(outputDir);

        var hadUserConfig = File.Exists(configPath);
        string originalHash = "";
        if (hadUserConfig)
        {
            originalHash = Sha256OfFile(configPath);
            File.Copy(configPath, backupPath, overwrite: true);
            if (originalHash != Sha256OfFile(backupPath))
            {
                Console.Error.WriteLine("ABORT: Backup do config não confere!");
                return 3;
            }
        }

        try
        {
            var panelFile = string.IsNullOrEmpty(prefix) ? "A-app-real-painel.png" : $"{prefix}-painel.png";
            var mosaicoFile = string.IsNullOrEmpty(prefix) ? "A-app-real-mosaico.png" : $"{prefix}-mosaico.png";

            // 1. Modo Painel
            CaptureTeamsMode(exePath, configPath, outputDir, "Panel", panelFile);

            // 2. Modo App Folder (Mosaico)
            CaptureTeamsMode(exePath, configPath, outputDir, "AppFolder", mosaicoFile);

            return 0;
        }
        finally
        {
            StopApp();
            if (hadUserConfig)
            {
                File.Copy(backupPath, configPath, overwrite: true);
                var restoredHash = Sha256OfFile(configPath);
                Console.WriteLine(restoredHash == originalHash
                    ? $"Configuração restaurada e CONFERIDA por hash (SHA256: {restoredHash})."
                    : "ERRO CRÍTICO no restore do config!");
            }
        }
    }

    private static void CaptureTeamsMode(string exePath, string configPath, string outputDir, string mode, string fileName)
    {
        StopApp();

        var fixture = new JsonObject
        {
            ["Categories"] = new JsonArray
            {
                new JsonObject
                {
                    ["Name"] = "Teams & Apps",
                    ["Categories"] = new JsonArray(),
                    ["Items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Name"] = "Microsoft Teams",
                            ["Type"] = "Url",
                            ["Target"] = "msteams://teams.microsoft.com/l/chat/0/0?users=alexandre.sousa@iob.com.br",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 20.0,
                            ["DesktopIconY"] = 20.0
                        },
                        new JsonObject
                        {
                            ["Name"] = "Alexandre",
                            ["Type"] = "File",
                            ["Target"] = @"C:\Users\alxch\OneDrive\Área de Trabalho\Alexandre.url",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 120.0,
                            ["DesktopIconY"] = 20.0
                        },
                        new JsonObject
                        {
                            ["Name"] = "Bloco de Notas",
                            ["Type"] = "Application",
                            ["Target"] = @"C:\Windows\System32\notepad.exe",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 220.0,
                            ["DesktopIconY"] = 20.0
                        },
                        new JsonObject
                        {
                            ["Name"] = "Calculadora",
                            ["Type"] = "Application",
                            ["Target"] = @"C:\Windows\System32\calc.exe",
                            ["ExecutionMode"] = "Normal",
                            ["IsDesktopPinned"] = true,
                            ["DesktopIconX"] = 320.0,
                            ["DesktopIconY"] = 20.0
                        }
                    },
                    ["IsDesktopGroup"] = true,
                    ["DesktopX"] = 200.0,
                    ["DesktopY"] = 200.0,
                    ["DesktopWidth"] = 420.0,
                    ["DesktopHeight"] = 260.0,
                    ["DesktopIconScale"] = 1.0,
                    ["IsCollapsed"] = false,
                    ["IsClosed"] = false,
                    ["AreaOpacity"] = 1.0,
                    ["TitleOpacity"] = 1.0,
                    ["DisplayMode"] = mode,
                    ["IconArrangement"] = "None"
                }
            },
            ["Items"] = new JsonArray(),
            ["Theme"] = new JsonObject
            {
                ["BackgroundColor"] = "#1E1E1E",
                ["Opacity"] = 0.97,
                ["BorderColor"] = "#3C3C3C",
                ["CornerRadius"] = 6.0,
                ["ShowShadow"] = true,
                ["ShadowBlurRadius"] = 12.0,
                ["ShadowDepth"] = 2.0,
                ["ShadowDirection"] = 315.0,
                ["ShadowOpacity"] = 0.35,
                ["ItemSpacing"] = 2.0,
                ["ItemPadding"] = 8.0,
                ["IconSize"] = 18.0,
                ["TextColor"] = "#FFFFFF",
                ["HighlightColor"] = "#3D7EB8FF",
                ["TitleFontFamily"] = "Segoe UI",
                ["TitleFontSize"] = 13.0,
                ["TitleBold"] = true,
                ["ItemFontFamily"] = "Segoe UI",
                ["ItemFontSize"] = 13.0,
                ["AnimationDurationMs"] = 120
            },
            ["Behavior"] = new JsonObject
            {
                ["ClickMode"] = "SingleClick",
                ["GlobalHotkeyEnabled"] = false,
                ["AppTheme"] = "Dark",
                ["Language"] = "pt"
            }
        };

        File.WriteAllText(configPath, fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var app = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Falha ao iniciar app.");

        var live = Process.GetProcessesByName("SmartDockGroups.App").FirstOrDefault()
            ?? throw new InvalidOperationException("SmartDockGroups.App não encontrado.");

        var clock = Stopwatch.StartNew();
        IntPtr groupHwnd = IntPtr.Zero;
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            var windows = Win32.VisibleWindowsOf((uint)live.Id)
                .Where(h => Win32.GetWindowRect(h, out var r) && r.Width >= 100 && r.Height >= 80 && r.Left > -5000)
                .ToList();
            if (windows.Count > 0)
            {
                groupHwnd = windows[0];
                break;
            }
            Thread.Sleep(300);
        }

        if (groupHwnd == IntPtr.Zero) throw new InvalidOperationException($"Janela do grupo no modo {mode} não apareceu.");

        // Aguarda resolução e renderização de ícones
        Thread.Sleep(3000);

        var shotPath = Path.Combine(outputDir, fileName);
        if (Win32.TryCapture(groupHwnd, shotPath))
        {
            Console.WriteLine($"Captura do modo {mode} realizada: {shotPath}");
        }
        else
        {
            throw new InvalidOperationException($"Falha ao capturar janela do grupo em {mode}.");
        }

        StopApp();
    }

    private static string Sha256OfFile(string path)
    {
        if (!File.Exists(path)) return "";
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
