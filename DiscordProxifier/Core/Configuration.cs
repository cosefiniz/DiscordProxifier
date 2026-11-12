using System;
using System.IO;
using Newtonsoft.Json;

namespace DiscordUnblocker
{
    // Persisted to Config.json next to the exe. Every UI change is saved immediately,
    // so the app always reopens in the state the user left it.
    public class Configuration
    {
        // General
        public string Language { get; set; } = "en"; // "en" or "tr"
        public bool OpenAtStartup { get; set; }
        public bool AutoConnect { get; set; } = true; // Tor bootstraps while the user is still getting ready
        public bool AutoRoute { get; set; } = true; // try Direct, WARP and Tor; keep the first that works
        public bool LaunchDiscordAfterConnect { get; set; }
        public bool AutoUpdateProxified { get; set; }
        public string SelectedClient { get; set; } // DiscordClient.Key
        public string[] CustomClientRoots { get; set; } = new string[0];

        // Look: "glass" (Liquid Glass, WPF) or "classic" (the 2.x WinForms windows).
        public string Appearance { get; set; } = "glass";
        // Liquid Glass background tone: "light" (first start), "mixed" or "dark".
        public string GlassTone { get; set; } = "light";

        // Notifications while the window isn't in front: "all", "important" or "off"; with sound.
        public string Notifications { get; set; } = "all";
        public bool NotificationSound { get; set; } = true;

        // Proxy chain
        public string Mode { get; set; } = "Tor";
        public string MainProxyPort { get; set; } = "42213";
        public string GostPort { get; set; } = "42212";

        // WARP mode
        public bool WarpTosAccepted { get; set; }
        public bool WarpFallbackToTor { get; set; } = true;
        public string WarpEndpoint { get; set; }

        // Tor mode: bridge lines from bridges.torproject.org, one per line.
        public string TorBridges { get; set; }

        static string FilePath => Path.Combine(AppContext.BaseDirectory, "Config.json");

        public static Configuration Load()
        {
            Configuration c = null;
            try
            {
                if (File.Exists(FilePath))
                    c = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(FilePath));
            }
            catch { }
            c = c ?? new Configuration();
            if (string.IsNullOrEmpty(c.Mode))
                c.Mode = "Tor";
            if (string.IsNullOrEmpty(c.MainProxyPort))
                c.MainProxyPort = "42213";
            if (string.IsNullOrEmpty(c.GostPort))
                c.GostPort = "42212";
            if (c.Language != "tr")
                c.Language = "en";
            if (c.Appearance != "classic")
                c.Appearance = "glass";
            if (c.GlassTone != "mixed" && c.GlassTone != "dark")
                c.GlassTone = "light";
            if (c.Notifications != "important" && c.Notifications != "off")
                c.Notifications = "all";
            c.CustomClientRoots = c.CustomClientRoots ?? new string[0];
            return c;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch { }
        }

        public Configuration Clone()
        {
            return JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(this));
        }

        // True when a running proxy chain must restart to pick up the change.
        public bool NetworkDiffers(Configuration o)
        {
            return MainProxyPort != o.MainProxyPort
                || GostPort != o.GostPort
                || (WarpEndpoint ?? "") != (o.WarpEndpoint ?? "")
                || (TorBridges ?? "") != (o.TorBridges ?? "");
        }
    }
}
