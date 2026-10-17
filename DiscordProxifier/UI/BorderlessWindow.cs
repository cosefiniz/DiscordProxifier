using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // Frameless dark window with a drop shadow, a draggable title bar showing the logo,
    // and title-bar buttons on the right.
    public class BorderlessWindow : Form
    {
        protected readonly FlatButton CloseButton;
        protected string Subtitle = "";
        protected int TitleHeight => Theme.S(46);

        static Image logo;
        static Icon appIcon;

        public static Image Logo
        {
            get
            {
                if (logo == null)
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.png"));
                    if (name != null)
                        using (Stream s = asm.GetManifestResourceStream(name))
                            logo = new Bitmap(Image.FromStream(s));
                }
                return logo;
            }
        }

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    }
                    catch
                    {
                        appIcon = SystemIcons.Application;
                    }
                }
                return appIcon;
            }
        }

        public BorderlessWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon;
            KeyPreview = true;

            CloseButton = TitleButton(Theme.Glyph.Close);
            CloseButton.Click += (s, e) => Close();
        }

        protected FlatButton TitleButton(string glyph)
        {
            var b = new FlatButton
            {
                Glyph = glyph,
                Quiet = true,
                Size = new Size(Theme.S(34), Theme.S(30)),
                Font = Theme.Small,
                ForeColor = Theme.TextMuted
            };
            Controls.Add(b);
            return b;
        }

        // Right-to-left order of the title bar buttons.
        protected virtual FlatButton[] TitleButtons => new[] { CloseButton };

        protected void LayoutTitleButtons()
        {
            int x = ClientSize.Width - Theme.S(8);
            foreach (FlatButton b in TitleButtons)
            {
                if (b == null)
                    continue; // derived-class buttons not created yet (resize during construction)
                x -= b.Width;
                b.Location = new Point(x, (TitleHeight - b.Height) / 2);
                x -= Theme.S(2);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTitleButtons();
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < TitleHeight)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            int pad = Theme.S(14);
            int logoSize = Theme.S(24);
            if (Logo != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(Logo, new Rectangle(pad, (TitleHeight - logoSize) / 2, logoSize, logoSize));
            }
            int x = pad + logoSize + Theme.S(10);
            int tw = TextRenderer.MeasureText(g, Text, Theme.Title, Size.Empty, TextFormatFlags.NoPadding).Width;
            Theme.DrawText(g, Text, Theme.Title, new Rectangle(x, 0, tw + 4, TitleHeight), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(Subtitle))
                Theme.DrawText(g, Subtitle, Theme.Small, new Rectangle(x + tw + Theme.S(8), Theme.S(2), Theme.S(80), TitleHeight), Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, 0, TitleHeight - 1, ClientSize.Width, TitleHeight - 1);
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && Modal)
                Close();
        }
    }

    public class LogsWindow : BorderlessWindow
    {
        readonly Logger logger;
        readonly RichTextBox box;
        readonly FlatButton copy;

        public LogsWindow(Logger logger)
        {
            this.logger = logger;
            Text = L.T("logs.title");
            ClientSize = new Size(Theme.S(620), Theme.S(440));
            MinimumSize = Size;

            copy = new FlatButton { Glyph = Theme.Glyph.Copy, Text = L.T("logs.copy"), Font = Theme.Small, Height = Theme.S(28) };
            copy.Width = copy.PreferredWidth() + Theme.S(10);
            copy.Click += (s, e) => CopyAll();
            Controls.Add(copy);

            box = new RichTextBox
            {
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono,
                ReadOnly = true,
                DetectUrls = false,
                HideSelection = false,
                WordWrap = true
            };
            Controls.Add(box);
            LayoutBox();
            logger.AddLogs(box);
            logger.CurrentPanel = box;
        }

        protected override FlatButton[] TitleButtons => new[] { CloseButton, copy };

        void LayoutBox()
        {
            if (box == null)
                return;
            int pad = Theme.S(14);
            box.Bounds = new Rectangle(pad, TitleHeight + Theme.S(8), ClientSize.Width - pad * 2, ClientSize.Height - TitleHeight - Theme.S(8) - pad);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            logger.CurrentPanel = null;
            base.OnFormClosed(e);
        }

        void CopyAll()
        {
            string text = logger.AllText();
            if (text.Length == 0)
                return;
            try
            {
                Clipboard.SetText(text);
                copy.Text = L.T("logs.copied");
                copy.Glyph = Theme.Glyph.Check;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
