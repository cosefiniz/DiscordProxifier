using System.Drawing;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // The notification-area icon and its menu, shared by both looks.
    public class TrayIcon
    {
        readonly AppController app;
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem status, connect, switchMode, launch, show, exit;

        public TrayIcon(AppController app)
        {
            this.app = app;
            status = new ToolStripMenuItem { Enabled = false };
            connect = new ToolStripMenuItem();
            connect.Click += async (s, e) => await app.ConnectButtonClicked();
            switchMode = new ToolStripMenuItem();
            switchMode.Click += async (s, e) => await app.SwitchToNextMode();
            launch = new ToolStripMenuItem();
            launch.Click += async (s, e) => await app.LaunchSelected();
            show = new ToolStripMenuItem();
            show.Click += (s, e) => app.ShowFromTray();
            exit = new ToolStripMenuItem();
            exit.Click += (s, e) => app.Exit();
            menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Body };
            menu.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), connect, switchMode, launch, new ToolStripSeparator(), show, exit });
            icon = new NotifyIcon
            {
                Icon = new Icon(BorderlessWindow.AppIcon, SystemInformation.SmallIconSize),
                Text = "Discord Proxifier",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (s, e) => app.ShowFromTray();
            ApplyTexts();
        }

        public void ApplyTexts()
        {
            show.Text = L.T("tray.show");
            launch.Text = L.T("tray.launch");
            exit.Text = L.T("tray.exit");
        }

        public void Refresh(StatusInfo s)
        {
            status.Text = s.Title + " · " + s.ModeName;
            connect.Text = s.ButtonText;
            switchMode.Text = L.F("tray.switch", ProxyModes.Display(ProxyModes.Next(app.Mode)));
            switchMode.Enabled = s.Kind != StatusKind.Connecting;
            string tip = "Discord Proxifier · " + s.Title;
            tip = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            if (icon.Text != tip)
                icon.Text = tip;
        }

        public void Balloon(int ms, string title, string text, NoteKind kind)
        {
            ToolTipIcon i = kind == NoteKind.Error ? ToolTipIcon.Error : kind == NoteKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            icon.ShowBalloonTip(ms, title, string.IsNullOrEmpty(text) ? " " : text, i);
        }

        public void Hide()
        {
            icon.Visible = false;
        }
    }
}
