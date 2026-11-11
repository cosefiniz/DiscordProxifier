using System;
using System.Collections.Generic;

namespace DiscordUnblocker
{
    // UI text in English (default) and Turkish. Only these two languages are supported.
    // Logs stay in English so they can be shared and compared.
    public static partial class L
    {
        public static string Lang { get; set; } = "en";

        public static event Action Changed;

        public static void Set(string lang)
        {
            lang = lang == "tr" ? "tr" : "en";
            if (lang == Lang)
                return;
            Lang = lang;
            Changed?.Invoke();
        }

        public static string T(string key)
        {
            string[] v;
            if (!Table.TryGetValue(key, out v) && !GlassTable.TryGetValue(key, out v))
                return key;
            return Lang == "tr" ? v[1] : v[0];
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        // key -> { English, Turkish }
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // Status card
            ["status.down"] = new[] { "Disconnected", "Bağlı değil" },
            ["status.connecting"] = new[] { "Connecting", "Bağlanıyor" },
            ["status.connected"] = new[] { "Connected", "Bağlı" },
            ["status.error"] = new[] { "Connection failed", "Bağlanamadı" },
            ["status.sub.down"] = new[] { "Connect to use Discord through the proxy", "Discord'u proxy ile kullanmak için bağlanın" },
            ["status.sub.connected"] = new[] { "Discord answers in {0} ms · {1}", "Discord {0} ms'de yanıt veriyor · {1}" },
            ["status.sub.unhealthy"] = new[] { "{0} stopped unexpectedly", "{0} beklenmedik şekilde durdu" },
            ["status.sub.error"] = new[] { "See the details below", "Ayrıntılar aşağıda" },
            ["btn.connect"] = new[] { "Connect", "Bağlan" },
            ["btn.disconnect"] = new[] { "Disconnect", "Bağlantıyı kes" },
            ["btn.cancel"] = new[] { "Cancel", "İptal" },
            ["btn.done"] = new[] { "Done", "Tamam" },
            ["tip.tray"] = new[] { "Hide to tray", "Tepsiye gizle" },
            ["tip.minimize"] = new[] { "Minimize", "Simge durumuna küçült" },

            // Progress (set by the proxy engine)
            ["progress.gost"] = new[] { "Starting local proxy", "Yerel proxy başlatılıyor" },
            ["progress.tor"] = new[] { "Connecting to Tor · {0}%", "Tor'a bağlanılıyor · %{0}" },
            ["progress.warp.register"] = new[] { "Registering WARP device", "WARP cihazı kaydediliyor" },
            ["progress.warp.profile"] = new[] { "Creating WARP profile", "WARP profili oluşturuluyor" },
            ["progress.warp.connect"] = new[] { "Connecting to WARP", "WARP'a bağlanılıyor" },
            ["progress.warp.activating"] = new[] { "Waiting for the new WARP account to activate", "Yeni WARP hesabının etkinleşmesi bekleniyor" },
            ["progress.direct"] = new[] { "Starting direct connection", "Doğrudan bağlantı başlatılıyor" },
            ["mode.direct"] = new[] { "Direct", "Doğrudan" },
            ["route.direct.hint"] = new[] { "Fastest; no relay in between", "En hızlısı; araya sunucu girmez" },
            ["progress.auto"] = new[] { "Trying {0}", "{0} deneniyor" },
            ["autoroute"] = new[] { "Pick the fastest working route", "Çalışan en hızlı yolu otomatik seç" },
            ["route.auto.hint"] = new[] { "Auto · {0}", "Otomatik · {0}" },
            ["toast.auto.off"] = new[] { "Automatic route turned off", "Otomatik yol seçimi kapatıldı" },
            ["toast.auto.picked"] = new[] { "Using {0}", "{0} kullanılıyor" },
            ["toast.auto.picked.sub"] = new[] { "Picked automatically; the faster routes didn't work here", "Otomatik seçildi; daha hızlı yollar burada çalışmadı" },
            ["toast.reconnected"] = new[] { "Reconnected via {0}", "{0} ile yeniden bağlandı" },
            ["reason.resume"] = new[] { "After sleep", "Uykudan sonra" },
            ["reason.network"] = new[] { "Network changed", "Ağ değişti" },
            ["reason.dropped"] = new[] { "The connection had dropped", "Bağlantı kopmuştu" },
            ["toast.newcircuit"] = new[] { "New Tor exit selected", "Yeni Tor çıkış noktası seçildi" },
            ["toast.newcircuit.sub"] = new[] { "Discord had refused the previous one", "Discord öncekini reddetmişti" },
            ["action.NewCircuit"] = new[] { "New exit", "Yeni çıkış noktası" },
            ["progress.testing"] = new[] { "Checking Discord access", "Discord erişimi kontrol ediliyor" },

            // Route switch
            ["route.label"] = new[] { "Connection route", "Bağlantı yolu" },
            ["route.tor.hint"] = new[] { "Slower, works almost everywhere", "Daha yavaş, hemen her yerde çalışır" },
            ["route.warp.hint"] = new[] { "Fast; some providers block it", "Hızlı; bazı sağlayıcılar engelliyor" },
            ["autoconnect"] = new[] { "Connect when the app opens", "Uygulama açılınca bağlan" },

            // Discord client
            ["client.label"] = new[] { "Discord installation", "Discord kurulumu" },
            ["client.none"] = new[] { "No Discord installation found", "Discord kurulumu bulunamadı" },
            ["client.change"] = new[] { "Change", "Değiştir" },
            ["client.browse"] = new[] { "Choose Discord folder…", "Discord klasörünü seç…" },
            ["client.browse.desc"] = new[] { "Select the Discord folder (it contains Update.exe or app-* folders)", "Discord klasörünü seçin (içinde Update.exe veya app-* klasörleri olur)" },
            ["client.browse.invalid"] = new[] { "That folder doesn't look like a Discord installation", "Bu klasör bir Discord kurulumuna benzemiyor" },
            ["client.rescan"] = new[] { "Scan again", "Yeniden tara" },
            ["client.running.proxy"] = new[] { "Running through the proxy", "Proxy üzerinden çalışıyor" },
            ["client.running.direct"] = new[] { "Running without the proxy", "Proxy olmadan çalışıyor" },
            ["client.stopped"] = new[] { "Not running", "Çalışmıyor" },
            ["client.launch"] = new[] { "Launch", "Başlat" },
            ["client.restart"] = new[] { "Restart", "Yeniden başlat" },
            ["client.shortcut"] = new[] { "Use proxy in shortcut", "Kısayolda proxy kullan" },

            // Bottom bar
            ["btn.test"] = new[] { "Test", "Test" },
            ["btn.logs"] = new[] { "Logs", "Loglar" },
            ["btn.settings"] = new[] { "Settings", "Ayarlar" },

            // Toasts
            ["toast.connected"] = new[] { "Connected via {0}", "{0} ile bağlandı" },
            ["toast.disconnected"] = new[] { "Disconnected", "Bağlantı kesildi" },
            ["toast.mode"] = new[] { "Switched to {0}", "{0} moduna geçildi" },
            ["toast.mode.sub"] = new[] { "Discord reconnects in a few seconds", "Discord birkaç saniye içinde yeniden bağlanır" },
            ["toast.fallback"] = new[] { "WARP didn't work here, using Tor", "WARP burada çalışmadı, Tor kullanılıyor" },
            ["toast.test.ok"] = new[] { "Everything's reachable", "Her şeye ulaşılabiliyor" },
            ["toast.test.fail"] = new[] { "The test found a problem", "Test bir sorun buldu" },
            ["test.API"] = new[] { "API", "API" },
            ["test.CDN"] = new[] { "images", "görseller" },
            ["test.Media"] = new[] { "media", "medya" },
            ["test.Updater"] = new[] { "updater", "güncelleyici" },
            ["toast.test.detail"] = new[] { "API {0} · images {1} · media {2}", "API {0} · görseller {1} · medya {2}" },
            ["toast.discord.started"] = new[] { "Discord launched", "Discord başlatıldı" },
            ["toast.discord.restarted"] = new[] { "Discord restarted through the proxy", "Discord proxy üzerinden yeniden başlatıldı" },
            ["toast.discord.sub"] = new[] { "It may take a few seconds to appear", "Görünmesi birkaç saniye sürebilir" },
            ["toast.settings"] = new[] { "Settings applied, reconnecting", "Ayarlar uygulandı, yeniden bağlanılıyor" },
            ["toast.copied"] = new[] { "Logs copied", "Loglar kopyalandı" },
            ["toast.tray"] = new[] { "Still running in the tray", "Tepside çalışmaya devam ediyor" },
            ["toast.notconnected"] = new[] { "Connect first", "Önce bağlanın" },
            ["toast.leftovers"] = new[] { "Closed {0} leftover process(es)", "Kalan {0} süreç kapatıldı" },

            // Updates
            ["toast.update.app"] = new[] { "Version {0} is available", "{0} sürümü yayınlandı" },
            ["toast.update.tor"] = new[] { "A Tor update is available", "Tor güncellemesi var" },
            ["toast.update.sub"] = new[] { "Settings → Updates", "Ayarlar → Güncellemeler" },
            ["toast.dll.updated"] = new[] { "Proxy module updated to {0}", "Proxy modülü {0} sürümüne güncellendi" },
            ["settings.updates"] = new[] { "Updates", "Güncellemeler" },
            ["settings.tor.version"] = new[] { "Tor {0}", "Tor {0}" },
            ["settings.tor.unknown"] = new[] { "Tor (version unknown)", "Tor (sürüm bilinmiyor)" },
            ["settings.tor.check"] = new[] { "Update Tor", "Tor'u güncelle" },
            ["settings.app.version"] = new[] { "Discord Proxifier {0}", "Discord Proxifier {0}" },
            ["settings.app.check"] = new[] { "Check for updates", "Güncellemeleri denetle" },
            ["update.checking"] = new[] { "Checking…", "Denetleniyor…" },
            ["update.latest"] = new[] { "Up to date", "Güncel" },
            ["update.installing"] = new[] { "Installing {0}…", "{0} kuruluyor…" },
            ["update.done"] = new[] { "Updated to {0}", "{0} sürümüne güncellendi" },
            ["update.failed"] = new[] { "Update failed, see Logs", "Güncelleme başarısız, loglara bakın" },
            ["update.offline"] = new[] { "Couldn't reach the update server", "Güncelleme sunucusuna ulaşılamadı" },
            ["update.notconfigured"] = new[] { "No update source configured", "Güncelleme kaynağı ayarlanmamış" },
            ["update.available"] = new[] { "Version {0} available, download page opened", "{0} sürümü var, indirme sayfası açıldı" },

            // Self-update
            ["UPDATE.t"] = new[] { "Version {0} is ready", "{0} sürümü hazır" },
            ["UPDATE.d"] = new[] { "Installs in a few seconds. The proxy restarts briefly; Discord stays open and reconnects by itself.", "Birkaç saniyede kurulur. Proxy kısa süre yeniden başlar; Discord açık kalır ve kendiliğinden yeniden bağlanır." },
            ["action.InstallUpdate"] = new[] { "Install now", "Şimdi kur" },
            ["action.ReleaseNotes"] = new[] { "What's new", "Yenilikler" },
            ["action.Repair"] = new[] { "Repair", "Onar" },
            ["toast.updating"] = new[] { "Downloading the update", "Güncelleme indiriliyor" },
            ["toast.updated"] = new[] { "Updated to {0}", "{0} sürümüne güncellendi" },
            ["toast.updated.sub"] = new[] { "Your settings were kept", "Ayarlarınız korundu" },
            ["update.install.failed"] = new[] { "Couldn't install the update", "Güncelleme kurulamadı" },
            ["update.install.failed.sub"] = new[] { "The download page is open instead", "Bunun yerine indirme sayfası açıldı" },
            ["update.confirm"] = new[] { "Install version {0} now?\n\nThe app restarts; Discord stays open and reconnects.", "{0} sürümü şimdi kurulsun mu?\n\nUygulama yeniden başlar; Discord açık kalır ve yeniden bağlanır." },

            // Troubleshooting
            ["settings.trouble"] = new[] { "Troubleshooting", "Sorun giderme" },
            ["settings.trouble.hint"] = new[] { "Repair checks files, Discord and the connection. The report is saved to your desktop; send it when asking for help.", "Onar; dosyaları, Discord'u ve bağlantıyı kontrol eder. Rapor masaüstüne kaydedilir; yardım isterken onu gönderin." },
            ["settings.repair"] = new[] { "Repair", "Onar" },
            ["settings.report"] = new[] { "Create report", "Rapor oluştur" },
            ["report.building"] = new[] { "Collecting information…", "Bilgiler toplanıyor…" },
            ["report.saved"] = new[] { "Report saved to the desktop", "Rapor masaüstüne kaydedildi" },
            ["repair.title"] = new[] { "Repair", "Onar" },
            ["repair.running"] = new[] { "Checking…", "Kontrol ediliyor…" },
            ["repair.files"] = new[] { "Program files", "Program dosyaları" },
            ["repair.files.ok"] = new[] { "All files match the published versions", "Tüm dosyalar yayınlanan sürümlerle aynı" },
            ["repair.files.fixed"] = new[] { "Restored {0} file(s)", "{0} dosya geri yüklendi" },
            ["repair.files.failed"] = new[] { "Couldn't restore: {0}", "Geri yüklenemedi: {0}" },
            ["repair.files.unverified"] = new[] { "Present, but the update server wasn't reachable to verify them", "Mevcut, ama doğrulamak için güncelleme sunucusuna ulaşılamadı" },
            ["repair.files.missing"] = new[] { "Missing and couldn't be downloaded: {0}", "Eksik ve indirilemedi: {0}" },
            ["repair.leftovers"] = new[] { "Leftover processes", "Kalan süreçler" },
            ["repair.leftovers.none"] = new[] { "None found", "Bulunmadı" },
            ["repair.leftovers.closed"] = new[] { "Closed {0}", "{0} tanesi kapatıldı" },
            ["repair.discord"] = new[] { "Discord and the proxy module", "Discord ve proxy modülü" },
            ["repair.discord.ok"] = new[] { "{0}: proxy module in place", "{0}: proxy modülü yerinde" },
            ["repair.discord.restarted"] = new[] { "{0}: proxy module updated, Discord will restart", "{0}: proxy modülü güncellendi, Discord yeniden başlatılacak" },
            ["repair.discord.failed"] = new[] { "{0}: couldn't install the proxy module", "{0}: proxy modülü kurulamadı" },
            ["repair.conn"] = new[] { "Connection", "Bağlantı" },
            ["repair.conn.ok"] = new[] { "Discord answers via {0}", "Discord {0} üzerinden yanıt veriyor" },
            ["repair.conn.failed"] = new[] { "Couldn't connect; see the card in the main window", "Bağlanılamadı; ana penceredeki karta bakın" },
            ["repair.summary.ok"] = new[] { "Everything looks good", "Her şey yolunda görünüyor" },
            ["repair.summary.issues"] = new[] { "Some problems remain; create a report and send it", "Bazı sorunlar sürüyor; bir rapor oluşturup gönderin" },
            ["btn.close"] = new[] { "Close", "Kapat" },

            // Tray
            ["tray.launch"] = new[] { "Launch Discord", "Discord'u başlat" },
            ["tray.show"] = new[] { "Show", "Göster" },
            ["tray.exit"] = new[] { "Exit", "Çıkış" },
            ["tray.switch"] = new[] { "Switch to {0}", "{0} moduna geç" },

            // Dialogs
            ["warp.consent.title"] = new[] { "Cloudflare WARP", "Cloudflare WARP" },
            ["warp.consent.body"] = new[]
            {
                "WARP mode registers a free Cloudflare WARP device (via wgcf).\n\nThis requires accepting Cloudflare's terms:\nhttps://www.cloudflare.com/application/terms/\n\nDo you accept the terms and want to register?",
                "WARP modu ücretsiz bir Cloudflare WARP cihazı kaydeder (wgcf ile).\n\nBunun için Cloudflare'in koşullarını kabul etmeniz gerekir:\nhttps://www.cloudflare.com/application/terms/\n\nKoşulları kabul edip kayıt olmak istiyor musunuz?"
            },
            ["warp.consent.declined"] = new[] { "WARP needs Cloudflare's terms to be accepted", "WARP için Cloudflare koşullarının kabulü gerekiyor" },
            ["leftovers.body"] = new[]
            {
                "These processes from another DiscordProxifier copy are still running and block the proxy ports:\n\n{0}\n\nClose them now?",
                "Başka bir DiscordProxifier kopyasından kalan bu süreçler hâlâ çalışıyor ve proxy portlarını tutuyor:\n\n{0}\n\nŞimdi kapatılsın mı?"
            },
            ["instance.body"] = new[] { "Discord Proxifier is already running. Close the other one?", "Discord Proxifier zaten çalışıyor. Diğeri kapatılsın mı?" },

            // Settings
            ["settings.title"] = new[] { "Settings", "Ayarlar" },
            ["settings.autosave"] = new[] { "Changes are saved automatically", "Değişiklikler otomatik kaydedilir" },
            ["settings.general"] = new[] { "General", "Genel" },
            ["settings.language"] = new[] { "Language", "Dil" },
            ["settings.startup"] = new[] { "Start with Windows", "Windows ile başlat" },
            ["settings.launchafter"] = new[] { "Launch Discord after connecting", "Bağlandıktan sonra Discord'u başlat" },
            ["settings.shortcuts"] = new[] { "Keep proxified shortcuts up to date", "Proxy'li kısayolları güncel tut" },
            ["settings.warp"] = new[] { "WARP", "WARP" },
            ["settings.fallback"] = new[] { "Use Tor if WARP doesn't work", "WARP çalışmazsa Tor'u kullan" },
            ["settings.endpoint"] = new[] { "Custom endpoint", "Özel endpoint" },
            ["settings.endpoint.hint"] = new[] { "Optional, like 162.159.192.1:2408", "İsteğe bağlı, örneğin 162.159.192.1:2408" },
            ["settings.tor"] = new[] { "Tor", "Tor" },
            ["settings.bridges"] = new[] { "Bridges", "Köprüler (bridge)" },
            ["settings.bridges.hint"] = new[] { "One per line, from bridges.torproject.org", "Her satıra bir tane, bridges.torproject.org'dan" },
            ["settings.advanced"] = new[] { "Advanced", "Gelişmiş" },
            ["settings.port.http"] = new[] { "Discord port (HTTP)", "Discord portu (HTTP)" },
            ["settings.port.socks"] = new[] { "Backend port (SOCKS)", "Arka uç portu (SOCKS)" },
            ["settings.port.invalid"] = new[] { "Use a number from 1 to 65535", "1 ile 65535 arasında bir sayı girin" },
            ["settings.port.same"] = new[] { "The two ports must be different", "İki port farklı olmalı" },
            ["settings.endpoint.invalid"] = new[] { "Use host:port", "host:port biçiminde yazın" },
            ["settings.folder"] = new[] { "Open app folder", "Uygulama klasörünü aç" },
            ["settings.shortcut"] = new[] { "Create desktop shortcut", "Masaüstü kısayolu oluştur" },
            ["settings.shortcut.done"] = new[] { "Shortcut created", "Kısayol oluşturuldu" },

            // Logs
            ["logs.title"] = new[] { "Logs", "Loglar" },
            ["logs.copy"] = new[] { "Copy all", "Tümünü kopyala" },
            ["logs.copied"] = new[] { "Copied", "Kopyalandı" },

            // Error card actions
            ["action.Retry"] = new[] { "Try again", "Tekrar dene" },
            ["action.SwitchMode"] = new[] { "Use {0}", "{0} kullan" },
            ["action.OpenSettings"] = new[] { "Settings", "Ayarlar" },
            ["action.AddBridges"] = new[] { "Add bridges", "Köprü ekle" },
            ["action.ChangeEndpoint"] = new[] { "Change endpoint", "Endpoint değiştir" },
            ["action.KillLeftovers"] = new[] { "Close leftovers", "Kalanları kapat" },
            ["action.OpenLogs"] = new[] { "Logs", "Loglar" },
            ["action.OpenFolder"] = new[] { "Open folder", "Klasörü aç" },
            ["action.BrowseDiscord"] = new[] { "Choose folder", "Klasör seç" },
            ["action.RescanDiscord"] = new[] { "Scan again", "Yeniden tara" },
            ["action.RetryDll"] = new[] { "Try again", "Tekrar dene" },

            // Error catalog: title / description
            ["DP-101.t"] = new[] { "Port in use", "Port kullanımda" },
            ["DP-101.d"] = new[] { "Port {0} is used by another program. Usually it's a leftover from an older DiscordProxifier copy.", "{0} numaralı portu başka bir program kullanıyor. Genellikle eski bir DiscordProxifier kopyasından kalan bir süreçtir." },
            ["DP-102.t"] = new[] { "Local proxy didn't start", "Yerel proxy başlamadı" },
            ["DP-102.d"] = new[] { "The app couldn't open its local proxy on port {0}. Another program may be using it; change the port in Settings → Advanced.", "Uygulama yerel proxy'sini {0} portunda açamadı. Portu başka bir program kullanıyor olabilir; Ayarlar → Gelişmiş'ten portu değiştirin." },
            ["DP-103.t"] = new[] { "A required file is missing", "Gerekli bir dosya eksik" },
            ["DP-103.d"] = new[] { "{0} wasn't found. Copy the Data folder next to the app again.", "{0} bulunamadı. Data klasörünü uygulamanın yanına yeniden kopyalayın." },
            ["DP-201.t"] = new[] { "Couldn't reach the Tor network", "Tor ağına ulaşılamadı" },
            ["DP-201.d"] = new[] { "Tor didn't finish connecting in time. This network may block Tor; adding bridges usually fixes it.", "Tor bağlantıyı zamanında tamamlayamadı. Bu ağ Tor'u engelliyor olabilir; köprü (bridge) eklemek genellikle çözer." },
            ["DP-202.t"] = new[] { "Tor stopped unexpectedly", "Tor beklenmedik şekilde durdu" },
            ["DP-202.d"] = new[] { "The Tor process exited while connecting. The logs show the reason.", "Tor süreci bağlanırken kapandı. Sebebi loglarda görünür." },
            ["DP-301.t"] = new[] { "WARP registration failed", "WARP kaydı yapılamadı" },
            ["DP-301.d"] = new[] { "Cloudflare's registration service couldn't be reached. It may be blocked on this network.", "Cloudflare'in kayıt servisine ulaşılamadı. Bu ağda engelli olabilir." },
            ["DP-302.t"] = new[] { "WARP can't reach Discord", "WARP Discord'a ulaşamıyor" },
            ["DP-302.d"] = new[] { "The WARP tunnel works but traffic doesn't get through. A brand-new WARP account can take a few minutes to activate; try again shortly.", "WARP tüneli kuruldu ama trafik geçmiyor. Yeni açılan WARP hesaplarının etkinleşmesi birkaç dakika sürebilir; biraz sonra tekrar deneyin." },
            ["DP-303.t"] = new[] { "WARP is blocked on this network", "WARP bu ağda engelli" },
            ["DP-303.d"] = new[] { "Cloudflare's WARP server never answered. Your provider probably blocks it; another endpoint may work.", "Cloudflare'in WARP sunucusu hiç yanıt vermedi. Sağlayıcınız büyük ihtimalle engelliyor; başka bir endpoint işe yarayabilir." },
            ["DP-401.t"] = new[] { "Discord isn't reachable", "Discord'a ulaşılamıyor" },
            ["DP-401.d"] = new[] { "The proxy is running but Discord didn't answer ({0}).", "Proxy çalışıyor ama Discord yanıt vermedi ({0})." },
            ["DP-402.t"] = new[] { "Discord refused this route", "Discord bu çıkışı reddetti" },
            ["DP-402.d"] = new[] { "Discord answered {0}: the current exit is blocked or rate-limited. Reconnecting picks a new one.", "Discord {0} yanıtı verdi: mevcut çıkış noktası engellenmiş ya da sınırlanmış. Yeniden bağlanmak yeni bir çıkış seçer." },
            ["DP-601.t"] = new[] { "Direct route is blocked", "Doğrudan yol engelli" },
            ["DP-601.d"] = new[] { "Your provider stopped the connection even with the split trick. It may block by address or detect the trick; WARP or Tor still work.", "Sağlayıcınız bölme hilesine rağmen bağlantıyı kesti. Adres bazlı engelliyor ya da hileyi tanıyor olabilir; WARP veya Tor yine çalışır." },
            ["DP-602.t"] = new[] { "Secure DNS isn't reachable", "Şifreli DNS'e ulaşılamıyor" },
            ["DP-602.d"] = new[] { "The direct route needs DNS over HTTPS (Cloudflare or Google) because the provider's DNS gives fake answers for Discord. None of them answered.", "Doğrudan yol, sağlayıcının DNS'i Discord için sahte cevap verdiğinden şifreli DNS (Cloudflare veya Google) kullanır. Hiçbiri yanıt vermedi." },
            ["DP-700.t"] = new[] { "No route works", "Hiçbir yol çalışmadı" },
            ["DP-700.d"] = new[] { "Direct, WARP and Tor all failed on this network ({0}). Tor bridges usually help; the logs show each attempt.", "Bu ağda Doğrudan, WARP ve Tor'un hepsi başarısız oldu ({0}). Tor köprüleri genellikle işe yarar; her deneme loglarda görünür." },
            ["DP-501.t"] = new[] { "Discord wasn't found", "Discord bulunamadı" },
            ["DP-501.d"] = new[] { "No Discord installation was found. Install Discord, or choose its folder if it's in a custom location.", "Discord kurulumu bulunamadı. Discord'u kurun ya da farklı bir yerdeyse klasörünü seçin." },
            ["DP-502.t"] = new[] { "Proxy module unavailable", "Proxy modülü alınamadı" },
            ["DP-502.d"] = new[] { "version.dll is missing and couldn't be downloaded from the update server, so Discord's updater can't use the proxy.", "version.dll eksik ve güncelleme sunucusundan indirilemedi; bu yüzden Discord güncelleyicisi proxy'yi kullanamıyor." },
            ["DP-503.t"] = new[] { "Couldn't launch Discord", "Discord başlatılamadı" },
            ["DP-503.d"] = new[] { "{0}", "{0}" },
        };
    }
}
