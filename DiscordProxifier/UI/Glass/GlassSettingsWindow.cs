using System.Windows;

namespace DiscordUnblocker.Glass
{
    // PLACEHOLDER — replaced by the full Liquid Glass settings window.
    public class GlassSettingsWindow : GlassWindow
    {
        public GlassSettingsWindow(AppController app, string focus) : base(720, false, L.T("settings.title"))
        {
            CloseOnEscape = true;
            ShowInTaskbar = false;
        }

        public static Window CreatePreview(AppController app, string state)
        {
            return new GlassSettingsWindow(app, null);
        }
    }
}
