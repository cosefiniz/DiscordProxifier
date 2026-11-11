using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    public enum LogType
    {
        NORMAL,
        WARNING,
        ERROR
    }

    public class Log
    {
        public LogType Type { get; set; }
        public string Text { get; set; }
        public DateTime Time { get; set; }
    }

    // Thread-safe in-memory log. Child process output arrives on thread-pool threads,
    // so writes to the Logs window are marshalled to its UI thread.
    public class Logger
    {
        readonly List<Log> logs = new List<Log>();

        // The Logs window's text box while it is open; null otherwise.
        public RichTextBox CurrentPanel;

        // Raised for every new line, on the thread that logged it (the glass Logs window listens).
        public event Action<Log> Added;

        public void Log(string text, LogType type = LogType.NORMAL)
        {
            var nl = new Log { Text = text, Type = type, Time = DateTime.UtcNow };
            lock (logs)
                logs.Add(nl);
            try
            {
                Added?.Invoke(nl);
            }
            catch { }
            RichTextBox box = CurrentPanel;
            if (box == null || box.IsDisposed || !box.IsHandleCreated)
                return;
            if (box.InvokeRequired)
            {
                try
                {
                    box.BeginInvoke(new Action(() => AddLog(nl, box)));
                }
                catch (InvalidOperationException) { }
            }
            else
            {
                AddLog(nl, box);
            }
        }

        static Color GetLogColor(LogType type)
        {
            switch (type)
            {
                case LogType.WARNING:
                    return Theme.Warning;
                case LogType.ERROR:
                    return Theme.Danger;
                default:
                    return Theme.Text;
            }
        }

        public static string Format(Log l)
        {
            return $"[{l.Time.ToLocalTime():HH:mm:ss}][{l.Type}] {l.Text}";
        }

        public void AddLog(Log l, RichTextBox box)
        {
            if (box.IsDisposed)
                return;
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = GetLogColor(l.Type);
            box.AppendText(Format(l) + Environment.NewLine);
            box.SelectionColor = box.ForeColor;
            box.ScrollToCaret();
        }

        public void AddLogs(RichTextBox box)
        {
            List<Log> snapshot;
            lock (logs)
                snapshot = logs.ToList();
            foreach (Log l in snapshot)
                AddLog(l, box);
        }

        public List<Log> Snapshot()
        {
            lock (logs)
                return logs.ToList();
        }

        // Plain text of every log line, for the clipboard.
        public string AllText()
        {
            lock (logs)
                return string.Join(Environment.NewLine, logs.Select(Format));
        }
    }
}
