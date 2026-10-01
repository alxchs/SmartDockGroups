using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace SmartDockGroups.Probe;

/// <summary>
/// Drives the real app through the 2026-09-30 batch: shortcut repair at startup, paste in a
/// free-placement group, scrolling, Ctrl+F emphasis, the regrouped menus, duplicate-name
/// refusal, removing a non-empty group, the App Folder sheet's menus, a new group next to the
/// pointer, sharing a single aspect, and importing several shortcuts from Settings.
///
/// It never touches the user's configuration or the installed instance: the app runs with
/// <c>SMARTDOCKGROUPS_DATA_DIR</c> pointing at a scratch folder holding a <b>copy</b> of the
/// user's real config (so every item keeps its real type and shape) and of the icon cache
/// (the upgrade scenario). Only the process this verifier started is stopped at the end.
/// </summary>
internal static class LoteVerifier
{
    private const string FreeGroup = "Livre (teste)";
    private const string ScrollGroup = "Rolagem (teste)";
    private const string NewGroupName = "Criado no teste";
    private const string ImportedGroupName = "Importados (teste)";

    private static readonly string[] BrokenNames =
    [
        "DBeaver Community", "Parametrizacao de acesso ao IGC", "SQL Server Management Studio 22",
        "Microsoft Edge", "NVIDIA App", "WinDirStat"
    ];

    /// <summary>"Instalação limpa": start without the user's icon cache instead of a copy of it ("Upgrade").</summary>
    public static bool CleanCache { get; set; }

    /// <summary>Runs only <see cref="CheckMenuEdges"/>.</summary>
    public static bool OnlyMenuEdges { get; set; }

    /// <summary>Runs only <see cref="CheckDock"/>.</summary>
    public static bool OnlyDock { get; set; }

    /// <summary>Runs only <see cref="CheckDragBetweenGroups"/>.</summary>
    public static bool OnlyDrag { get; set; }

    /// <summary>Runs only the 2026-09-30 evening batch: menu order, keep on screen, navigation, limit, group links.</summary>
    public static bool OnlyBatch { get; set; }

    private const string FullGroup = "Cheio (teste)";

    private static string _data = "";
    private static Process? _current;
    private static string _out = "";
    private static uint _pid;
    private static readonly JsonObject Report = new();

    /// <param name="reuseData">Starts on the data folder a previous run left behind (shortcuts already adopted) instead of a fresh copy.</param>
    public static int Run(string exe, string outDir, bool reuseData = false)
    {
        _out = outDir;
        _data = Path.Combine(outDir, "data");
        if (!reuseData || !Directory.Exists(_data))
        {
            if (Directory.Exists(_data))
            {
                Directory.Delete(_data, recursive: true);
            }

            Directory.CreateDirectory(_data);
            PrepareData();
        }

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.Environment["SMARTDOCKGROUPS_DATA_DIR"] = _data;
        var app = Process.Start(start) ?? throw new InvalidOperationException("app did not start");
        _current = app;
        _pid = (uint)app.Id;
        Log($"started pid {_pid} with data {_data}");

        try
        {
            WaitFor(() => Window(FreeGroup) != IntPtr.Zero && Window("Dev Apps") != IntPtr.Zero, 40, "group windows");
            Thread.Sleep(3000);

            if (OnlyBatch)
            {
                Step("menuOrder", CheckMenuOrder);
                Step("paste", CheckPaste);
                Step("keepOnScreen", CheckKeepOnScreen);
                Step("navigation", CheckNavigation);
                Step("limit", CheckLimit);
                Step("groupLink", CheckGroupLink);
                return 0;
            }

            if (OnlyDrag)
            {
                Step("paste", CheckPaste);
                Step("dragBetweenGroups", CheckDragBetweenGroups);
                return 0;
            }

            if (OnlyDock)
            {
                Step("dock", () => CheckDock(exe, app));
                return 0;
            }

            if (OnlyMenuEdges)
            {
                Step("paste", CheckPaste);
                Step("menuEdges", CheckMenuEdges);
                return 0;
            }

            Step("repair", CheckRepair);
            Step("captures", CaptureUserGroups);
            Step("paste", CheckPaste);
            Step("scroll", CheckScroll);
            Step("search", CheckSearch);
            Step("headerMenu", CheckHeaderMenu);
            Step("itemMenu", CheckItemMenu);
            Step("renameDuplicate", CheckRenameDuplicate);
            Step("removeGroup", CheckRemoveGroup);
            Step("appFolder", CheckAppFolder);
            Step("newGroup", CheckNewGroup);
            Step("shareOpacity", CheckShareOpacity);
            Step("importShortcuts", CheckImportShortcuts);
            Step("taskbarShortcut", CheckTaskbarShortcut);
        }
        finally
        {
            try
            {
                _current?.Kill();
                _current?.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
            }

            File.WriteAllText(Path.Combine(_out, "report-lote.json"), Report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Log($"report -> {Path.Combine(_out, "report-lote.json")}");
        }

        return 0;
    }

    // ───────────────────────────── setup

    private static void PrepareData()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartDockGroups");

        // Read-only copies. The real folder is never written to.
        File.Copy(Path.Combine(real, "config.json"), Path.Combine(_data, "config.json"));
        var realCache = Path.Combine(real, "IconCache");
        if (Directory.Exists(realCache) && !CleanCache)
        {
            var cache = Directory.CreateDirectory(Path.Combine(_data, "IconCache"));
            foreach (var file in Directory.GetFiles(realCache))
            {
                File.Copy(file, Path.Combine(cache.FullName, Path.GetFileName(file)));
            }
        }

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_data, "config.json")))!.AsObject();
        var groups = config["Categories"]!.AsArray();

        // The user's real configuration may be docked right now; every scenario starts undocked.
        config["Dock"] = new JsonObject { ["IsDocked"] = false };

        var scale = SystemScale();
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var left = (area.Left / scale) + 420;
        var top = (area.Top / scale) + 80;

        // Something to paste into, arranged freely — the case where pasted icons stayed invisible.
        groups.Add(new JsonObject
        {
            ["Name"] = FreeGroup, ["IsDesktopGroup"] = true, ["DesktopX"] = left, ["DesktopY"] = top,
            ["DesktopWidth"] = 380, ["DesktopHeight"] = 260, ["IconArrangement"] = "None", ["DisplayMode"] = "Panel",
            ["Items"] = new JsonArray(), ["Categories"] = new JsonArray()
        });

        // Short and crowded, so it must scroll.
        var taskbarItems = groups.First(g => g!["Name"]!.GetValue<string>() == "Taskbar")!["Items"]!.AsArray();
        var crowd = new JsonArray();
        foreach (var item in taskbarItems.Take(14))
        {
            // Without the Taskbar group's own coordinates, so this group lays them out itself.
            var copy = item!.DeepClone().AsObject();
            copy.Remove("DesktopIconX");
            copy.Remove("DesktopIconY");
            crowd.Add(copy);
        }

        groups.Add(new JsonObject
        {
            ["Name"] = ScrollGroup, ["IsDesktopGroup"] = true, ["DesktopX"] = left + 420, ["DesktopY"] = top,
            ["DesktopWidth"] = 300, ["DesktopHeight"] = 190, ["IconArrangement"] = "ByName", ["DisplayMode"] = "Panel",
            ["Items"] = crowd, ["Categories"] = new JsonArray()
        });

        // Optional: the app in another language (SDG_TEST_LANG=de) — menu labels are then looked up by the verifier in that language.
        if (Environment.GetEnvironmentVariable("SDG_TEST_LANG") is { Length: > 0 } language)
        {
            config["Behavior"]!["Language"] = language;
        }

        // Optional: the app itself in the light theme (SDG_TEST_APPTHEME=Light), as on a PC with Windows in light mode.
        if (Environment.GetEnvironmentVariable("SDG_TEST_APPTHEME") is { Length: > 0 } appTheme)
        {
            config["Behavior"]!["AppTheme"] = appTheme;
        }

        // Optional: a group whose own colours clash, to see how its menus read (SDG_TEST_TEXT=#404040).
        if (Environment.GetEnvironmentVariable("SDG_TEST_TEXT") is { Length: > 0 } clash)
        {
            groups.First(g => g!["Name"]!.GetValue<string>() == FreeGroup)!.AsObject()["ThemeOverride"] = new JsonObject
            {
                ["BackgroundColor"] = "#1E1E1E", ["TextColor"] = clash
            };
        }

        // 29 entries already: room for exactly one more.
        var full = new JsonArray();
        for (var i = 1; i <= 29; i++)
        {
            full.Add(new JsonObject { ["Name"] = $"Item {i:D2}", ["Type"] = "Application", ["Target"] = @"C:\Windows\System32\notepad.exe", ["IsDesktopPinned"] = true });
        }

        groups.Add(new JsonObject
        {
            ["Name"] = FullGroup, ["IsDesktopGroup"] = true, ["DesktopX"] = left + 1200, ["DesktopY"] = top + 300,
            ["DesktopWidth"] = 380, ["DesktopHeight"] = 300, ["IconArrangement"] = "ByName", ["DisplayMode"] = "Panel",
            ["Items"] = full, ["Categories"] = new JsonArray()
        });

        // A distinct opacity on Dev Apps, so sharing only the opacity is observable.
        var dev = groups.First(g => g!["Name"]!.GetValue<string>() == "Dev Apps")!.AsObject();
        dev["AreaOpacity"] = 0.55;
        dev["TitleOpacity"] = 0.8;

        // Every group on the primary monitor, side by side, so the run does not depend on
        // which monitors happen to be connected.
        var column = 0;
        foreach (var group in groups)
        {
            var name = group!["Name"]!.GetValue<string>();
            if (name is FreeGroup or ScrollGroup)
            {
                continue;
            }

            group["IsClosed"] = false;
            group["IsCollapsed"] = false;
            group["DesktopX"] = (area.Left / scale) + 40 + (column % 2 * 20);
            group["DesktopY"] = (area.Top / scale) + 380 + (column * 40);
            if (name == "Teams Chat")
            {
                group["DesktopX"] = left + 780;
                group["DesktopY"] = top;
            }

            column++;
        }

        File.WriteAllText(Path.Combine(_data, "config.json"), config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ───────────────────────────── steps

    private static JsonNode CheckRepair()
    {
        var config = ReadConfig();
        var result = new JsonObject();
        foreach (var name in BrokenNames)
        {
            var item = AllItems(config).FirstOrDefault(i => i["Name"]!.GetValue<string>() == name);
            var target = item?["Target"]?.GetValue<string>() ?? "(not found)";
            result[name] = new JsonObject
            {
                ["target"] = target,
                ["owned"] = target.StartsWith(Path.Combine(_data, "Shortcuts"), StringComparison.OrdinalIgnoreCase),
                ["exists"] = File.Exists(target)
            };
        }

        result["backup"] = Directory.GetFiles(_data, "config.json.before-repair-*.bak").Length > 0;
        result["ownedFiles"] = Directory.Exists(Path.Combine(_data, "Shortcuts")) ? Directory.GetFiles(Path.Combine(_data, "Shortcuts")).Length : 0;
        return result;
    }

    private static JsonNode CaptureUserGroups()
    {
        var result = new JsonObject();
        foreach (var name in new[] { "Dev Apps", "Taskbar" })
        {
            var hwnd = Window(name);
            BringTop(hwnd);
            result[name] = Win32.TryCapture(hwnd, Shot($"grupo-{name}"));
            Unpin(hwnd);
        }

        return result;
    }

    private static JsonNode CheckPaste()
    {
        var files = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinDirStat", "WinDirStat.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "NVIDIA Corporation", "NVIDIA App.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Microsoft Edge.lnk")
        }.Where(File.Exists).ToArray();

        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.TryCapture(hwnd, Shot("colar-antes"));

        SetClipboardFiles(files);
        InvokeFromMenu(CanvasPoint(hwnd), "Paste");
        Thread.Sleep(1200);
        Win32.TryCapture(hwnd, Shot("colar-depois"));
        var afterFirst = GroupItems(FreeGroup);

        InvokeFromMenu(CanvasPoint(hwnd), "Paste");
        Thread.Sleep(1000);
        var afterSecond = GroupItems(FreeGroup);
        Unpin(hwnd);

        return new JsonObject
        {
            ["clipboardFiles"] = files.Length,
            ["itemsAfterFirstPaste"] = afterFirst.Count,
            ["itemsAfterSecondPaste"] = afterSecond.Count,
            ["names"] = new JsonArray(afterFirst.Select(i => (JsonNode)i["Name"]!.GetValue<string>()).ToArray()),
            ["allOwned"] = afterFirst.All(i => i["Target"]!.GetValue<string>().StartsWith(Path.Combine(_data, "Shortcuts"), StringComparison.OrdinalIgnoreCase)),
            ["captionsVisibleToUia"] = afterFirst.Count(i => FindText(hwnd, i["Name"]!.GetValue<string>()) is not null)
        };
    }

    private static JsonNode CheckScroll()
    {
        var hwnd = Window(ScrollGroup);
        BringTop(hwnd);
        var pane = AutomationElement.FromHandle(hwnd).FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
        var result = new JsonObject { ["scrollPane"] = pane is not null };
        Win32.TryCapture(hwnd, Shot("rolagem-topo"));
        if (pane?.GetCurrentPattern(ScrollPattern.Pattern) is ScrollPattern scroll)
        {
            result["verticallyScrollable"] = scroll.Current.VerticallyScrollable;
            result["verticalViewSizePercent"] = scroll.Current.VerticalViewSize;
            result["horizontallyScrollable"] = scroll.Current.HorizontallyScrollable;
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 100);
            Thread.Sleep(500);
            result["afterScrollPercent"] = scroll.Current.VerticalScrollPercent;
            Win32.TryCapture(hwnd, Shot("rolagem-fim"));
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
        }

        Unpin(hwnd);
        return result;
    }

    private static JsonNode CheckSearch()
    {
        var hwnd = Window("Taskbar");
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        Win32.LeftClick(rect.Left + 60, rect.Top + 14);
        Thread.Sleep(500);
        Keys.Chord(Keys.VK_CONTROL, (byte)'F');
        Thread.Sleep(400);
        Keys.Type("ed");
        Thread.Sleep(900);

        var popup = Win32.VisibleWindowsOf(_pid).FirstOrDefault(h => h != hwnd && IsPopupNear(h, rect));
        Win32.TryCapture(hwnd, Shot("busca-ctrl-f"));
        if (popup != IntPtr.Zero)
        {
            Win32.TryCapture(popup, Shot("busca-lista"));
        }

        Win32.PressEscape();
        Thread.Sleep(300);
        Unpin(hwnd);
        return new JsonObject { ["resultListShown"] = popup != IntPtr.Zero };
    }

    private static JsonNode CheckHeaderMenu()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        var (popup, menu) = OpenMenu(rect.Left + 80, rect.Top + 14);
        var names = TopLevelNames(menu);
        var view = menu is null ? null : FindItem(menu, "View");
        var viewNames = view is null ? new JsonArray() : SubNames(view);
        var look = menu is null ? null : FindItem(menu, "Appearance");
        var lookNames = look is null ? new JsonArray() : SubNames(look);
        if (popup != IntPtr.Zero)
        {
            Win32.TryCapture(popup, Shot("menu-grupo"));
        }

        CloseMenus();
        Unpin(hwnd);
        return new JsonObject { ["top"] = names, ["view"] = viewNames, ["appearance"] = lookNames };
    }

    private static JsonNode CheckItemMenu()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        var caption = FindText(hwnd, "WinDirStat") ?? throw new InvalidOperationException("caption WinDirStat not found");
        var r = caption.Current.BoundingRectangle;
        var (popup, menu) = OpenMenu((int)(r.Left + (r.Width / 2)), (int)r.Top - 20);
        var names = TopLevelNames(menu);
        if (popup != IntPtr.Zero)
        {
            Win32.TryCapture(popup, Shot("menu-atalho"));
        }

        CloseMenus();
        Unpin(hwnd);
        return new JsonObject { ["top"] = names };
    }

    private static JsonNode CheckRenameDuplicate()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        var caption = FindText(hwnd, "WinDirStat") ?? throw new InvalidOperationException("caption WinDirStat not found");
        var r = caption.Current.BoundingRectangle;
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(new System.Drawing.Point((int)(r.Left + (r.Width / 2)), (int)r.Top - 20), "Rename");

        var prompt = WaitNewWindow(before, 5);
        var result = new JsonObject { ["promptOpened"] = prompt != IntPtr.Zero };
        if (prompt == IntPtr.Zero)
        {
            return result;
        }

        var root = AutomationElement.FromHandle(prompt);
        var box = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue("nvidia app");
        Button(root, "OK")?.Invoke();
        Thread.Sleep(700);

        var stillOpen = Win32.IsWindowVisible(prompt);
        var error = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            is not null
            ? root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().Select(e => e.Current.Name).FirstOrDefault(n => n.StartsWith("There is already", StringComparison.Ordinal))
            : null;

        Win32.CaptureWindowsComposite(hwnd, prompt, Shot("renomear-duplicado"));
        result["stillOpenAfterOk"] = stillOpen;
        result["errorText"] = error;
        Button(root, "Cancel")?.Invoke();
        Thread.Sleep(400);
        result["nameUnchanged"] = GroupItems(FreeGroup).Any(i => i["Name"]!.GetValue<string>() == "WinDirStat");
        Unpin(hwnd);
        return result;
    }

    private static JsonNode CheckRemoveGroup()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(new System.Drawing.Point(rect.Left + 80, rect.Top + 14), "Remove group");

        var box = WaitNewWindow(before, 5);
        var result = new JsonObject { ["dialogOpened"] = box != IntPtr.Zero };
        if (box == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(400);
        var root = AutomationElement.FromHandle(box);
        result["dialogText"] = string.Join(" | ", root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Select(e => e.Current.Name));
        result["focusedButton"] = AutomationElement.FocusedElement?.Current.Name;
        Win32.TryCapture(box, Shot("remover-grupo-aviso"));

        // "Não" by pressing Enter: proves the default answer, not just which button exists.
        Keys.Press(0x0D);
        Thread.Sleep(700);
        result["groupStillThere"] = Window(FreeGroup) != IntPtr.Zero && GroupItems(FreeGroup).Count > 0;
        Unpin(hwnd);
        return result;
    }

    private static JsonNode CheckAppFolder()
    {
        var tile = Window("Teams Chat");
        BringTop(tile);
        Win32.GetWindowRect(tile, out var rect);
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        Win32.LeftClick(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2) - 10);
        var sheet = WaitNewWindow(before, 5);
        var result = new JsonObject { ["sheetOpened"] = sheet != IntPtr.Zero };
        Unpin(tile);
        if (sheet == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(900);
        Win32.TryCapture(sheet, Shot("pasta-folha"));

        var caption = FindText(sheet, "Alexandre");
        if (caption is not null)
        {
            var r = caption.Current.BoundingRectangle;
            var (popup, menu) = OpenMenu((int)(r.Left + (r.Width / 2)), (int)r.Top - 30);
            result["itemMenu"] = TopLevelNames(menu);
            result["sheetOpenWhileItemMenuOpen"] = Win32.IsWindowVisible(sheet);
            if (popup != IntPtr.Zero)
            {
                Win32.TryCapture(popup, Shot("pasta-menu-atalho"));
            }

            CloseMenus(keepSheet: true);
        }

        result["sheetOpenAfterItemMenu"] = Win32.IsWindowVisible(sheet);

        Win32.GetWindowRect(sheet, out var sheetRect);
        var (bgPopup, bgMenu) = OpenMenu(sheetRect.Right - 40, sheetRect.Bottom - 40);
        result["backgroundMenu"] = TopLevelNames(bgMenu);
        if (bgPopup != IntPtr.Zero)
        {
            Win32.TryCapture(bgPopup, Shot("pasta-menu-fundo"));
        }

        CloseMenus(keepSheet: true);
        Win32.PressEscape();
        Thread.Sleep(500);
        return result;
    }

    private static JsonNode CheckNewGroup()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        var click = new System.Drawing.Point(rect.Right - 60, rect.Bottom - 30);
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(click, "New", "Group");
        var prompt = WaitNewWindow(before, 5);
        var result = new JsonObject { ["promptOpened"] = prompt != IntPtr.Zero };
        Unpin(hwnd);
        if (prompt == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(500);
        Win32.GetCursorPos(out var cursor);
        Win32.GetWindowRect(prompt, out var promptRect);
        result["cursor"] = $"{cursor.X},{cursor.Y}";
        result["prompt"] = $"{promptRect.Left},{promptRect.Top},{promptRect.Width}x{promptRect.Height}";
        result["promptDistanceFromCursorPx"] = DistanceToRect(cursor, promptRect);

        var root = AutomationElement.FromHandle(prompt);
        var box = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(NewGroupName);
        Button(root, "OK")?.Invoke();

        WaitFor(() => Window(NewGroupName) != IntPtr.Zero, 5, "new group window");
        var created = Window(NewGroupName);
        Win32.GetWindowRect(created, out var groupRect);
        result["newGroup"] = $"{groupRect.Left},{groupRect.Top},{groupRect.Width}x{groupRect.Height}";
        result["groupDistanceFromCursorPx"] = DistanceToRect(cursor, groupRect);
        return result;
    }

    private static JsonNode CheckShareOpacity()
    {
        string Look(JsonNode group) =>
            $"area={group["AreaOpacity"]?.GetValue<double>() ?? 1} title={group["TitleOpacity"]?.GetValue<double>() ?? 1} " +
            $"bg={group["ThemeOverride"]?["BackgroundColor"]?.GetValue<string>() ?? "(padrão)"} img={group["DesktopBackgroundImagePath"]?.GetValue<string>() ?? "-"}";

        var before = Groups(ReadConfig()).ToDictionary(g => g["Name"]!.GetValue<string>(), Look);

        var hwnd = Window("Dev Apps");
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        InvokeFromMenu(new System.Drawing.Point(rect.Left + 60, rect.Top + 14),
            "Appearance", "Share with the other groups", "Apply to all groups now", "Opacity only");
        Thread.Sleep(1500);
        Unpin(Window("Dev Apps"));

        var after = Groups(ReadConfig()).ToDictionary(g => g["Name"]!.GetValue<string>(), Look);
        var result = new JsonObject();
        foreach (var (name, look) in after)
        {
            result[name] = $"{(before.TryGetValue(name, out var was) ? was : "(novo)")}  ->  {look}";
        }

        return result;
    }

    private static JsonNode CheckImportShortcuts()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinDirStat");
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(new System.Drawing.Point(rect.Left + 80, rect.Top + 14), "Settings");
        Unpin(hwnd);
        var settings = WaitNewWindow(before, 8);
        var result = new JsonObject { ["settingsOpened"] = settings != IntPtr.Zero };
        if (settings == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(800);
        Win32.TryCapture(settings, Shot("configuracoes"));
        var settingsRoot = AutomationElement.FromHandle(settings);
        before = Win32.VisibleWindowsOf(_pid).ToHashSet();

        // The file dialog is modal: Invoke would block until it closes, so press the button from a worker.
        var importButton = Button(settingsRoot, "Import shortcuts");
        _ = Task.Run(() => importButton?.Invoke());
        var dialog = WaitNewWindow(before, 8);
        result["fileDialogOpened"] = dialog != IntPtr.Zero;
        if (dialog == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(1200);
        var dialogRoot = AutomationElement.FromHandle(dialog);
        var fileName = dialogRoot.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1148")));
        ((ValuePattern)fileName.GetCurrentPattern(ValuePattern.Pattern)).SetValue(folder);
        Keys.Press(0x0D);
        Thread.Sleep(1200);
        ((ValuePattern)fileName.GetCurrentPattern(ValuePattern.Pattern)).SetValue("\"WinDirStat.lnk\" \"Uninstall WinDirStat.lnk\"");
        before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        Keys.Press(0x0D);

        var picker = WaitNewWindow(before, 8, exclude: dialog);
        result["pickerOpened"] = picker != IntPtr.Zero;
        if (picker == IntPtr.Zero)
        {
            return result;
        }

        Thread.Sleep(600);
        var pickerRoot = AutomationElement.FromHandle(picker);
        var combo = pickerRoot.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(400);
        var options = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().ToList();
        result["pickerOptions"] = options.Count;
        ((SelectionItemPattern)options[^1].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Thread.Sleep(400);
        var nameBox = pickerRoot.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            .Cast<AutomationElement>().Last();
        ((ValuePattern)nameBox.GetCurrentPattern(ValuePattern.Pattern)).SetValue(ImportedGroupName);
        Win32.TryCapture(picker, Shot("importar-escolher-grupo"));
        Button(pickerRoot, "OK")?.Invoke();

        WaitFor(() => Window(ImportedGroupName) != IntPtr.Zero, 8, "imported group window");
        Thread.Sleep(1000);
        var imported = Window(ImportedGroupName);
        BringTop(imported);
        Win32.TryCapture(imported, Shot("importar-resultado"));
        Unpin(imported);
        result["importedItems"] = new JsonArray(GroupItems(ImportedGroupName).Select(i => (JsonNode)i["Name"]!.GetValue<string>()).ToArray());
        Button(settingsRoot, "Close")?.Invoke();
        return result;
    }

    /// <summary>
    /// "Create taskbar shortcut" on the group made by <see cref="CheckNewGroup"/>: the file lands in the
    /// Start-menu folder (the isolated instance's own StartMenu folder here), carries the group's own
    /// AppUserModelID, follows a rename and disappears with the group.
    /// </summary>
    private static JsonNode CheckTaskbarShortcut()
    {
        var folder = Path.Combine(_data, "StartMenu");
        var hwnd = Window(NewGroupName);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var rect);
        var header = new System.Drawing.Point(rect.Left + 60, rect.Top + 14);
        var explorersBefore = Process.GetProcessesByName("explorer").Select(p => p.Id).ToHashSet();
        InvokeFromMenu(header, "Create taskbar shortcut");
        Thread.Sleep(2500);
        var created = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.lnk").Select(Path.GetFileName).ToArray() : [];
        var result = new JsonObject
        {
            ["filesAfterCreate"] = new JsonArray(created.Select(f => (JsonNode)f!).ToArray()),
            ["appUserModelId"] = created.Length == 1 ? ReadAppUserModelId(Path.Combine(folder, created[0]!)) : null
        };

        CloseRevealWindow();

        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(header, "Rename");
        var prompt = WaitNewWindow(before, 5);
        if (prompt != IntPtr.Zero)
        {
            var root = AutomationElement.FromHandle(prompt);
            var box = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Renomeado no teste");
            Button(root, "OK")?.Invoke();
            Thread.Sleep(1200);
        }

        result["filesAfterRename"] = new JsonArray(Directory.GetFiles(folder, "*.lnk").Select(f => (JsonNode)Path.GetFileName(f)!).ToArray());

        hwnd = Window("Renomeado no teste");
        before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(header, "Remove group");
        var box2 = WaitNewWindow(before, 5);
        if (box2 != IntPtr.Zero)
        {
            Button(AutomationElement.FromHandle(box2), "Yes")?.Invoke();
            Thread.Sleep(1200);
        }

        result["groupWindowGone"] = Window("Renomeado no teste") == IntPtr.Zero;
        result["filesAfterRemove"] = new JsonArray(Directory.GetFiles(folder, "*.lnk").Select(f => (JsonNode)Path.GetFileName(f)!).ToArray());
        return result;
    }

    /// <summary>The Explorer window "Create taskbar shortcut" opens on the isolated StartMenu folder.</summary>
    private static void CloseRevealWindow()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(type)!;
        foreach (var window in shell.Windows())
        {
            try
            {
                string location = window.LocationURL;
                if (location.Contains("StartMenu", StringComparison.OrdinalIgnoreCase))
                {
                    window.Quit();
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static string? ReadAppUserModelId(string shortcutPath)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null)
        {
            return null;
        }

        dynamic shell = Activator.CreateInstance(type)!;
        dynamic item = shell.Namespace(Path.GetDirectoryName(shortcutPath)).ParseName(Path.GetFileName(shortcutPath));
        return item.ExtendedProperty("System.AppUserModel.ID") as string;
    }

    /// <summary>
    /// Puts "Livre (teste)" against each corner of each monitor's work area (physical pixels, set
    /// with SetWindowPos) and records where the header menu, a submenu, a sub-submenu and the "…"
    /// button's menu open — each checked against the work area of the monitor the click was on.
    /// </summary>
    private static JsonNode CheckMenuEdges()
    {
        var hwnd = Window(FreeGroup);
        var result = new JsonArray();
        var screens = System.Windows.Forms.Screen.AllScreens;
        foreach (var screen in screens)
        {
            var wa = screen.WorkingArea;
            foreach (var corner in new[] { "TL", "TR", "BL", "BR" })
            {
                Win32.GetWindowRect(hwnd, out var r0);
                var x = corner.EndsWith('L') ? wa.Left : wa.Right - r0.Width;
                var y = corner.StartsWith('T') ? wa.Top : wa.Bottom - r0.Height;
                Win32.SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
                Thread.Sleep(900);
                Win32.GetWindowRect(hwnd, out var rect);
                var label = $"mon{Array.IndexOf(screens, screen) + 1}{(screen.Primary ? "p" : "")}-{corner}";
                var entry = new JsonObject
                {
                    ["case"] = label,
                    ["workArea"] = $"{wa.Left},{wa.Top},{wa.Right},{wa.Bottom}",
                    ["window"] = $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}"
                };

                // Header, on the side facing away from the header buttons.
                var click = new System.Drawing.Point(corner.EndsWith('L') ? rect.Left + 60 : rect.Right - 170, rect.Top + 14);
                var (popup, menu) = OpenMenu(click.X, click.Y);
                entry["headerMenu"] = Describe(popup, wa);
                if (menu is not null && FindItem(menu, "Appearance") is { } look)
                {
                    ((ExpandCollapsePattern)look.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    Thread.Sleep(600);
                    var sub = NewestPopup(popup);
                    entry["submenu"] = Describe(sub, wa);
                    if (FindItem(look, "Share with") is { } share)
                    {
                        ((ExpandCollapsePattern)share.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                        Thread.Sleep(600);
                        entry["subSubmenu"] = Describe(NewestPopup(popup, sub), wa);
                    }

                    if (sub != IntPtr.Zero)
                    {
                        Win32.TryCapture(popup, Shot($"borda-{label}-menu"));
                    }
                }

                CloseMenus();

                // An icon's menu, and its tall "Group ▸" submenu.
                if (FindText(hwnd, "WinDirStat") is { } caption)
                {
                    var cr = caption.Current.BoundingRectangle;
                    var (itemPopup, itemMenu) = OpenMenu((int)(cr.Left + (cr.Width / 2)), (int)cr.Top - 20);
                    entry["itemMenu"] = Describe(itemPopup, wa);
                    if (itemMenu is not null && FindItem(itemMenu, "Group") is { } groupItem)
                    {
                        ((ExpandCollapsePattern)groupItem.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                        Thread.Sleep(600);
                        entry["itemGroupSubmenu"] = Describe(NewestPopup(itemPopup), wa);
                    }

                    CloseMenus();
                }

                // The "…" button: third from the right in the header.
                var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
                var dotsX = rect.Right - (int)((rect.Right - rect.Left) * 0.128);
                Win32.LeftClick(dotsX, rect.Top + 14);
                var dotsPopup = WaitNewWindow(before, 3);
                entry["moreButtonMenu"] = Describe(dotsPopup, wa);
                CloseMenus();
                result.Add(entry);
                Log(entry.ToJsonString());
            }
        }

        // Straddling the border between the first two monitors: a click on each side.
        if (screens.Length > 1)
        {
            var a = screens[0].WorkingArea;
            var b = screens[1].WorkingArea;
            var leftSide = a.Right <= b.Left ? a : b;
            var rightSide = a.Right <= b.Left ? b : a;
            Win32.GetWindowRect(hwnd, out var r0);
            Win32.SetWindowPos(hwnd, new IntPtr(-1), leftSide.Right - (r0.Width / 2), Math.Max(leftSide.Top, rightSide.Top) + 200, 0, 0, 0x0001 | 0x0010);
            Thread.Sleep(900);
            Win32.GetWindowRect(hwnd, out var rect);
            foreach (var (side, x, area) in new[] { ("left part", leftSide.Right - 40, leftSide), ("right part", rightSide.Left + 40, rightSide) })
            {
                var (popup, menu) = OpenMenu(x, rect.Top + 14);
                var entry = new JsonObject { ["case"] = $"straddle-{side}", ["window"] = $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}", ["headerMenu"] = Describe(popup, area) };
                if (menu is not null && FindItem(menu, "Appearance") is { } look)
                {
                    ((ExpandCollapsePattern)look.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    Thread.Sleep(600);
                    entry["submenu"] = Describe(NewestPopup(popup), area);
                }

                CloseMenus();
                result.Add(entry);
                Log(entry.ToJsonString());
            }
        }

        // The App Folder tile against the bottom-right corner of the primary monitor.
        var tile = Window("Teams Chat");
        var primary = screens.First(sc => sc.Primary).WorkingArea;
        Win32.GetWindowRect(tile, out var t0);
        Win32.SetWindowPos(tile, new IntPtr(-1), primary.Right - t0.Width, primary.Bottom - t0.Height, 0, 0, 0x0001 | 0x0010);
        Thread.Sleep(900);
        Win32.GetWindowRect(tile, out var trect);
        var (tilePopup, _) = OpenMenu(trect.Left + (trect.Width / 2), trect.Top + (trect.Height / 2));
        var tileEntry = new JsonObject { ["case"] = "appfolder-tile-BR", ["tileMenu"] = Describe(tilePopup, primary) };
        CloseMenus();
        result.Add(tileEntry);
        Log(tileEntry.ToJsonString());
        Unpin(tile);

        Unpin(hwnd);
        return result;
    }

    private static IntPtr NewestPopup(params IntPtr[] known)
    {
        return Win32.VisibleWindowsOf(_pid)
            .Where(h => !known.Contains(h) && Win32.TitleOf(h).Length == 0
                && Win32.GetWindowRect(h, out var r) && r.Width > 1 && r.Left > -5000)
            .FirstOrDefault();
    }

    private static JsonNode Describe(IntPtr popup, System.Drawing.Rectangle workArea)
    {
        if (popup == IntPtr.Zero || !Win32.GetWindowRect(popup, out var r))
        {
            return "(none)";
        }

        var inside = r.Left >= workArea.Left && r.Top >= workArea.Top && r.Right <= workArea.Right && r.Bottom <= workArea.Bottom;
        return $"{(inside ? "OK " : "OUT")} {r.Left},{r.Top},{r.Right},{r.Bottom}";
    }

    private static readonly string[] DockGroups = ["Taskbar", "Dev Apps", "Teams Chat", FreeGroup, ScrollGroup];

    private static JsonObject Rects()
    {
        var o = new JsonObject();
        foreach (var name in DockGroups)
        {
            var h = Window(name);
            if (h != IntPtr.Zero && Win32.GetWindowRect(h, out var r))
            {
                o[name] = $"{r.Left},{r.Top},{r.Width}x{r.Height}";
            }
        }

        return o;
    }

    private static List<(string Name, Win32.Rect Rect)> StackInOrder()
    {
        var config = ReadConfig();
        var order = config["Dock"]?["Order"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? [];
        var byId = Groups(config).ToDictionary(g => g["Id"]?.GetValue<string>() ?? "", g => g["Name"]!.GetValue<string>());
        var list = new List<(string, Win32.Rect)>();
        foreach (var id in order)
        {
            if (byId.TryGetValue(id, out var name) && Window(name) is var h && h != IntPtr.Zero && Win32.GetWindowRect(h, out var r))
            {
                list.Add((name, r));
            }
        }

        return list;
    }

    /// <summary>Contiguous (±3 px), same left and width, and which ones are taller than a title bar.</summary>
    private static JsonObject DescribeStack(string label)
    {
        var stack = StackInOrder();
        var contiguous = stack.Zip(stack.Skip(1)).All(p => Math.Abs(p.Second.Rect.Top - p.First.Rect.Bottom) <= 3);
        var sameColumn = stack.All(x => Math.Abs(x.Rect.Left - stack[0].Rect.Left) <= 2 && Math.Abs(x.Rect.Width - stack[0].Rect.Width) <= 2);
        var minHeight = stack.Min(x => x.Rect.Height);
        var expanded = stack.Where(x => x.Rect.Height > minHeight + 20).Select(x => (JsonNode)x.Name).ToArray();
        var o = new JsonObject
        {
            ["members"] = stack.Count,
            ["order"] = new JsonArray(stack.Select(x => (JsonNode)$"{x.Name} {x.Rect.Left},{x.Rect.Top} {x.Rect.Width}x{x.Rect.Height}").ToArray()),
            ["contiguous"] = contiguous,
            ["sameColumn"] = sameColumn,
            ["expanded"] = new JsonArray(expanded)
        };

        var union = new System.Drawing.Rectangle(stack[0].Rect.Left - 8, stack[0].Rect.Top - 8, stack[0].Rect.Width + 16, stack[^1].Rect.Bottom - stack[0].Rect.Top + 16);
        Win32.CaptureRect(union, Shot($"dock-{label}"));
        return o;
    }

    private static void HeaderCommand(string group, params string[] path)
    {
        var h = Window(group);
        BringTop(h);
        Thread.Sleep(300);
        Win32.GetWindowRect(h, out var r);
        InvokeFromMenu(new System.Drawing.Point(r.Left + 60, r.Top + 12), path);
        Thread.Sleep(1200);
        foreach (var name in DockGroups)
        {
            if (Window(name) is var other && other != IntPtr.Zero)
            {
                Unpin(other);
            }
        }
    }

    private static Process LaunchWith(string exe, string? action)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.Environment["SMARTDOCKGROUPS_DATA_DIR"] = _data;
        if (action is not null)
        {
            start.Arguments = "--desktop-action=" + action;
        }

        return Process.Start(start)!;
    }

    private static void Restart(string exe, string? action)
    {
        _current?.Kill();
        _current?.WaitForExit(5000);
        Thread.Sleep(800);
        _current = LaunchWith(exe, action);
        _pid = (uint)_current.Id;
        WaitFor(() => Window("Dev Apps") != IntPtr.Zero, 30, "groups after restart");
        Thread.Sleep(3500);
    }

    private static JsonNode CheckDock(string exe, Process _)
    {
        var result = new JsonObject();
        var before = Rects();
        var beforeConfig = Groups(ReadConfig()).ToDictionary(
            g => g["Name"]!.GetValue<string>(),
            g => $"{g["DesktopX"]} {g["DesktopY"]} {g["DesktopWidth"]}x{g["DesktopHeight"]} collapsed={g["IsCollapsed"]} mode={g["DisplayMode"]}");
        result["before"] = before;

        HeaderCommand(FreeGroup, "Dock all groups");
        Thread.Sleep(800);
        result["docked"] = DescribeStack("1-acoplado");

        // The View > Style item must stay usable while docked: it flips the style the group has after undock.
        string SavedMode(string group)
        {
            var id = Groups(ReadConfig()).First(g => g["Name"]!.GetValue<string>() == group)["Id"]!.GetValue<string>();
            return ReadConfig()["Dock"]!["Saved"]!.AsArray().First(p => p!["Id"]!.GetValue<string>() == id)!["DisplayMode"]!.GetValue<string>();
        }

        var styleBefore = SavedMode("Dev Apps");
        HeaderCommand("Dev Apps", "View", styleBefore == "AppFolder" ? "Style: panel" : "Style: app folder");
        var styleFlipped = SavedMode("Dev Apps");
        HeaderCommand("Dev Apps", "View", styleFlipped == "AppFolder" ? "Style: panel" : "Style: app folder");
        result["styleWhileDocked"] = new JsonObject
        {
            ["savedBefore"] = styleBefore,
            ["afterFirstClick"] = styleFlipped,
            ["afterSecondClick"] = SavedMode("Dev Apps"),
            ["stillBarInStack"] = Window("Dev Apps") != IntPtr.Zero && Win32.GetWindowRect(Window("Dev Apps"), out var barRect) && barRect.Height < 90
        };

        var stack = StackInOrder();
        var second = stack[1].Name;
        var third = stack[2].Name;
        HeaderCommand(second, "Expand");
        result["expandSecond"] = DescribeStack("2-expande-segundo");

        HeaderCommand(third, "Expand");
        result["expandThird"] = DescribeStack("3-expande-terceiro");

        HeaderCommand(third, "Collapse");
        result["collapseThird"] = DescribeStack("4-fecha-terceiro");

        // Drag the first title bar 300 px right and 150 px down.
        var firstHandle = Window(stack[0].Name);
        BringTop(firstHandle);
        Thread.Sleep(300);
        Win32.GetWindowRect(firstHandle, out var fr);
        var grab = new System.Drawing.Point(fr.Left + 60, fr.Top + 12);
        Win32.SetCursorPos(grab.X, grab.Y);
        Thread.Sleep(200);
        Win32.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        for (var i = 1; i <= 15; i++)
        {
            Win32.SetCursorPos(grab.X + (20 * i), grab.Y + (10 * i));
            Thread.Sleep(30);
        }

        Win32.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(1500);
        result["dragged"] = DescribeStack("5-arrastado");

        // Drag a MIDDLE title (not the first): everyone else must move with it while the button is down.
        var mid = StackInOrder();
        var middleName = mid[2].Name;
        var middleHandle = Window(middleName);
        BringTop(middleHandle);
        Thread.Sleep(300);
        Win32.GetWindowRect(middleHandle, out var mr);
        var midGrab = new System.Drawing.Point(mr.Left + 60, mr.Top + 12);
        var beforeMid = StackInOrder().Select(x => (x.Name, x.Rect.Left, x.Rect.Top)).ToList();
        Win32.SetCursorPos(midGrab.X, midGrab.Y);
        Thread.Sleep(200);
        Win32.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        for (var i = 1; i <= 12; i++)
        {
            Win32.SetCursorPos(midGrab.X - (15 * i), midGrab.Y + (8 * i));
            Thread.Sleep(40);
        }

        Thread.Sleep(300);
        var duringMid = StackInOrder().Select(x => (x.Name, x.Rect.Left, x.Rect.Top)).ToList();
        Win32.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(1200);
        var afterMid = StackInOrder().Select(x => (x.Name, x.Rect.Left, x.Rect.Top)).ToList();
        result["dragMiddle"] = new JsonObject
        {
            ["dragged"] = middleName,
            ["deltaOfEachWhileHeld"] = new JsonArray(beforeMid.Zip(duringMid).Select(p => (JsonNode)$"{p.First.Name}: {p.Second.Left - p.First.Left},{p.Second.Top - p.First.Top}").ToArray()),
            ["allMovedTogetherWhileHeld"] = beforeMid.Zip(duringMid).Select(p => (p.Second.Left - p.First.Left, p.Second.Top - p.First.Top)).ToList() is var d && d.All(x => Math.Abs(x.Item1 - d[0].Item1) <= 3 && Math.Abs(x.Item2 - d[0].Item2) <= 3),
            ["finalDeltaOfEach"] = new JsonArray(beforeMid.Zip(afterMid).Select(p => (JsonNode)$"{p.First.Name}: {p.Second.Left - p.First.Left},{p.Second.Top - p.First.Top}").ToArray()),
            ["contiguousAfter"] = DescribeStack("5b-meio-arrastado")["contiguous"]!.GetValue<bool>()
        };

        Restart(exe, null);
        result["afterRestart"] = DescribeStack("6-reaberto");

        HeaderCommand(FreeGroup, "Undock groups");
        Thread.Sleep(1500);
        var after = Rects();
        result["afterUndock"] = after;
        var afterConfig = Groups(ReadConfig()).ToDictionary(
            g => g["Name"]!.GetValue<string>(),
            g => $"{g["DesktopX"]} {g["DesktopY"]} {g["DesktopWidth"]}x{g["DesktopHeight"]} collapsed={g["IsCollapsed"]} mode={g["DisplayMode"]}");
        result["configRestored"] = new JsonObject(beforeConfig.Select(kv => new KeyValuePair<string, JsonNode?>(
            kv.Key, (afterConfig.TryGetValue(kv.Key, out var now) && now == kv.Value ? "SAME " : "DIFF ") + kv.Value + (now == kv.Value ? "" : "  ->  " + now))));
        result["windowsRestored"] = before.All(kv => after[kv.Key]?.GetValue<string>() == kv.Value!.GetValue<string>());

        // Not in memory: the desktop menu's command line starts the app and carries the command out.
        _current?.Kill();
        _current?.WaitForExit(5000);
        Thread.Sleep(800);
        Restart(exe, "dock-all");
        result["coldDockAll"] = DescribeStack("7-acoplar-com-app-fechado");
        result["coldDockConfig"] = ReadConfig()["Dock"]?["IsDocked"]?.GetValue<bool>();

        // Running: a second launch hands the command over and exits.
        var forwarded = LaunchWith(exe, "undock-all");
        forwarded.WaitForExit(8000);
        Thread.Sleep(1500);
        result["forwardedUndock"] = new JsonObject
        {
            ["secondProcessExited"] = forwarded.HasExited,
            ["isDocked"] = ReadConfig()["Dock"]?["IsDocked"]?.GetValue<bool>(),
            ["windowsRestored"] = Rects().ToJsonString() == after.ToJsonString()
        };

        foreach (var action in new[] { "bring-all-to-front", "send-all-to-back", "open-all-groups", "toggle-collapse-all" })
        {
            _current?.Kill();
            _current?.WaitForExit(5000);
            Thread.Sleep(800);
            Restart(exe, action);
            result["cold-" + action] = new JsonObject
            {
                ["running"] = !_current!.HasExited,
                ["groupWindows"] = DockGroups.Count(name => Window(name) != IntPtr.Zero)
            };
        }

        return result;
    }

    /// <summary>
    /// Real mouse: press on an icon of "Rolagem (teste)" (arranged by name), move across the
    /// screen in steps, release over an empty spot of "Livre (teste)" (free placement). Checks the
    /// icon left the source, joined the target, and sits under the release point — one frame per
    /// step is saved for a GIF.
    /// </summary>
    private static JsonNode CheckDragBetweenGroups()
    {
        var source = Window(ScrollGroup);
        var target = Window(FreeGroup);
        BringTop(source);
        BringTop(target);
        Thread.Sleep(500);

        var caption = FindText(source, "Google Chrome") ?? FindText(source, "Telegram") ?? throw new InvalidOperationException("no icon caption in source");
        var name = caption.Current.Name;
        var r = caption.Current.BoundingRectangle;
        var from = new System.Drawing.Point((int)(r.Left + (r.Width / 2)), (int)(r.Top - 30));

        Win32.GetWindowRect(target, out var tr);
        var drop = new System.Drawing.Point(tr.Left + (int)(tr.Width * 0.72), tr.Top + (int)(tr.Height * 0.72));

        var sourceBefore = GroupItems(ScrollGroup).Count;
        var targetBefore = GroupItems(FreeGroup).Count;
        var frames = Path.Combine(_out, "drag-frames");
        Directory.CreateDirectory(frames);
        var union = System.Drawing.Rectangle.Union(
            new System.Drawing.Rectangle(0, 0, 1, 1),
            new System.Drawing.Rectangle(Math.Min(from.X, tr.Left) - 60, Math.Min(from.Y, tr.Top) - 60,
                Math.Abs(drop.X - from.X) + 500, Math.Abs(drop.Y - from.Y) + 300));

        Win32.SetCursorPos(from.X, from.Y);
        Thread.Sleep(300);
        Win32.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(150);
        const int steps = 24;
        for (var i = 1; i <= steps; i++)
        {
            Win32.SetCursorPos(from.X + ((drop.X - from.X) * i / steps), from.Y + ((drop.Y - from.Y) * i / steps));
            Thread.Sleep(45);
            if (i % 2 == 0)
            {
                Win32.CaptureRect(union, Path.Combine(frames, $"f{i:D2}.png"));
            }
        }

        Thread.Sleep(200);
        Win32.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(1500);
        Win32.CaptureRect(union, Path.Combine(frames, "f99.png"));

        var moved = GroupItems(FreeGroup).FirstOrDefault(i => i["Name"]!.GetValue<string>() == name);
        Win32.GetWindowRect(target, out var tr2);
        var result = new JsonObject
        {
            ["icon"] = name,
            ["sourceCount"] = $"{sourceBefore} -> {GroupItems(ScrollGroup).Count}",
            ["targetCount"] = $"{targetBefore} -> {GroupItems(FreeGroup).Count}",
            ["joinedTarget"] = moved is not null,
            ["leftSource"] = GroupItems(ScrollGroup).All(i => i["Name"]!.GetValue<string>() != name)
        };

        if (moved is not null)
        {
            var scale = SystemScale();
            var x = moved["DesktopIconX"]!.GetValue<double>();
            var y = moved["DesktopIconY"]!.GetValue<double>();
            var cap = FindText(target, name)?.Current.BoundingRectangle;
            result["savedPosition"] = $"{x:0},{y:0}";
            if (cap is { } c)
            {
                var cx = (int)(c.Left + (c.Width / 2));
                var cy = (int)(c.Top + (c.Height / 2));
                result["dropPoint"] = $"{drop.X},{drop.Y}";
                result["iconCaptionCentre"] = $"{cx},{cy}";
                result["distancePx"] = Math.Round(Math.Sqrt(Math.Pow(cx - drop.X, 2) + Math.Pow(cy - drop.Y, 2)));
            }
        }

        Unpin(source);
        Unpin(target);
        return result;
    }

    private static string? HeaderTitle(string group)
    {
        var h = Window(group);
        return AutomationElement.FromHandle(h)
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .Select(e => e.Current.Name)
            .FirstOrDefault(n => n == group || n.StartsWith(group + " [", StringComparison.Ordinal));
    }

    private static JsonNode CheckMenuOrder()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var r);
        var (popup, menu) = OpenMenu(r.Left + 80, r.Top + 14);
        var top = TopLevelNames(menu);
        var result = new JsonObject { ["top"] = top };
        if (menu is not null && FindItem(menu, "New") is { } fresh)
        {
            result["newMenu"] = SubNames(fresh);
        }

        if (popup != IntPtr.Zero)
        {
            Win32.TryCapture(popup, Shot("lote2-menu-grupo"));
        }

        CloseMenus();
        Unpin(hwnd);
        return result;
    }

    private static bool InsideCursorMonitor(Win32.Rect rect)
    {
        Win32.GetCursorPos(out var cursor);
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        return rect.Left >= wa.Left && rect.Top >= wa.Top && rect.Right <= wa.Right && rect.Bottom <= wa.Bottom;
    }

    /// <summary>Real drags that end half outside each side, and across the border between two monitors.</summary>
    private static JsonNode CheckKeepOnScreen()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        var screens = System.Windows.Forms.Screen.AllScreens;
        var cases = new JsonArray();

        void Drag(string label, System.Drawing.Rectangle start, int grabX, Func<Win32.Rect, (int Left, int Top)> destination)
        {
            Win32.SetWindowPos(hwnd, new IntPtr(-1), start.Left, start.Top, 0, 0, 0x0001 | 0x0010);
            Thread.Sleep(700);
            Win32.GetWindowRect(hwnd, out var r);
            var from = new System.Drawing.Point(r.Left + grabX, r.Top + 12);
            var (left, top) = destination(r);
            var to = new System.Drawing.Point(left + grabX, top + 12);
            Win32.SetCursorPos(from.X, from.Y);
            Thread.Sleep(250);
            Win32.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            for (var i = 1; i <= 20; i++)
            {
                Win32.SetCursorPos(from.X + ((to.X - from.X) * i / 20), from.Y + ((to.Y - from.Y) * i / 20));
                Thread.Sleep(25);
            }

            Win32.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(1300);
            Win32.GetWindowRect(hwnd, out var after);
            Win32.GetCursorPos(out var cursor);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
            cases.Add(new JsonObject
            {
                ["case"] = label,
                ["released"] = $"{to.X},{to.Y} on {System.Array.IndexOf(screens, screens.First(sc => sc.DeviceName == screen.DeviceName)) + 1}",
                ["windowWouldBe"] = $"{left},{top}",
                ["windowNow"] = $"{after.Left},{after.Top},{after.Right},{after.Bottom}",
                ["insideMouseMonitor"] = InsideCursorMonitor(after),
                ["savedInConfig"] = ConfigPosition(FreeGroup)
            });
            Log(cases[^1]!.ToJsonString());
        }

        var primary = screens.First(sc => sc.Primary).WorkingArea;
        var last = screens.OrderBy(sc => sc.Bounds.Right).Last().WorkingArea;
        var mid = new System.Drawing.Rectangle(primary.Left + 800, primary.Top + 500, 0, 0);
        Win32.GetWindowRect(hwnd, out var size);
        var w = size.Width;
        var h = size.Height;

        Drag("left half out (monitor 1)", mid, w - 210, r => (primary.Left - (w / 2), r.Top));
        Drag("top out", mid, 60, r => (r.Left, primary.Top - 60));
        Drag("bottom out", mid, 60, r => (r.Left, primary.Bottom - 60));
        Drag("right half out (last monitor)", new System.Drawing.Rectangle(last.Left + 600, last.Top + 300, 0, 0), 60, r => (last.Right - (w / 2), r.Top));

        if (screens.Length > 1)
        {
            var left = screens.OrderBy(sc => sc.Bounds.Left).First().WorkingArea;
            var right = screens.OrderBy(sc => sc.Bounds.Left).Skip(1).First().WorkingArea;
            var border = left.Right;
            Drag("across the border, mouse on the left monitor", new System.Drawing.Rectangle(left.Left + 600, left.Top + 300, 0, 0), 60, r => (border - (w / 2), left.Top + 300));
            Drag("across the border, mouse on the right monitor", new System.Drawing.Rectangle(left.Left + 600, left.Top + 300, 0, 0), w - 210, r => (border - (w / 2), left.Top + 300));
        }

        Win32.SetWindowPos(hwnd, new IntPtr(-1), mid.Left, mid.Top, 0, 0, 0x0001 | 0x0010);
        Unpin(hwnd);
        return cases;
    }

    private static string ConfigPosition(string group)
    {
        var g = Groups(ReadConfig()).First(x => x["Name"]!.GetValue<string>() == group);
        return $"{g["DesktopX"]:0},{g["DesktopY"]:0} (dip)";
    }

    /// <summary>Home/End/arrows/Page keys on an arranged group, against reading order computed here from the saved positions.</summary>
    private static JsonNode CheckNavigation()
    {
        var hwnd = Window(ScrollGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var r);
        Win32.LeftClick(r.Left + 80, r.Top + 12);
        Thread.Sleep(400);
        Win32.keybd_event(0x74, 0, 0, UIntPtr.Zero); // F5: lay the arranged group out and save the positions
        Win32.keybd_event(0x74, 0, 2, UIntPtr.Zero);
        Thread.Sleep(1200);

        // Expected order, from the positions in the config: rows of the same Y, left to right.
        var items = GroupItems(ScrollGroup)
            .Select(i => (Name: i["Name"]!.GetValue<string>(), X: i["DesktopIconX"]!.GetValue<double>(), Y: i["DesktopIconY"]!.GetValue<double>()))
            .OrderBy(i => i.Y).ThenBy(i => i.X).ToList();
        var rows = items.GroupBy(i => Math.Round(i.Y)).OrderBy(g => g.Key).Select(g => g.OrderBy(i => i.X).Select(i => i.Name).ToList()).ToList();
        var flat = rows.SelectMany(x => x).ToList();

        var steps = new JsonArray();
        bool Step1(string key, Action press, string expected)
        {
            press();
            Thread.Sleep(350);
            var title = HeaderTitle(ScrollGroup);
            var ok = title == $"{ScrollGroup} [{expected}]";
            steps.Add($"{(ok ? "OK " : "BAD")} {key}: title='{title}' expected='[{expected}]'");
            return ok;
        }

        const byte Home = 0x24, End = 0x23, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, PgDn = 0x22;
        void K(byte vk) { Win32.keybd_event(vk, 0, 0, UIntPtr.Zero); Win32.keybd_event(vk, 0, 2, UIntPtr.Zero); }

        var cols = rows[0].Count;
        Step1("Home", () => K(Home), flat[0]);
        Step1("Right", () => K(Right), flat[1]);
        Step1("End", () => K(End), flat[^1]);
        Step1("Right at the last (wraps to first)", () => K(Right), flat[0]);
        Step1("Left at the first (wraps to last)", () => K(Left), flat[^1]);
        Step1("Home again", () => K(Home), flat[0]);
        Step1("Down", () => K(Down), rows[1][0]);
        Step1("Up", () => K(Up), flat[0]);
        Step1("Up at the first row (wraps to last row)", () => K(Up), rows[^1][0]);
        Step1("Down at the last row (wraps to first row)", () => K(Down), rows[0][0]);
        Step1("PageDown", () => K(PgDn), rows[1][0]); // the group shows about one row, so a page is one row
        Win32.TryCapture(hwnd, Shot("lote2-selecao-arredondada"));
        K(0x1B);
        Thread.Sleep(350);
        var cleared = HeaderTitle(ScrollGroup);
        steps.Add($"{(cleared == ScrollGroup ? "OK " : "BAD")} Escape clears the selection: title='{cleared}'");
        Unpin(hwnd);
        return new JsonObject { ["visualRows"] = rows.Count, ["columns"] = cols, ["steps"] = steps };
    }

    private static JsonNode CheckLimit()
    {
        var hwnd = Window(FullGroup);
        BringTop(hwnd);
        var files = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinDirStat", "WinDirStat.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "NVIDIA Corporation", "NVIDIA App.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Microsoft Edge.lnk")
        }.Where(File.Exists).ToArray();
        SetClipboardFiles(files);
        Win32.GetWindowRect(hwnd, out var r);

        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        var result = new JsonObject { ["before"] = GroupItems(FullGroup).Count };
        InvokeFromMenu(new System.Drawing.Point(r.Left + 80, r.Top + 14), "Paste");
        var box = WaitNewWindow(before, 5);
        result["warningShown"] = box != IntPtr.Zero;
        if (box != IntPtr.Zero)
        {
            Thread.Sleep(400);
            var root = AutomationElement.FromHandle(box);
            result["warningText"] = string.Join(" | ", root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().Select(e => e.Current.Name));
            Win32.TryCapture(box, Shot("lote2-limite-30"));
            Button(root, "OK")?.Invoke();
            Thread.Sleep(500);
        }

        result["afterPaste"] = GroupItems(FullGroup).Count;

        before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        InvokeFromMenu(new System.Drawing.Point(r.Left + 80, r.Top + 14), "New", "Shortcut");
        var second = WaitNewWindow(before, 4);
        result["newShortcutWarning"] = second != IntPtr.Zero && Win32.TitleOf(second) == "Smart Dock Groups" || second != IntPtr.Zero;
        if (second != IntPtr.Zero)
        {
            Button(AutomationElement.FromHandle(second), "OK")?.Invoke();
            Thread.Sleep(400);
        }

        result["afterNewShortcut"] = GroupItems(FullGroup).Count;
        Unpin(hwnd);
        return result;
    }

    private static JsonNode CheckGroupLink()
    {
        var hwnd = Window(FreeGroup);
        BringTop(hwnd);
        Win32.GetWindowRect(hwnd, out var r);
        var header = new System.Drawing.Point(r.Left + 80, r.Top + 14);
        var result = new JsonObject();

        // The list: every other group, never this one.
        var (popup, menu) = OpenMenu(header.X, header.Y);
        var newItem = FindItem(menu!, "New")!;
        ((ExpandCollapsePattern)newItem.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(500);
        var linkItem = FindItem(newItem, "Shortcut to a group")!;
        result["linkMenu"] = SubNames(linkItem);
        CloseMenus();

        InvokeFromMenu(header, "New", "Shortcut to a group", "Dev Apps");
        Thread.Sleep(1200);
        result["linkAdded"] = GroupItems(FreeGroup).Any(i => i["Type"]?.GetValue<string>() == "GroupLink" && i["Name"]!.GetValue<string>() == "Dev Apps");

        var (_, menu2) = OpenMenu(header.X, header.Y);
        var again = FindItem(menu2!, "New")!;
        ((ExpandCollapsePattern)again.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(500);
        var link2 = FindItem(again, "Shortcut to a group")!;
        ((ExpandCollapsePattern)link2.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(500);
        var devEntry = FindItem(link2, "Dev Apps");
        result["secondLinkToDevAppsEnabled"] = devEntry?.Current.IsEnabled;
        CloseMenus();
        Win32.TryCapture(hwnd, Shot("lote2-atalho-de-grupo"));

        // Clicking it with Dev Apps closed: it opens, comes forward and its first icon is selected.
        var dev = Window("Dev Apps");
        Win32.GetWindowRect(dev, out var dr);
        InvokeFromMenu(new System.Drawing.Point(dr.Left + 60, dr.Top + 12), "Close group");
        Thread.Sleep(1200);
        result["devAppsClosed"] = Window("Dev Apps") == IntPtr.Zero;

        BringTop(hwnd);
        var caption = FindText(hwnd, "Dev Apps") ?? throw new InvalidOperationException("link caption not found");
        var c = caption.Current.BoundingRectangle;
        var point = new System.Drawing.Point((int)(c.Left + (c.Width / 2)), (int)(c.Top - 25));
        Win32.LeftClick(point.X, point.Y);
        Thread.Sleep(120);
        Win32.LeftClick(point.X, point.Y);
        WaitFor(() => Window("Dev Apps") != IntPtr.Zero, 8, "Dev Apps reopened by its link");
        Thread.Sleep(1500);

        var firstByPosition = GroupItems("Dev Apps")
            .Select(i => (Name: i["Name"]!.GetValue<string>(), X: i["DesktopIconX"]?.GetValue<double>() ?? 0, Y: i["DesktopIconY"]?.GetValue<double>() ?? 0))
            .OrderBy(i => Math.Round(i.Y / 40)).ThenBy(i => i.X).First().Name;
        result["reopenedTitle"] = HeaderTitle("Dev Apps");
        result["expectedFirstIcon"] = firstByPosition;
        result["isForeground"] = Win32.GetForegroundWindow() == Window("Dev Apps");
        Win32.TryCapture(Window("Dev Apps"), Shot("lote2-grupo-aberto-pelo-atalho"));
        Unpin(hwnd);
        return result;
    }

    // ───────────────────────────── helpers

    private static void Step(string name, Func<JsonNode> step)
    {
        Log($"== {name}");
        try
        {
            var result = step();
            Report[name] = result;
            Log(result.ToJsonString());
        }
        catch (Exception ex)
        {
            Report[name] = new JsonObject { ["error"] = $"{ex.GetType().Name}: {ex.Message}" };
            Log($"   ERROR {ex}");
            CloseMenus();
        }
    }

    private static void Log(string text) => Console.WriteLine(text);

    private static string Shot(string name) => Path.Combine(_out, $"lote-{name}.png");

    private static IntPtr Window(string title) =>
        Win32.VisibleWindowsOf(_pid).FirstOrDefault(h => Win32.TitleOf(h) == title);

    private static void WaitFor(Func<bool> condition, int seconds, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed.TotalSeconds > seconds)
            {
                throw new TimeoutException($"timed out waiting for {what}");
            }

            Thread.Sleep(150);
        }
    }

    private static IntPtr WaitNewWindow(HashSet<IntPtr> before, int seconds, IntPtr exclude = default)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var fresh = Win32.VisibleWindowsOf(_pid).FirstOrDefault(h => !before.Contains(h) && h != exclude);
            if (fresh != IntPtr.Zero)
            {
                return fresh;
            }

            Thread.Sleep(120);
        }

        return IntPtr.Zero;
    }

    private static void BringTop(IntPtr hwnd) => Win32.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013);

    private static void Unpin(IntPtr hwnd) => Win32.SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, 0x0013);

    private static System.Drawing.Point CanvasPoint(IntPtr hwnd)
    {
        Win32.GetWindowRect(hwnd, out var rect);
        return new System.Drawing.Point(rect.Right - 40, rect.Bottom - 24);
    }

    private static (IntPtr Popup, AutomationElement? Menu) OpenMenu(int x, int y)
    {
        var before = Win32.VisibleWindowsOf(_pid).ToHashSet();
        Win32.RightClick(x, y);
        var popup = WaitNewWindow(before, 4);
        if (popup == IntPtr.Zero)
        {
            return (IntPtr.Zero, null);
        }

        Thread.Sleep(350);
        var root = AutomationElement.FromHandle(popup);
        var menu = root.FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu)) ?? root;
        return (popup, menu);
    }

    private static void InvokeFromMenu(System.Drawing.Point at, params string[] path)
    {
        var (_, menu) = OpenMenu(at.X, at.Y);
        if (menu is null)
        {
            throw new InvalidOperationException($"no menu opened at {at}");
        }

        var current = menu;
        for (var i = 0; i < path.Length; i++)
        {
            var item = FindItem(current, path[i]) ?? throw new InvalidOperationException($"menu item '{path[i]}' not found");
            if (i < path.Length - 1)
            {
                ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                Thread.Sleep(450);
                current = item;
                continue;
            }

            // A command that opens a modal dialog blocks Invoke until the dialog closes.
            _ = Task.Run(() => ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke());
            Thread.Sleep(400);
        }
    }

    private static AutomationElement? FindItem(AutomationElement parent, string prefix)
    {
        return parent.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
            .Cast<AutomationElement>()
            .FirstOrDefault(e => Clean(e.Current.Name).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string Clean(string name) => name.Replace('\t', ' ').Trim();

    private static JsonArray TopLevelNames(AutomationElement? menu)
    {
        if (menu is null)
        {
            return new JsonArray("(no menu)");
        }

        return new JsonArray(menu.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
            .Cast<AutomationElement>()
            .Select(e => (JsonNode)(Clean(e.Current.Name) + (e.Current.IsEnabled ? "" : " [off]")))
            .ToArray());
    }

    private static JsonArray SubNames(AutomationElement item)
    {
        ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(450);
        var names = TopLevelNames(item);
        ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Thread.Sleep(200);
        return names;
    }

    private static void CloseMenus(bool keepSheet = false)
    {
        // Escape walks the menus closed one level at a time; stop before it reaches the sheet.
        static List<IntPtr> Popups() => Win32.VisibleWindowsOf(_pid)
            .Where(h => Win32.TitleOf(h).Length == 0 && Win32.ClassOf(h).StartsWith("HwndWrapper", StringComparison.Ordinal))
            // Not the app's permanent 0x0 anchor for the tray menu, parked off screen.
            .Where(h => Win32.GetWindowRect(h, out var r) && r.Width > 1 && r.Height > 1 && r.Left > -5000)
            .ToList();

        for (var i = 0; i < 4; i++)
        {
            var open = Popups().Count;
            if (open == 0)
            {
                break;
            }

            Win32.PressEscape();

            // One Escape per level, and only after the previous one took effect — an extra
            // press would land on the App Folder sheet and close it.
            var clock = Stopwatch.StartNew();
            while (Popups().Count >= open && clock.Elapsed < TimeSpan.FromSeconds(1.5))
            {
                Thread.Sleep(80);
            }
        }

        if (!keepSheet)
        {
            Thread.Sleep(100);
        }
    }

    private static InvokePattern? Button(AutomationElement root, string prefix)
    {
        var button = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .FirstOrDefault(e => e.Current.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));
        return button?.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
    }

    private static AutomationElement? FindText(IntPtr hwnd, string text)
    {
        return AutomationElement.FromHandle(hwnd)
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .FirstOrDefault(e => e.Current.Name == text);
    }

    private static bool IsPopupNear(IntPtr hwnd, Win32.Rect near)
    {
        Win32.GetWindowRect(hwnd, out var r);
        return Math.Abs(r.Left - near.Left) < 40 && r.Top >= near.Top && r.Top < near.Bottom;
    }

    private static double DistanceToRect(System.Drawing.Point p, Win32.Rect r)
    {
        var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
    }

    private static JsonNode ReadConfig() => JsonNode.Parse(File.ReadAllText(Path.Combine(_data, "config.json")))!;

    private static IEnumerable<JsonNode> Groups(JsonNode config) =>
        config["Categories"]!.AsArray().Where(g => g is not null)!;

    private static IEnumerable<JsonNode> AllItems(JsonNode config) =>
        Groups(config).SelectMany(g => g["Items"]!.AsArray()).Where(i => i is not null)!;

    private static List<JsonNode> GroupItems(string group) =>
        Groups(ReadConfig()).FirstOrDefault(g => g["Name"]!.GetValue<string>() == group)?["Items"]!.AsArray().OfType<JsonNode>().ToList()
        ?? [];

    private static double SystemScale() => GetDpiForSystem() / 96.0;

    private static void SetClipboardFiles(string[] files)
    {
        var thread = new Thread(() =>
        {
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(files);
            System.Windows.Forms.Clipboard.SetFileDropList(list);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static class Keys
    {
        public const byte VK_CONTROL = 0x11;
        private const uint KEYUP = 0x0002;

        public static void Press(byte vk)
        {
            Win32.keybd_event(vk, 0, 0, UIntPtr.Zero);
            Win32.keybd_event(vk, 0, KEYUP, UIntPtr.Zero);
        }

        public static void Chord(byte modifier, byte vk)
        {
            Win32.keybd_event(modifier, 0, 0, UIntPtr.Zero);
            Press(vk);
            Win32.keybd_event(modifier, 0, KEYUP, UIntPtr.Zero);
        }

        public static void Type(string text)
        {
            foreach (var ch in text)
            {
                var vk = (byte)(Win32.VkKeyScan(ch) & 0xFF);
                Press(vk);
                Thread.Sleep(60);
            }
        }
    }
}
