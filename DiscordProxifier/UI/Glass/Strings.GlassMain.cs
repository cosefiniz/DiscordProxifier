using System.Collections.Generic;

namespace DiscordUnblocker
{
    public static partial class L
    {
        // key -> { English, Turkish }. New text for the GlassMain part of the Liquid Glass look.
        static readonly Dictionary<string, string[]> GlassMainStrings = new Dictionary<string, string[]>
        {
            ["glass.dismiss"] = new[] { "Dismiss", "Kapat" },
            ["glass.version"] = new[] { "Version {0}", "Sürüm {0}" },
            ["glass.client.pick"] = new[] { "Choose which Discord to use", "Kullanılacak Discord'u seçin" },
        };
    }
}
