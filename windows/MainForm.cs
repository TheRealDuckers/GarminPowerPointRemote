using System.Drawing;
using System.Windows.Forms;

namespace PptRemoteServer;

/// <summary>Status window + tray icon. Closing the window hides it to the tray.</summary>
internal sealed class MainForm : Form
{
    private static readonly Color Blue = Color.FromArgb(31, 111, 235);
    private static readonly Color Green = Color.FromArgb(30, 123, 52);
    private static readonly Color Red = Color.FromArgb(155, 34, 38);
    private static readonly Color Amber = Color.FromArgb(176, 112, 0);
    private static readonly Color Slate = Color.FromArgb(90, 98, 108);
    private static readonly Color Muted = Color.FromArgb(110, 118, 128);

    private readonly Settings _settings;
    private readonly AuthState _auth;
    private readonly GattServer _server;

    private readonly Panel _header = new();
    private readonly Label _dot = new();
    private readonly Label _statusTitle = new();
    private readonly Label _statusDetail = new();
    private readonly Label _lastActionCaption = new();
    private readonly Label _lastAction = new();
    private readonly Label _pinLabel = new();
    private readonly Button _pinToggle = new();
    private readonly Button _pinNew = new();
    private readonly ComboBox _pinLength = new();
    private readonly ComboBox _keyScheme = new();
    private readonly CheckBox _keysEnabled = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly Button _test = new();
    private readonly ListBox _log = new();
    private readonly NotifyIcon _tray = new();

    private ServerState _serverState = ServerState.Stopped;
    private string _serverDetail = "";
    private bool _watchConnected;
    private string _watchId = "";
    private bool _pinVisible = true;
    private bool _pinAutoHidden;
    private DateTime _lastReject = DateTime.MinValue;

    private bool _startHidden;
    private bool _quitting;
    private bool _serverStarted;
    private bool _trayHintShown;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;

        // Load settings; the first run gets a random PIN.
        _settings = Settings.Load();
        if (!PinCode.IsValid(_settings.Pin))
        {
            _settings.Pin = PinCode.Generate(6);
            _settings.LastCounter = 0;
            _settings.Save();
        }
        _auth = new AuthState();
        _auth.SetPin(_settings.Pin, _settings.LastCounter);
        _server = new GattServer(_auth);

        Text = "PPT Remote";
        ClientSize = new Size(440, 720);
        MinimumSize = new Size(420, 660);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        BuildUi();
        BuildTray();
        WireServer();
        UpdateStatus();
        UpdatePinDisplay();

        AddLog(Auth.SelfTest()
            ? "Security self-test passed"
            : "Security self-test FAILED: the watch's packets won't verify");
    }

    // ---- UI construction ----

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // last action
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // PIN
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // settings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // log caption
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // log

        // Status header.
        _header.Dock = DockStyle.Fill;
        _header.Height = 92;
        _header.Margin = Padding.Empty;

        _dot.Text = "\u25CF";
        _dot.Font = new Font("Segoe UI", 26f);
        _dot.ForeColor = Color.White;
        _dot.AutoSize = true;
        _dot.Location = new Point(18, 16);

        _statusTitle.Font = new Font("Segoe UI Semibold", 15f);
        _statusTitle.ForeColor = Color.White;
        _statusTitle.AutoSize = true;
        _statusTitle.Location = new Point(70, 14);

        _statusDetail.Font = new Font("Segoe UI", 10f);
        _statusDetail.ForeColor = Color.FromArgb(235, 240, 255);
        _statusDetail.AutoSize = true;
        _statusDetail.MaximumSize = new Size(360, 0);
        _statusDetail.Location = new Point(72, 52);

        _header.Controls.AddRange(new Control[] { _dot, _statusTitle, _statusDetail });
        root.Controls.Add(_header, 0, 0);

        // Last action.
        var actionPanel = new Panel { Dock = DockStyle.Fill, Height = 92, Margin = Padding.Empty };

        _lastActionCaption.Text = "LAST ACTION";
        _lastActionCaption.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _lastActionCaption.ForeColor = Muted;
        _lastActionCaption.AutoSize = true;
        _lastActionCaption.Location = new Point(20, 16);

        _lastAction.Text = "Nothing yet";
        _lastAction.Font = new Font("Segoe UI Semibold", 20f);
        _lastAction.AutoSize = true;
        _lastAction.Location = new Point(18, 36);

        actionPanel.Controls.AddRange(new Control[] { _lastActionCaption, _lastAction });
        root.Controls.Add(actionPanel, 0, 1);

        // Watch PIN.
        var pinPanel = new Panel { Dock = DockStyle.Fill, Height = 150, Margin = Padding.Empty };

        var pinCaption = new Label
        {
            Text = "WATCH PIN",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(20, 8)
        };

        _pinLabel.Font = new Font("Consolas", 26f, FontStyle.Bold);
        _pinLabel.AutoSize = true;
        _pinLabel.Location = new Point(18, 26);

        var pinHelp = new Label
        {
            Text = "Type this PIN into the watch the first time you open PPT Remote. " +
                   "Only a watch that knows it can control your slides.",
            ForeColor = Muted,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Location = new Point(20, 72)
        };

        _pinToggle.Text = "Hide";
        _pinToggle.AutoSize = true;
        _pinToggle.Location = new Point(20, 112);
        _pinToggle.Click += (_, _) =>
        {
            _pinVisible = !_pinVisible;
            UpdatePinDisplay();
        };

        _pinNew.Text = "New PIN";
        _pinNew.AutoSize = true;
        _pinNew.Location = new Point(100, 112);
        _pinNew.Click += (_, _) => NewPin();

        _pinLength.DropDownStyle = ComboBoxStyle.DropDownList;
        _pinLength.Width = 90;
        _pinLength.Location = new Point(190, 113);
        _pinLength.Items.AddRange(new object[] { "6 digits", "8 digits", "10 digits", "12 digits" });
        _pinLength.SelectedIndex = Math.Clamp((_settings.Pin.Length - 6) / 2, 0, 3);

        pinPanel.Controls.AddRange(new Control[]
            { pinCaption, _pinLabel, pinHelp, _pinToggle, _pinNew, _pinLength });
        root.Controls.Add(pinPanel, 0, 2);

        // Settings.
        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 0, 20, 8),
            Margin = Padding.Empty
        };

        var keysLabel = new Label
        {
            Text = "Slide navigation keys",
            AutoSize = true,
            ForeColor = Muted,
            Margin = new Padding(0, 4, 0, 2)
        };

        _keyScheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _keyScheme.Width = 280;
        _keyScheme.Items.AddRange(new object[] { "Arrow keys (Left / Right)", "Page Up / Page Down" });
        _keyScheme.SelectedIndex = (int)_settings.KeyScheme;
        _keyScheme.SelectedIndexChanged += (_, _) =>
        {
            _settings.KeyScheme = (KeyScheme)_keyScheme.SelectedIndex;
            _settings.Save();
        };

        _keysEnabled.Text = "Send keypresses to the active window";
        _keysEnabled.Checked = true;
        _keysEnabled.AutoSize = true;
        _keysEnabled.Margin = new Padding(0, 10, 0, 0);
        _keysEnabled.CheckedChanged += (_, _) =>
            AddLog(_keysEnabled.Checked ? "Keypresses on" : "Keypresses paused");

        _startWithWindows.Text = "Start with Windows (minimized to tray)";
        _startWithWindows.Checked = StartupRegistration.IsEnabled;
        _startWithWindows.AutoSize = true;
        _startWithWindows.CheckedChanged += (_, _) => StartupRegistration.Set(_startWithWindows.Checked);

        _test.Text = "Test: send Next in 3 seconds";
        _test.AutoSize = true;
        _test.Margin = new Padding(0, 10, 0, 0);
        _test.Click += async (_, _) => await RunTestAsync();

        settings.Controls.AddRange(new Control[]
            { keysLabel, _keyScheme, _keysEnabled, _startWithWindows, _test });
        root.Controls.Add(settings, 0, 3);

        // Activity log.
        var logCaption = new Label
        {
            Text = "ACTIVITY",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Muted,
            AutoSize = true,
            Margin = new Padding(20, 8, 0, 4)
        };
        root.Controls.Add(logCaption, 0, 4);

        _log.Dock = DockStyle.Fill;
        _log.Margin = new Padding(20, 0, 20, 20);
        _log.IntegralHeight = false;
        _log.Font = new Font("Consolas", 9.5f);
        root.Controls.Add(_log, 0, 5);

        Controls.Add(root);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "PPT Remote";
        _tray.ContextMenuStrip = menu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void WireServer()
    {
        _server.StateChanged += (state, detail) => RunOnUi(() =>
        {
            _serverState = state;
            _serverDetail = detail;
            if (state is ServerState.Stopped or ServerState.Error)
            {
                _watchConnected = false;
            }
            UpdateStatus();
        });

        _server.WatchConnectionChanged += (connected, id) => RunOnUi(() =>
        {
            _watchConnected = connected;
            _watchId = id;
            AddLog(connected ? $"Watch connected ({id})" : "Watch disconnected");

            // Once a watch has got in, tuck the PIN away so it isn't on screen during a talk.
            if (connected && _pinVisible && !_pinAutoHidden)
            {
                _pinAutoHidden = true;
                _pinVisible = false;
                UpdatePinDisplay();
            }
            UpdateStatus();
        });

        _server.CommandRejected += result => RunOnUi(() =>
        {
            _lastReject = DateTime.Now;
            AddLog("Rejected a message: " + result switch
            {
                AuthResult.BadTag => "wrong PIN, or not your watch",
                AuthResult.BadLength => "unrecognized message",
                AuthResult.Replay => "old message (make a new PIN if your watch keeps getting rejected)",
                AuthResult.LockedOut => "too many wrong PINs, pausing for a minute",
                _ => result.ToString()
            });
            UpdateStatus();
        });

        _server.CounterAdvanced += counter =>
        {
            _settings.LastCounter = counter;
            _settings.Save();
        };

        _server.CommandReceived += OnCommand;
        _server.Message += text => RunOnUi(() => AddLog(text));
    }

    // ---- Behaviour ----

    private void OnCommand(byte command) => RunOnUi(() =>
    {
        if (command == Protocol.Hello)
        {
            return;  // heartbeat; connection state is tracked by GattServer
        }

        string label;
        if (!_keysEnabled.Checked)
        {
            label = $"{Protocol.Describe(command)} (keypresses paused)";
        }
        else
        {
            label = KeySender.Execute(command, _settings.KeyScheme)
                    ?? Protocol.Describe(command);
        }

        _lastAction.Text = label;
        _lastActionCaption.Text = $"LAST ACTION   {DateTime.Now:HH:mm:ss}";
        AddLog(label);
    });

    private async Task RunTestAsync()
    {
        _test.Enabled = false;
        AddLog("Test: click your slideshow now; sending Next in 3 seconds");
        await Task.Delay(3000);
        var label = KeySender.Execute(Protocol.Next, _settings.KeyScheme) ?? "Test failed";
        _lastAction.Text = label;
        _lastActionCaption.Text = $"LAST ACTION   {DateTime.Now:HH:mm:ss}";
        AddLog($"Test: {label}");
        _test.Enabled = true;
    }

    private void NewPin()
    {
        int length = 6 + _pinLength.SelectedIndex * 2;
        var answer = MessageBox.Show(this,
            $"Make a new {length}-digit PIN?\n\nYour watch will stop working until you type the new PIN into it.",
            "New PIN", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK)
        {
            return;
        }

        _settings.Pin = PinCode.Generate(length);
        _settings.LastCounter = 0;
        _settings.Save();
        _auth.SetPin(_settings.Pin, 0);

        _pinVisible = true;
        _watchConnected = false;
        UpdatePinDisplay();
        UpdateStatus();
        AddLog($"New {length}-digit PIN created");
    }

    private void UpdatePinDisplay()
    {
        _pinLabel.Text = PinCode.Format(_settings.Pin, masked: !_pinVisible);
        _pinToggle.Text = _pinVisible ? "Hide" : "Show";
    }

    private void UpdateStatus()
    {
        Color color;
        string title;
        string detail;
        bool recentlyRejected = (DateTime.Now - _lastReject) < TimeSpan.FromMinutes(2);

        switch (_serverState)
        {
            case ServerState.Error:
                color = Red;
                title = "Bluetooth problem";
                detail = _serverDetail;
                break;
            case ServerState.Advertising or ServerState.Starting when _watchConnected:
                color = Green;
                title = "Watch connected";
                detail = string.IsNullOrEmpty(_watchId) ? "Ready for slide commands" : $"Device {_watchId}";
                break;
            case ServerState.Advertising when recentlyRejected:
                color = Amber;
                title = "Waiting for your watch";
                detail = "A device was turned away. Check the PIN on the watch.";
                break;
            case ServerState.Advertising:
                color = Blue;
                title = "Waiting for your watch";
                detail = "Open PPT Remote on the watch";
                break;
            case ServerState.Starting:
                color = Slate;
                title = "Starting...";
                detail = _serverDetail;
                break;
            default:
                color = Slate;
                title = "Stopped";
                detail = _serverDetail;
                break;
        }

        _header.BackColor = color;
        _statusTitle.Text = title;
        _statusDetail.Text = detail;

        var tip = $"PPT Remote: {title}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void AddLog(string text)
    {
        _log.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_log.Items.Count > 200)
        {
            _log.Items.RemoveAt(_log.Items.Count - 1);
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Window is closing.
            }
        }
        else
        {
            action();
        }
    }

    // ---- Window / tray lifecycle ----

    private void ShowWindow()
    {
        _startHidden = false;
        Visible = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        _quitting = true;
        _tray.Visible = false;
        _server.Stop();
        Close();
        Application.Exit();
    }

    // Lets the app start hidden in the tray (for --minimized) while still creating the window handle.
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            value = false;
            if (!IsHandleCreated)
            {
                CreateHandle();
            }
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_serverStarted)
        {
            _serverStarted = true;
            _ = _server.StartAsync();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(2500, "PPT Remote is still running",
                    "Right-click the tray icon to quit.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Dispose();
        base.OnFormClosed(e);
    }
}
