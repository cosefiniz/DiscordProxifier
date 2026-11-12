using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DiscordUnblocker
{
    // What a note is about. A new note replaces the one of the same group on screen.
    public enum NoteGroup
    {
        General,
        Connection,
        Update,
        Problem
    }

    public class NoteAction
    {
        public string Label;
        public Func<Task> Run;
        public bool Primary;
    }

    public class Note
    {
        public NoteKind Kind;
        public NoteGroup Group;
        public string Title, Sub;
        public readonly List<NoteAction> Actions = new List<NoteAction>();
        public Action Open; // clicking the note itself

        // Errors, warnings, updates and problem cards always count; connection chatter doesn't.
        public bool Important => Kind == NoteKind.Error || Kind == NoteKind.Warning || Group == NoteGroup.Update || Group == NoteGroup.Problem;

        public int DurationMs => Group == NoteGroup.Update || Group == NoteGroup.Problem ? 12000 : Kind == NoteKind.Error ? 10000 : 6000;
    }

    // Shows notes on screen (the Liquid Glass banners).
    public interface INoteHost
    {
        void Show(Note n);
        void CloseAll();
    }

    // Decides whether, when and how a note appears while the window isn't in front:
    // the user's setting (all / important / off), quiet start with Windows, full-screen games
    // and presentations (important notes wait, others are dropped), and the sound.
    public class Notifier
    {
        readonly Configuration cfg;
        readonly INoteHost host;
        readonly Logger logger;
        readonly List<Note> waiting = new List<Note>();
        DateTime quietUntil = DateTime.MinValue;

        public Notifier(Configuration cfg, INoteHost host, Logger logger)
        {
            this.cfg = cfg;
            this.host = host;
            this.logger = logger;
        }

        // After starting with Windows, routine notes ("connected") stay quiet for a while.
        public void QuietFor(TimeSpan t)
        {
            quietUntil = DateTime.Now + t;
        }

        public void Post(Note n)
        {
            if (cfg.Notifications == "off")
                return;
            if (!n.Important && (cfg.Notifications == "important" || DateTime.Now < quietUntil))
                return;
            if (UserBusy())
            {
                if (n.Important)
                {
                    waiting.RemoveAll(w => w.Group == n.Group);
                    waiting.Add(n);
                }
                return;
            }
            Show(n);
        }

        // Shows a sample note (Settings → Notifications → Try), ignoring the mode.
        public void ShowSample(Note n)
        {
            Show(n);
        }

        void Show(Note n)
        {
            try
            {
                host.Show(n);
                if (cfg.NotificationSound)
                    Sounds.Play(n.Kind == NoteKind.Error || n.Kind == NoteKind.Warning);
            }
            catch (Exception ex)
            {
                logger.Log("Could not show a notification: " + ex.Message, LogType.WARNING);
            }
        }

        // Called every 500 ms: shows important notes that waited for a game or presentation to end.
        public void Tick()
        {
            if (waiting.Count == 0 || UserBusy())
                return;
            List<Note> now = waiting.ToList();
            waiting.Clear();
            foreach (Note n in now)
                Show(n);
        }

        public void CloseAll()
        {
            waiting.Clear();
            try
            {
                host.CloseAll();
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        // Full-screen game (D3D), presentation mode, "busy" full-screen app, or quiet time.
        public static bool UserBusy()
        {
            try
            {
                int s;
                if (SHQueryUserNotificationState(out s) != 0)
                    return false;
                return s == 2 || s == 3 || s == 4 || s == 6;
            }
            catch
            {
                return false;
            }
        }
    }

    // The app's own notification sounds (Assets/Sounds, made by tools/make-sounds.py).
    public static class Sounds
    {
        static SoundPlayer note, alert;

        public static void Play(bool isAlert)
        {
            try
            {
                SoundPlayer p = isAlert ? (alert ?? (alert = Load("alert.wav"))) : (note ?? (note = Load("note.wav")));
                p?.Play(); // asynchronous
            }
            catch { }
        }

        static SoundPlayer Load(string file)
        {
            Assembly asm = typeof(Sounds).Assembly;
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
            if (name == null)
                return null;
            var ms = new MemoryStream();
            using (Stream s = asm.GetManifestResourceStream(name))
                s.CopyTo(ms);
            ms.Position = 0;
            var p = new SoundPlayer(ms);
            p.Load();
            return p;
        }
    }
}
