using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\AppProxy.Desktop", out bool acquired);
        if (!acquired)
        {
            MessageBox.Show("AppProxy уже запущен.", "AppProxy");
            return;
        }
        Application.Run(new ProxyWindow());
    }
}

internal sealed class ProxyWindow : Form
{
    private readonly TextBox application = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox host = new() { Dock = DockStyle.Fill, PlaceholderText = "proxy.example.com" };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 1080, Width = 100 };
    private readonly ComboBox protocol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox username = new() { Dock = DockStyle.Fill };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button installed = new() { Text = "Из списка…", AutoSize = true };
    private readonly Button browse = new() { Text = "Файл…", AutoSize = true };
    private readonly Button start = new() { Text = "Включить", AutoSize = true };
    private readonly Button stop = new() { Text = "Остановить", AutoSize = true, Enabled = false };
    private readonly Label status = new() { Text = "Выключено", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private readonly ConcurrentQueue<string> messages = new();
    private Process? engine;

    public ProxyWindow()
    {
        Text = "AppProxy";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 490);
        MinimumSize = new Size(580, 420);
        Font = new Font("Segoe UI", 10);
        protocol.Items.AddRange(new object[] { "SOCKS5", "HTTPS CONNECT" });
        protocol.SelectedIndex = 0;
        protocol.SelectedIndexChanged += (_, _) => port.Value = protocol.SelectedIndex == 0 ? 1080 : 443;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 7; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Приложение", application);
        var pickers = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pickers.Controls.AddRange(new Control[] { installed, browse });
        layout.Controls.Add(pickers, 2, 0);
        AddRow(layout, 1, "Протокол", protocol);
        AddRow(layout, 2, "Сервер", host);
        AddRow(layout, 3, "Порт", port);
        AddRow(layout, 4, "Логин", username);
        AddRow(layout, 5, "Пароль", password);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.AddRange(new Control[] { start, stop, status });
        layout.Controls.Add(actions, 0, 6);
        layout.SetColumnSpan(actions, 3);
        layout.Controls.Add(log, 0, 7);
        layout.SetColumnSpan(log, 3);
        Controls.Add(layout);

        installed.Click += (_, _) =>
        {
            using var picker = new InstalledAppPicker();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedPath is { } path)
                application.Text = path;
        };
        browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "Приложения Windows (*.exe)|*.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == DialogResult.OK)
                application.Text = picker.FileName;
        };
        start.Click += (_, _) => StartProxy();
        stop.Click += (_, _) => TryStop();
        timer.Tick += (_, _) => RefreshEngine();
        timer.Start();
        FormClosing += (_, e) => e.Cancel = !TryStop();
        FormClosed += (_, _) => timer.Dispose();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string name, Control control)
    {
        layout.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private void StartProxy()
    {
        if (engine is not null)
            return;

        var server = host.Text.Trim();
        if (server.StartsWith('[') && server.EndsWith(']'))
            server = server[1..^1];
        if (!File.Exists(application.Text) ||
            (!IPAddress.TryParse(server, out _) && Uri.CheckHostName(server) != UriHostNameType.Dns))
        {
            MessageBox.Show(this, "Выберите существующий EXE и укажите имя или IP-адрес прокси.", Text);
            return;
        }
        if (username.Text.Length == 0 && password.Text.Length != 0)
        {
            MessageBox.Show(this, "Введите логин для указанного пароля.", Text);
            return;
        }

        log.Clear();
        messages.Clear();
        try
        {
            var config = ProxyConfig.Create(application.Text, server, (int)port.Value, protocol.SelectedIndex == 1,
                username.Text, password.Text);
            string enginePath = Path.Combine(AppContext.BaseDirectory, "engine", "sing-box.exe");
            var worker = new Process
            {
                StartInfo = new ProcessStartInfo(enginePath, "run -c stdin --disable-color")
                {
                    WorkingDirectory = Path.GetDirectoryName(enginePath)!,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            worker.ErrorDataReceived += (_, e) => Enqueue(e.Data);
            worker.OutputDataReceived += (_, e) => Enqueue(e.Data);
            try
            {
                if (!worker.Start())
                    throw new InvalidOperationException("Не удалось запустить sing-box.");
                engine = worker;
            }
            catch
            {
                worker.Dispose();
                throw;
            }
            worker.BeginErrorReadLine();
            worker.BeginOutputReadLine();
            worker.StandardInput.Write(config);
            worker.StandardInput.Close();
            SetActive(true, "Запуск…");
        }
        catch (Exception ex)
        {
            TryStop();
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Enqueue(string? line)
    {
        if (line is not null)
            messages.Enqueue(line);
    }

    private void RefreshEngine()
    {
        while (messages.TryDequeue(out var line))
        {
            if (log.TextLength > 64_000)
                log.Clear();
            log.AppendText(line + Environment.NewLine);
            if (engine is not null && line.Contains("sing-box started (", StringComparison.Ordinal))
                status.Text = "Маршрутизация включена";
        }

        if (engine is { HasExited: true } worker)
        {
            worker.Dispose();
            engine = null;
            SetActive(false, "Движок завершился");
        }
    }

    private bool TryStop()
    {
        if (engine is not { } worker)
            return true;

        try
        {
            if (!worker.HasExited && !StopWithInterrupt(worker))
            {
                try { worker.Kill(); }
                catch (InvalidOperationException) when (worker.HasExited) { }
            }
            if (!worker.WaitForExit(3000))
                throw new TimeoutException("Движок пока не завершился.");

            worker.Dispose();
            engine = null;
            SetActive(false, "Выключено");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static bool StopWithInterrupt(Process worker)
    {
        if (!AttachConsole((uint)worker.Id))
            return false;

        try
        {
            // The child has its own hidden console. Ignore its control event in this UI process.
            return SetConsoleCtrlHandler(IntPtr.Zero, true)
                && GenerateConsoleCtrlEvent(0, 0)
                && worker.WaitForExit(3000);
        }
        finally
        {
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint signal, uint processGroupId);

    private void SetActive(bool active, string message)
    {
        start.Enabled = !active;
        stop.Enabled = active;
        foreach (var field in new Control[] { installed, browse, protocol, host, port, username, password })
            field.Enabled = !active;
        status.Text = message;
    }
}
