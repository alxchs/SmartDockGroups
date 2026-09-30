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

    private static string _data = "";
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
        using var app = Process.Start(start) ?? throw new InvalidOperationException("app did not start");
        _pid = (uint)app.Id;
        Log($"started pid {_pid} with data {_data}");

        try
        {
            WaitFor(() => Window(FreeGroup) != IntPtr.Zero && Window("Dev Apps") != IntPtr.Zero, 40, "group windows");
            Thread.Sleep(3000);

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
                app.Kill();
                app.WaitForExit(5000);
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
        InvokeFromMenu(click, "New group");
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
