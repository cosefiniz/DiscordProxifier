using System.Windows;

namespace DiscordUnblocker.Glass
{
    // PLACEHOLDER — replaced by the full Liquid Glass logs window.
    public class GlassLogsWindow : GlassWindow
    {
        public GlassLogsWindow(Logger logger) : base(640, false, L.T("btn.logs"))
        {
        }

        public static Window CreatePreview(AppController app, string state)
        {
            return new GlassLogsWindow(app.Logger);
        }
    }
}
