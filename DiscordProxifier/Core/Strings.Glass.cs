using System.Collections.Generic;
using System.Linq;

namespace DiscordUnblocker
{
    // Text added with the Liquid Glass look (v3). Each table lives next to the code that uses it
    // (Strings.GlassMain.cs, Strings.GlassWindows.cs); L.T looks here after the main table.
    public static partial class L
    {
        static Dictionary<string, string[]> glassTable;

        static Dictionary<string, string[]> GlassTable
        {
            get
            {
                if (glassTable == null)
                {
                    var d = new Dictionary<string, string[]>();
                    foreach (var t in new[] { AppearanceStrings, NotifyStrings, GlassMainStrings, GlassWindowsStrings })
                        foreach (var kv in t.Where(kv => !d.ContainsKey(kv.Key)))
                            d[kv.Key] = kv.Value;
                    glassTable = d;
                }
                return glassTable;
            }
        }

        // key -> { English, Turkish }
        static readonly Dictionary<string, string[]> AppearanceStrings = new Dictionary<string, string[]>
        {
            ["look.style"] = new[] { "Style", "Stil" },
            ["look.glass"] = new[] { "Liquid Glass", "Liquid Glass" },
            ["look.classic"] = new[] { "Classic", "Klasik" },
            ["look.confirm"] = new[] { "Restart the app to change the style? The connection pauses for a few seconds.", "Stili değiştirmek için uygulama yeniden başlatılsın mı? Bağlantı birkaç saniye duraklar." },
            ["look.restart.hint"] = new[] { "The app restarts to change the style", "Stil değişince uygulama yeniden başlar" },
            ["look.tone"] = new[] { "Background", "Arka plan" },
            ["look.tone.light"] = new[] { "Light", "Açık" },
            ["look.tone.mixed"] = new[] { "Mixed", "Karışık" },
            ["look.tone.dark"] = new[] { "Dark", "Koyu" },
            ["look.appearance"] = new[] { "Appearance", "Görünüm" },
        };

        // Notifications (v3.1)
        static readonly Dictionary<string, string[]> NotifyStrings = new Dictionary<string, string[]>
        {
            ["settings.notify"] = new[] { "Notifications", "Bildirimler" },
            ["notify.mode"] = new[] { "Show notifications", "Bildirimleri göster" },
            ["notify.all"] = new[] { "All", "Hepsi" },
            ["notify.important"] = new[] { "Important", "Önemli" },
            ["notify.off"] = new[] { "Off", "Kapalı" },
            ["notify.mode.hint"] = new[] { "Shown in the corner of the screen when the window isn't in front. Important: errors, a connection that didn't come back, and updates.", "Pencere önde değilken ekranın köşesinde gösterilir. Önemli: hatalar, geri gelmeyen bağlantı ve güncellemeler." },
            ["notify.sound"] = new[] { "Notification sound", "Bildirim sesi" },
            ["notify.sound.sub"] = new[] { "A soft chime of its own; errors use a lower tone", "Uygulamaya özel yumuşak bir çan sesi; hatalarda daha pes bir ton" },
            ["notify.test"] = new[] { "Try", "Dene" },
            ["notify.test.row"] = new[] { "See what a notification looks like", "Bildirimin nasıl göründüğüne bakın" },
            ["notify.test.t"] = new[] { "Notifications look like this", "Bildirimler böyle görünür" },
            ["notify.test.d"] = new[] { "Discord Proxifier tells you here when something needs you.", "Bir şey gerektiğinde Discord Proxifier burada haber verir." },
            ["notify.busy.hint"] = new[] { "Nothing appears over full-screen games or presentations; important notes wait until you're back.", "Tam ekran oyun veya sunum sırasında bildirim çıkmaz; önemli olanlar siz dönünce gösterilir." },
            ["note.now"] = new[] { "now", "şimdi" },
            ["note.open"] = new[] { "Open", "Aç" },
            ["note.reconnected.n"] = new[] { "Reconnected {0} times · {1}", "{0} kez yeniden bağlandı · {1}" },
        };
    }
}
