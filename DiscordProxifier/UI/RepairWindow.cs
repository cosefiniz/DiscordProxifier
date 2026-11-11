using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // One line of the repair log: status glyph, title and a detail line.
    public class StepRow : DrawnControl
    {
        public string Title = "", Detail = "";
        public int Status = -1; // -1 running, 0 ok, 1 warning, 2 failed
        float phase;
        readonly Action tick;

        public StepRow()
        {
            Height = Theme.S(46);
            tick = () =>
            {
                phase += 0.08f;
                Invalidate();
            };
            Anim.AddTicker(tick);
        }

        public void Set(int status, string detail)
        {
            Status = status;
            Detail = detail ?? "";
            Anim.RemoveTicker(tick);
            Invalidate();
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
            string glyph;
            Color c;
            switch (Status)
            {
                case 0:
                    glyph = Theme.Glyph.Check;
                    c = Theme.Success;
                    break;
                case 1:
                    glyph = Theme.Glyph.Warning;
                    c = Theme.Warning;
                    break;
                case 2:
                    glyph = Theme.Glyph.Warning;
                    c = Theme.Danger;
                    break;
                default:
                    glyph = Theme.Glyph.Refresh;
                    c = Theme.Blend(Theme.TextFaint, Theme.Accent, 0.5f + 0.5f * (float)Math.Sin(phase));
                    break;
            }
            Theme.DrawText(g, glyph, Theme.Icons(11), new Rectangle(0, Theme.S(4), Theme.S(24), Theme.S(20)), c, TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Title, Theme.Strong, new Rectangle(Theme.S(30), Theme.S(2), Width - Theme.S(30), Theme.S(22)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string detail = Status < 0 ? L.T("repair.running") : Detail;
            Theme.DrawText(g, detail, Theme.Small, new Rectangle(Theme.S(30), Theme.S(24), Width - Theme.S(30), Theme.S(18)), Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Shows the repair steps live, then offers to create a diagnostic report.
    public class RepairWindow : BorderlessWindow
    {
        readonly List<StepRow> rows = new List<StepRow>();
        readonly TextLine summary;
        readonly FlatButton report, close;
        public Func<FlatButton, System.Threading.Tasks.Task> ReportClicked;

        public RepairWindow()
        {
            Text = L.T("repair.title");
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            summary = new TextLine { Font = Theme.Body, ForeColor = Theme.TextMuted, Height = Theme.S(22) };
            report = new FlatButton { Glyph = Theme.Glyph.Logs, Text = L.T("settings.report"), Font = Theme.Small, Height = Theme.S(30), Enabled = false };
            report.Click += async (s, e) =>
            {
                if (ReportClicked != null)
                    await ReportClicked(report);
            };
            close = new FlatButton { Primary = true, Text = L.T("btn.close"), Height = Theme.S(30), Enabled = false };
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { summary, report, close });
            DoLayout();
        }

        public int Step(string title)
        {
            var row = new StepRow { Title = title };
            rows.Add(row);
            Controls.Add(row);
            DoLayout();
            return rows.Count - 1;
        }

        public void Done(int i, int status, string detail)
        {
            rows[i].Set(status, detail);
        }

        public void Finish(bool allGood)
        {
            summary.ForeColor = allGood ? Theme.Success : Theme.Warning;
            summary.Text = L.T(allGood ? "repair.summary.ok" : "repair.summary.issues");
            report.Enabled = true;
            close.Enabled = true;
        }

        void DoLayout()
        {
            int pad = Theme.S(18);
            int w = Theme.S(420);
            int y = TitleHeight + Theme.S(14);
            foreach (StepRow r in rows)
            {
                r.SetBounds(pad, y, w - pad * 2, Theme.S(46));
                y += Theme.S(50);
            }
            y += Theme.S(4);
            summary.SetBounds(pad, y, w - pad * 2, Theme.S(22));
            y += Theme.S(34);
            close.Width = Math.Max(Theme.S(80), close.PreferredWidth());
            report.Width = report.PreferredWidth();
            close.Location = new Point(w - pad - close.Width, y);
            report.Location = new Point(close.Left - Theme.S(8) - report.Width, y);
            y += Theme.S(30) + pad;
            ClientSize = new Size(w, y);
            LayoutTitleButtons();
        }
    }
}
