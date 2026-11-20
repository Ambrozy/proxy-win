using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed class InstalledAppPicker : Form
{
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Поиск по названию или имени EXE" };
    private readonly ListBox results = new() { Dock = DockStyle.Fill, DisplayMember = nameof(InstalledApp.Name), SelectionMode = SelectionMode.MultiExtended };
    private readonly Label details = new() { Dock = DockStyle.Top, Height = 54, AutoEllipsis = true, Text = "Загрузка списка…" };
    private readonly Button choose = new() { Text = "Добавить", AutoSize = true, Enabled = false };
    private InstalledApp[] apps = [];

    public string[] SelectedPaths { get; private set; } = [];

    public InstalledAppPicker()
    {
        Text = "Установленные приложения";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 420);
        MinimumSize = new Size(420, 300);
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange(new Control[] { cancel, choose });
        Controls.Add(results);
        Controls.Add(details);
        Controls.Add(search);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = "Ctrl или Shift для выбора нескольких программ" });
        Controls.Add(buttons);
        AcceptButton = choose;
        CancelButton = cancel;
        search.TextChanged += (_, _) => Filter();
        results.SelectedIndexChanged += (_, _) => UpdateSelection();
        results.DoubleClick += (_, _) => Choose();
        choose.Click += (_, _) => Choose();
        Shown += async (_, _) =>
        {
            var discovered = await Task.Run(InstalledApp.Find);
            if (IsDisposed)
                return;
            apps = discovered;
            Filter();
            search.Focus();
        };
    }

    private void Filter()
    {
        string query = search.Text.Trim();
        var matching = apps.Where(app =>
            app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            Path.GetFileName(app.Path).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        results.BeginUpdate();
        results.Items.Clear();
        results.Items.AddRange(matching);
        if (matching.Length > 0)
            results.SelectedIndex = 0;
        results.EndUpdate();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var app = results.SelectedItem as InstalledApp;
        int count = results.SelectedItems.Count;
        choose.Enabled = count > 0;
        details.Text = count > 1 ? $"Выбрано программ: {count}" : app?.Path ?? "Программы не найдены. Измените запрос или выберите файл вручную.";
    }

    private void Choose()
    {
        if (results.SelectedItems.Count == 0)
            return;
        SelectedPaths = results.SelectedItems.Cast<InstalledApp>().Select(app => app.Path).ToArray();
        DialogResult = DialogResult.OK;
    }
}

internal sealed record InstalledApp(string Name, string Path)
{
    public static InstalledApp[] Find()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                ReadRegisteredPaths(apps, hive, view);
        }

        // A shortcut's display name is generally more useful than a registered file name.
        ReadShortcuts(apps, Environment.SpecialFolder.CommonPrograms);
        ReadShortcuts(apps, Environment.SpecialFolder.Programs);
        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ReadRegisteredPaths(Dictionary<string, InstalledApp> apps, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var paths = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
            if (paths is null)
                return;
            foreach (string name in paths.GetSubKeyNames())
            {
                try
                {
                    using var entry = paths.OpenSubKey(name);
                    Add(apps, System.IO.Path.GetFileNameWithoutExtension(name), entry?.GetValue(null) as string, false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private static void ReadShortcuts(Dictionary<string, InstalledApp> apps, Environment.SpecialFolder folder)
    {
        string directory = Environment.GetFolderPath(folder);
        if (directory.Length == 0)
            return;

        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null || (shell = Activator.CreateInstance(type)) is null)
                return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string file in Directory.EnumerateFiles(directory, "*.lnk", options))
            {
                object? shortcut = null;
                try
                {
                    shortcut = ((dynamic)shell).CreateShortcut(file);
                    Add(apps, System.IO.Path.GetFileNameWithoutExtension(file), (string)((dynamic)shortcut).TargetPath, true);
                }
                catch (COMException) { }
                finally
                {
                    if (shortcut is not null)
                        Marshal.FinalReleaseComObject(shortcut);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { }
        finally
        {
            if (shell is not null)
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void Add(Dictionary<string, InstalledApp> apps, string name, string? target, bool preferName)
    {
        var path = target?.Trim().Trim('"');
        // Only concrete local drive paths. Reading the list never launches a shortcut.
        if (path is null || path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('\\' or '/') || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
            return;
        path = System.IO.Path.GetFullPath(path);
        if (preferName || !apps.ContainsKey(path))
            apps[path] = new InstalledApp(name, path);
    }
}
