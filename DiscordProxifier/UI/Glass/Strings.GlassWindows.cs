using System.Collections.Generic;

namespace DiscordUnblocker
{
    public static partial class L
    {
        // key -> { English, Turkish }. New text for the GlassWindows part of the Liquid Glass look.
        static readonly Dictionary<string, string[]> GlassWindowsStrings = new Dictionary<string, string[]>
        {
            // Buttons
            ["btn.ok"] = new[] { "OK", "Tamam" },
            ["btn.create"] = new[] { "Create", "Oluştur" },
            ["btn.open"] = new[] { "Open", "Aç" },

            // Settings
            ["settings.version"] = new[] { "Version {0}", "Sürüm {0}" },
            ["settings.connection"] = new[] { "Connection", "Bağlantı" },
            ["settings.shortcuts.sub"] = new[] { "Fixes Discord shortcuts that start through the proxy after a Discord update or a port change", "Discord güncellenince ya da port değişince proxy ile açılan Discord kısayollarını düzeltir" },
            ["settings.shortcut.row"] = new[] { "Desktop and Start menu shortcut", "Masaüstü ve Başlat menüsü kısayolu" },
            ["settings.ports"] = new[] { "Ports", "Portlar" },
            ["settings.ports.hint"] = new[] { "Change these only if another program already uses them.", "Bunları yalnızca başka bir program kullanıyorsa değiştirin." },
            ["settings.updates.hint"] = new[] { "Downloads come only from the project's GitHub releases and are checked against the published checksums before they are installed.", "İndirmeler yalnızca projenin GitHub sürümlerinden yapılır ve kurulmadan önce yayınlanan sağlama değerleriyle karşılaştırılır." },
            ["settings.repair.row"] = new[] { "Check and repair", "Kontrol et ve onar" },
            ["settings.repair.sub"] = new[] { "Program files, leftover processes, Discord and the connection", "Program dosyaları, kalan süreçler, Discord ve bağlantı" },
            ["settings.report.row"] = new[] { "Diagnostic report", "Tanılama raporu" },
            ["settings.report.sub"] = new[] { "Saved to your desktop; send it when asking for help", "Masaüstüne kaydedilir; yardım isterken bunu gönderin" },
            ["settings.logs.sub"] = new[] { "What the app has been doing, step by step", "Uygulamanın adım adım yaptıkları" },
            ["settings.folder.row"] = new[] { "App folder", "Uygulama klasörü" },

            // Appearance
            ["look.confirm.title"] = new[] { "Switch to the classic style?", "Klasik stile geçilsin mi?" },
            ["look.confirm.ok"] = new[] { "Restart", "Yeniden başlat" },
            ["look.tone.hint"] = new[] { "Changes right away in every window.", "Tüm pencerelerde hemen değişir." },

            // Logs and repair
            ["logs.count"] = new[] { "{0} lines", "{0} satır" },
            ["repair.intro"] = new[] { "Checks the program files, leftover processes, Discord and the connection, and fixes what it can.", "Program dosyalarını, kalan süreçleri, Discord'u ve bağlantıyı kontrol eder; düzeltebildiklerini düzeltir." },
        };
    }
}
