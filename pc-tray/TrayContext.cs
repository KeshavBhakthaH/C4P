using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace A2dpSink;

internal sealed class TrayContext : ApplicationContext
{
    private readonly AudioSinkService _service = new();
    private readonly NotifyIcon _icon;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _disconnectItem;
    private readonly ToolStripMenuItem _pauseItem;
    private SinkStatus _status = new(SinkState.Disconnected, false, string.Empty, false);

    private Control? _marshalTarget;

    public TrayContext()
    {
        DeleteLegacyPairingKeyFile();

        _marshalTarget = new Control();
        _ = _marshalTarget.Handle;

        _connectItem = new ToolStripMenuItem("Connect", null, (_, _) => Safe(() => _service.ConnectAsync()));
        _disconnectItem = new ToolStripMenuItem("Disconnect", null, (_, _) => Safe(() => _service.DisconnectAsync()));
        _pauseItem = new ToolStripMenuItem("Pause forwarding", null, (_, _) => Safe(TogglePauseAsync));

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _connectItem,
            _disconnectItem,
            new ToolStripSeparator(),
            _pauseItem,
            new ToolStripSeparator(),
            exitItem
        });

        _appIcon = LoadEmbeddedIcon();

        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "C4P - starting",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowBalloon("C4P", _status.ToProtocolText());
        };

        _service.StatusChanged += status => InvokeOnUiThread(() =>
        {
            _status = status;
            UpdateUi(status);
        });

        _service.MessageRaised += message => InvokeOnUiThread(() => ShowBalloon("C4P", message));

        UpdateUi(_status);

        _service.Start();

        if (_status.DeviceName.Length > 0)
            ShowBalloon("C4P", $"Ready. Device found: {_status.DeviceName}");
        else
            ShowBalloon("C4P", "Waiting for a paired audio device.");
    }

    private static void DeleteLegacyPairingKeyFile()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "C4P",
                "pairing-key.txt");

            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static Icon LoadEmbeddedIcon()
    {
        using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("A2dpSink.AppIcon.ico")
                           ?? throw new InvalidOperationException("Embedded AppIcon.ico resource not found");
        return new Icon(stream);
    }

    private void UpdateUi(SinkStatus status)
    {
        string text = status.ToProtocolText();
        _icon.Text = text.Length <= 63 ? $"C4P - {text}" : "C4P";

        _connectItem.Enabled = status.State == SinkState.Disconnected && !status.Paused;
        _disconnectItem.Enabled = status.State != SinkState.Disconnected;
        _pauseItem.Checked = status.Paused;
        _pauseItem.Text = status.Paused ? "Resume forwarding" : "Pause forwarding";
    }

    private async Task TogglePauseAsync()
    {
        if (_status.Paused)
            await _service.ResumeForwardingAsync();
        else
            await _service.PauseForwardingAsync();
    }

    private async void Safe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowBalloon("C4P", ex.Message);
        }
    }

    private void InvokeOnUiThread(Action action)
    {
        var target = _marshalTarget!;
        target.BeginInvoke(action);
    }

    private void ShowBalloon(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message.Length > 255 ? message[..255] : message;
        _icon.ShowBalloonTip(2000);
    }

    private void ExitApplication()
    {
        DialogResult choice = MessageBox.Show(
            "Exit C4P?\n\nThis closes the audio link. Use \"Pause forwarding\" instead if you just want to stop the PC output temporarily.",
            "C4P",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (choice != DialogResult.Yes)
            return;

        _ = ExitCleanupAsync();
    }

    private async Task ExitCleanupAsync()
    {
        try
        {
            await _service.DisposeAsync();
        }
        catch
        {
        }

        InvokeOnUiThread(() =>
        {
            _icon.Visible = false;
            _icon.Dispose();
            _appIcon.Dispose();
            _marshalTarget?.Dispose();
            Application.Exit();
        });
    }
}
