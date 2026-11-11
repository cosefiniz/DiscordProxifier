using System.Windows;

namespace DiscordUnblocker.Glass
{
    // PLACEHOLDER — replaced by the full Liquid Glass repair window.
    public class GlassRepairWindow : GlassWindow, IRepairView
    {
        public GlassRepairWindow(AppController app) : base(440, false, L.T("settings.repair"))
        {
            ShowInTaskbar = false;
        }

        public int Step(string title) { return 0; }
        public void Done(int index, int status, string detail) { }
        public void Finish(bool allGood) { }

        public static Window CreatePreview(AppController app, string state)
        {
            return new GlassRepairWindow(app);
        }
    }
}
