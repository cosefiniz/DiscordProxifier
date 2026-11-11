using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // Base for custom-drawn controls: double buffered, repaints on resize, takes the parent's colour.
    public class DrawnControl : Control
    {
        public DrawnControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint
                    | ControlStyles.SupportsTransparentBackColor,
                true
            );
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
        }

        protected Color Surround => Parent != null ? Parent.BackColor : Theme.Bg;
    }

    // Rounded panel; children should use BackColor = Theme.Surface.
    public class Card : Panel
    {
        public Color Fill = Theme.Surface;
        public Color Stroke = Theme.Border;
        public float Radius = 10;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            // Children paint their background with Parent.BackColor, so it must match the fill.
            BackColor = Fill;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.Smooth(g);
            using (GraphicsPath p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(Radius)))
            using (var b = new SolidBrush(Fill))
            using (var pen = new Pen(Stroke))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }
    }

    public class FlatButton : DrawnControl
    {
        public string Glyph;
        public bool Primary;
        public bool Quiet; // no fill until hovered (title bar buttons)
        public Color? GlyphColor;
        float hover;
        bool pressed;

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(90), Theme.S(30));
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Anim.Run(this, hover, 1, 140, v => { hover = v; Invalidate(); });
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            pressed = false;
            Anim.Run(this, hover, 0, 200, v => { hover = v; Invalidate(); });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        // Width that fits the glyph and text.
        public int PreferredWidth()
        {
            int w = Theme.S(24);
            if (!string.IsNullOrEmpty(Glyph))
                w += Theme.S(16) + (string.IsNullOrEmpty(Text) ? 0 : Theme.S(6));
            if (!string.IsNullOrEmpty(Text))
                w += TextRenderer.MeasureText(Text, Font).Width;
            return w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surround);
            Theme.Smooth(g);
            Color baseFill = Primary ? Theme.Accent : Quiet ? Surround : Theme.Control;
            Color hoverFill = Primary ? Theme.AccentHover : Theme.ControlHover;
            Color fill = Theme.Blend(baseFill, hoverFill, hover);
            if (pressed)
                fill = Theme.Blend(fill, Theme.Bg, 0.25f);
            if (!Enabled)
                fill = Theme.Blend(baseFill, Surround, 0.5f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Round(r, Theme.S(7)))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, p);
                if (!Primary && !Quiet)
                    using (var pen = new Pen(Theme.Blend(Theme.BorderStrong, Theme.TextFaint, hover * 0.4f)))
                        g.DrawPath(pen, p);
            }

            Color fg = Enabled ? (Primary ? Color.White : ForeColor) : Theme.TextFaint;
            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasGlyph = !string.IsNullOrEmpty(Glyph);
            Font icon = Theme.Icons(Font.SizeInPoints * 0.95f);
            int glyphW = hasGlyph ? TextRenderer.MeasureText(g, Glyph, icon, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
            int textW = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
            int gap = hasGlyph && hasText ? Theme.S(6) : 0;
            int x = (Width - glyphW - gap - textW) / 2;
            if (hasGlyph)
            {
                Theme.DrawText(g, Glyph, icon, new Rectangle(x, 0, glyphW + 2, Height), GlyphColor ?? fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += glyphW + gap;
            }
            if (hasText)
                Theme.DrawText(g, Text, Font, new Rectangle(x, 0, textW + 4, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                OnClick(EventArgs.Empty);
        }
    }

    // Two-option segmented switch with a sliding thumb (Tor/WARP, English/Türkçe).
    public class SegmentSwitch : DrawnControl
    {
        public string[] Labels = { "A", "B" };
        public string[] Glyphs = { null, null };
        public bool Busy; // dims the control and ignores clicks
        int index;
        float pos; // segment index, animated (0 = leftmost)
        int hover = -1;

        public event Action<int> UserChanged;

        public SegmentSwitch()
        {
            Cursor = Cursors.Hand;
            Height = Theme.S(40);
        }

        public int Index => index;
        int Count => Math.Max(1, Labels.Length);
        float Pad => Theme.S(3);
        float Cell => (Width - Pad * 2) / Count;

        int CellAt(int x)
        {
            return Math.Max(0, Math.Min(Count - 1, (int)((x - Pad) / Cell)));
        }

        public void SetIndex(int i, bool animate)
        {
            index = i;
            Anim.Run(this, pos, i, animate ? 280 : 0, v => { pos = v; Invalidate(); });
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = CellAt(e.X);
            if (h != hover)
            {
                hover = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (Busy)
                return;
            int i = CellAt(e.X);
            if (i == index)
                return;
            SetIndex(i, true);
            UserChanged?.Invoke(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surround);
            Theme.Smooth(g);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Round(outer, Theme.S(9)))
            using (var b = new SolidBrush(Color.FromArgb(20, 21, 23)))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            float pad = Pad, cell = Cell;
            var thumb = new RectangleF(pad + pos * cell, pad, cell, Height - pad * 2 - 1);
            using (GraphicsPath p = Theme.Round(thumb, Theme.S(7)))
            using (var b = new SolidBrush(Busy ? Theme.Control : Theme.ControlHover))
            using (var pen = new Pen(Theme.BorderStrong))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            for (int i = 0; i < Count; i++)
            {
                float on = Math.Max(0, 1 - Math.Abs(pos - i));
                Color c = Theme.Blend(hover == i ? Theme.Text : Theme.TextMuted, Theme.Text, on);
                if (Busy)
                    c = Theme.Blend(c, Theme.TextFaint, 0.5f);
                var r = new Rectangle((int)(pad + i * cell), 0, (int)cell, Height);
                string label = Labels[i];
                string glyph = Glyphs != null && i < Glyphs.Length ? Glyphs[i] : null;
                Font icon = Theme.Icons(Font.SizeInPoints);
                int gw = glyph != null ? TextRenderer.MeasureText(g, glyph, icon, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
                int tw = TextRenderer.MeasureText(g, label, Theme.Strong, Size.Empty, TextFormatFlags.NoPadding).Width;
                int gap = glyph != null ? Theme.S(6) : 0;
                int x = r.X + (r.Width - gw - gap - tw) / 2;
                if (glyph != null)
                    Theme.DrawText(g, glyph, icon, new Rectangle(x, 0, gw + 2, Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.DrawText(g, label, Theme.Strong, new Rectangle(x + gw + gap, 0, tw + 4, Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // Small on/off switch with a label on the right.
    public class ToggleSwitch : DrawnControl
    {
        bool isOn;
        float knob;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Cursor = Cursors.Hand;
            Height = Theme.S(24);
        }

        public bool Checked
        {
            get { return isOn; }
            set
            {
                isOn = value;
                knob = value ? 1 : 0;
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            isOn = !isOn;
            Anim.Run(this, knob, isOn ? 1 : 0, 200, v => { knob = v; Invalidate(); });
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surround);
            Theme.Smooth(g);
            float w = Theme.S(34), h = Theme.S(18);
            float y = (Height - h) / 2f;
            var track = new RectangleF(0.5f, y, w, h);
            using (GraphicsPath p = Theme.Round(track, h / 2))
            using (var b = new SolidBrush(Theme.Blend(Theme.Control, Theme.Accent, knob)))
            using (var pen = new Pen(Theme.Blend(Theme.BorderStrong, Theme.Accent, knob)))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            float d = h - Theme.S(6);
            float kx = track.X + Theme.S(3) + knob * (w - d - Theme.S(6));
            using (var b = new SolidBrush(Theme.Blend(Theme.TextMuted, Color.White, knob)))
                g.FillEllipse(b, kx, y + Theme.S(3), d, d);
            Theme.DrawText(g, Text, Font, new Rectangle((int)w + Theme.S(10), 0, Width - (int)w - Theme.S(10), Height),
                Enabled ? Theme.Text : Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Coloured status dot; pulses while connecting.
    public class StatusDot : DrawnControl
    {
        Color color = Theme.Idle;
        bool pulse;
        readonly Action tick;
        float phase;

        public StatusDot()
        {
            Size = new Size(Theme.S(12), Theme.S(12));
            tick = () =>
            {
                phase += 0.06f;
                Invalidate();
            };
        }

        public void Set(Color c, bool pulsing)
        {
            if (c != color)
            {
                Color from = color;
                Anim.Run(this, 0, 1, 250, v => { color = Theme.Blend(from, c, v); Invalidate(); });
            }
            if (pulsing != pulse)
            {
                pulse = pulsing;
                if (pulse)
                    Anim.AddTicker(tick);
                else
                    Anim.RemoveTicker(tick);
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            Anim.RemoveTicker(tick);
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surround);
            Theme.Smooth(g);
            float a = pulse ? 0.45f + 0.55f * (0.5f + 0.5f * (float)Math.Cos(phase)) : 1f;
            float d = Math.Min(Width, Height) - 2;
            using (var halo = new SolidBrush(Color.FromArgb((int)(40 * a), color)))
                g.FillEllipse(halo, 0, 0, d + 1, d + 1);
            float inner = d * 0.62f;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), color)))
                g.FillEllipse(b, (d - inner) / 2 + 0.5f, (d - inner) / 2 + 0.5f, inner, inner);
        }
    }

    // Thin progress line; Value < 0 means indeterminate.
    public class ProgressLine : DrawnControl
    {
        float shown;
        float value = -2; // -2 hidden
        float sweep;
        readonly Action tick;

        public ProgressLine()
        {
            Height = Theme.S(3);
            tick = () =>
            {
                sweep = (sweep + 0.012f) % 1.4f;
                Invalidate();
            };
        }

        // null hides the line, negative = indeterminate, 0..1 = determinate.
        public void Set(float? v)
        {
            if (v == null)
            {
                value = -2;
                Anim.RemoveTicker(tick);
                Invalidate();
                return;
            }
            if (v < 0)
            {
                value = -1;
                Anim.AddTicker(tick);
                return;
            }
            Anim.RemoveTicker(tick);
            if (value < 0)
                shown = 0;
            value = v.Value;
            Anim.Run(this, shown, value, 350, x => { shown = x; Invalidate(); });
        }

        protected override void Dispose(bool disposing)
        {
            Anim.RemoveTicker(tick);
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surround);
            if (value <= -2)
                return;
            Theme.Smooth(g);
            using (var track = new SolidBrush(Theme.Border))
            using (GraphicsPath p = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f))
                g.FillPath(track, p);
            float x0, x1;
            if (value < 0)
            {
                x0 = (sweep - 0.4f) * Width;
                x1 = sweep * Width;
            }
            else
            {
                x0 = 0;
                x1 = shown * Width;
            }
            x0 = Math.Max(0, x0);
            x1 = Math.Min(Width, x1);
            if (x1 - x0 < 1)
                return;
            using (var b = new SolidBrush(Theme.Accent))
            using (GraphicsPath p = Theme.Round(new RectangleF(x0, 0, x1 - x0, Height), Height / 2f))
                g.FillPath(b, p);
        }
    }

    // Plain label drawn with the theme fonts (WinForms Label flickers on dark backgrounds).
    public class TextLine : DrawnControl
    {
        public TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        public TextLine()
        {
            Height = Theme.S(20);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Surround);
            Theme.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, Flags);
        }

        public int MeasureHeight(int width)
        {
            return TextRenderer.MeasureText(Text, Font, new Size(width, 0), TextFormatFlags.WordBreak).Height;
        }
    }

    // Toast that slides up from the bottom of its parent and hides itself.
    public class Toast : DrawnControl
    {
        public enum Kind
        {
            Success,
            Info,
            Warning,
            Error
        }

        string title = "", sub = "";
        Kind kind;
        float shown; // 0 hidden .. 1 visible
        readonly Timer hideTimer = new Timer();

        public Toast()
        {
            Visible = false;
            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                Hide(true);
            };
            Click += (s, e) => Hide(true);
            Cursor = Cursors.Hand;
        }

        int RestY => Parent.ClientSize.Height - Height - Theme.S(12);
        int HiddenY => Parent.ClientSize.Height + Theme.S(4);

        public void ShowToast(Kind k, string t, string s, int ms = 3200)
        {
            kind = k;
            title = t ?? "";
            sub = s ?? "";
            Height = Theme.S(string.IsNullOrEmpty(sub) ? 44 : 58);
            Width = Parent.ClientSize.Width - Theme.S(24);
            Left = Theme.S(12);
            using (GraphicsPath p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.S(10)))
                Region = new Region(p);
            Visible = true;
            BringToFront();
            Anim.Run(this, shown, 1, 320, v =>
            {
                shown = v;
                if (Parent != null && !IsDisposed) // the window may close mid-animation
                    Top = (int)(HiddenY + (RestY - HiddenY) * v);
            });
            Invalidate();
            hideTimer.Stop();
            hideTimer.Interval = ms;
            hideTimer.Start();
        }

        public void Hide(bool animate)
        {
            Anim.Run(this, shown, 0, animate ? 260 : 0, v =>
            {
                shown = v;
                if (Parent != null && !IsDisposed)
                    Top = (int)(HiddenY + (RestY - HiddenY) * v);
            }, () =>
            {
                if (!IsDisposed)
                    Visible = false;
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(36, 39, 43));
            Theme.Smooth(g);
            using (GraphicsPath p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10)))
            using (var pen = new Pen(Theme.BorderStrong))
                g.DrawPath(pen, p);
            Color c;
            string glyph;
            switch (kind)
            {
                case Kind.Success:
                    c = Theme.Success;
                    glyph = Theme.Glyph.Check;
                    break;
                case Kind.Warning:
                    c = Theme.Warning;
                    glyph = Theme.Glyph.Warning;
                    break;
                case Kind.Error:
                    c = Theme.Danger;
                    glyph = Theme.Glyph.Warning;
                    break;
                default:
                    c = Theme.Accent;
                    glyph = Theme.Glyph.Info;
                    break;
            }
            int pad = Theme.S(14);
            Theme.DrawText(g, glyph, Theme.Icons(12), new Rectangle(pad, 0, Theme.S(22), Height), c, TextFormatFlags.VerticalCenter);
            int x = pad + Theme.S(28);
            int w = Width - x - pad;
            if (string.IsNullOrEmpty(sub))
            {
                Theme.DrawText(g, title, Theme.Strong, new Rectangle(x, 0, w, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                Theme.DrawText(g, title, Theme.Strong, new Rectangle(x, Theme.S(9), w, Theme.S(20)), Theme.Text, TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, sub, Theme.Small, new Rectangle(x, Theme.S(29), w, Theme.S(20)), Theme.TextMuted, TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Expandable card that explains an AppError and offers one-click fixes.
    public class ErrorCard : Panel
    {
        AppError error;
        readonly List<FlatButton> buttons = new List<FlatButton>();
        readonly FlatButton close;
        float open; // 0..1
        int fullHeight;

        public event Action<ErrorAction> ActionClicked;
        public event Action Dismissed;
        public event Action HeightChanging;
        public Func<ErrorAction, string> ActionLabel = a => L.T("action." + a);

        public ErrorCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.DangerBg;
            Height = 0;
            Visible = false;
            close = new FlatButton { Glyph = Theme.Glyph.Close, Quiet = true, Size = new Size(Theme.S(26), Theme.S(26)), Font = Theme.Small };
            close.Click += (s, e) => Collapse();
            Controls.Add(close);
        }

        public AppError Error => error;
        Color CardBg => error != null && error.IsInfo ? Theme.InfoBg : Theme.DangerBg;
        public int CurrentHeight => (int)(fullHeight * open);

        public void ShowError(AppError e)
        {
            error = e;
            BackColor = CardBg;
            foreach (FlatButton b in buttons)
            {
                Controls.Remove(b);
                b.Dispose();
            }
            buttons.Clear();
            foreach (ErrorAction a in e.Actions)
            {
                var b = new FlatButton { Text = ActionLabel(a), Font = Theme.Small, BackColor = CardBg };
                b.Height = Theme.S(28);
                b.Width = b.PreferredWidth();
                ErrorAction act = a;
                b.Click += (s, ev) => ActionClicked?.Invoke(act);
                buttons.Add(b);
                Controls.Add(b);
            }
            Relayout();
            Visible = true;
            Anim.Run(this, open, 1, 340, v =>
            {
                open = v;
                Height = CurrentHeight;
                HeightChanging?.Invoke();
            });
            Invalidate();
        }

        // Re-measures after a width or language change.
        public void Relayout()
        {
            if (error == null)
                return;
            int pad = Theme.S(14);
            int textW = Width - pad * 2;
            int descH = TextRenderer.MeasureText(error.Description, Theme.Small, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
            int y = pad + Theme.S(24) + Theme.S(6) + descH + Theme.S(12);
            int x = pad;
            foreach (FlatButton b in buttons)
            {
                b.Text = ActionLabel(ActionOf(b));
                b.Width = b.PreferredWidth();
                b.Location = new Point(x, y);
                x += b.Width + Theme.S(6);
            }
            fullHeight = y + Theme.S(28) + pad;
            close.Location = new Point(Width - pad - close.Width + Theme.S(6), pad - Theme.S(4));
            if (open >= 1)
                Height = fullHeight;
            Invalidate();
        }

        ErrorAction ActionOf(FlatButton b)
        {
            return error.Actions[buttons.IndexOf(b)];
        }

        public void Collapse(bool notify = true)
        {
            if (!Visible)
                return;
            Anim.Run(this, open, 0, 260, v =>
            {
                open = v;
                Height = CurrentHeight;
                HeightChanging?.Invoke();
            }, () =>
            {
                Visible = false;
                error = null;
                if (notify)
                    Dismissed?.Invoke();
            });
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.Smooth(g);
            using (GraphicsPath p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Math.Max(2, fullHeight) - 1.5f), Theme.S(10)))
            using (var b = new SolidBrush(CardBg))
            using (var pen = new Pen(error != null && error.IsInfo ? Theme.InfoBorder : Theme.DangerBorder))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (error == null)
                return;
            Graphics g = e.Graphics;
            int pad = Theme.S(14);
            Color accent = error.IsInfo ? Theme.Accent : Theme.Danger;
            Theme.DrawText(g, error.IsInfo ? Theme.Glyph.Info : Theme.Glyph.Warning, Theme.Icons(12), new Rectangle(pad, pad, Theme.S(22), Theme.S(24)), accent, TextFormatFlags.VerticalCenter);
            int codeW = TextRenderer.MeasureText(error.DisplayCode, Theme.Mono).Width;
            Theme.DrawText(g, error.Title, Theme.Strong, new Rectangle(pad + Theme.S(26), pad, Width - pad * 2 - Theme.S(26) - codeW - Theme.S(30), Theme.S(24)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, error.DisplayCode, Theme.Mono, new Rectangle(Width - pad - Theme.S(30) - codeW, pad, codeW, Theme.S(24)), accent, TextFormatFlags.VerticalCenter);
            int textW = Width - pad * 2;
            Theme.DrawText(g, error.Description, Theme.Small, new Rectangle(pad, pad + Theme.S(30), textW, 400), error.IsInfo ? Theme.InfoText : Theme.DangerText, TextFormatFlags.WordBreak);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (error != null && open >= 1)
                Relayout();
        }
    }

    // Dark renderer for context menus (tray, client picker).
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextMuted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            Theme.DrawText(e.Graphics, Theme.Glyph.Check, Theme.Icons(9), r, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color MenuBorder => Theme.BorderStrong;
            public override Color MenuItemBorder => Theme.ControlHover;
            public override Color MenuItemSelected => Theme.ControlHover;
            public override Color MenuItemSelectedGradientBegin => Theme.ControlHover;
            public override Color MenuItemSelectedGradientEnd => Theme.ControlHover;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
            public override Color CheckBackground => Theme.Surface;
            public override Color CheckSelectedBackground => Theme.ControlHover;
            public override Color CheckPressedBackground => Theme.ControlHover;
        }
    }
}
