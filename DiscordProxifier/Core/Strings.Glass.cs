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
                    foreach (var t in new[] { AppearanceStrings, GlassMainStrings, GlassWindowsStrings })
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
    }
}
