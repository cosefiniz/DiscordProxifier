using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DiscordUnblocker
{
    // Where updates come from: our own GitHub repo. dist/manifest.json lists every
    // downloadable file with its version and SHA256; nothing is installed unless the hash matches.
    public static class UpdateSource
    {
        public const string Owner = "cosefiniz"; // GitHub user that hosts the repo
        public const string Repo = "DiscordProxifier";
        public const string Branch = "main";

        public static bool Configured => Owner != "OWNER";
        public static string Raw(string path) => $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/{path}";
        public static string ManifestUrl => Raw("dist/manifest.json");
        public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases/latest";
    }

    public class ManifestFile
    {
        [JsonProperty("version")] public string Version;
        [JsonProperty("sha256")] public string Sha256;
        [JsonProperty("path")] public string Path;
        [JsonProperty("size")] public long Size;
    }

    public class Manifest
    {
        [JsonProperty("appVersion")] public string AppVersion;
        [JsonProperty("appDownload")] public string AppDownload;
        [JsonProperty("files")] public Dictionary<string, ManifestFile> Files = new Dictionary<string, ManifestFile>();

        public ManifestFile Get(string key)
        {
            ManifestFile f;
            return Files != null && Files.TryGetValue(key, out f) ? f : null;
        }
    }

    public class Updater
    {
        readonly Logger logger;
        readonly string dataDir;

        public Updater(Logger logger, string dataDir)
        {
            this.logger = logger;
            this.dataDir = dataDir;
        }

        // Keys in manifest.files, relative to Data\.
        public static readonly string[] DataFiles = { "Tor.zip", "gost.exe", "wgcf.exe", "wireproxy.exe", "x64/version.dll", "x86/version.dll" };

        static HttpClient Client(TimeSpan timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = timeout };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordProxifier/" + AppSetup.AppVersion);
            return c;
        }

        public async Task<Manifest> FetchManifestAsync()
        {
            if (!UpdateSource.Configured)
                return null;
            try
            {
                using (HttpClient c = Client(TimeSpan.FromSeconds(15)))
                {
                    string json = await c.GetStringAsync(UpdateSource.ManifestUrl + "?t=" + DateTime.UtcNow.Ticks);
                    return JsonConvert.DeserializeObject<Manifest>(json);
                }
            }
            catch (Exception ex)
            {
                logger.Log("Could not read the update manifest: " + ex.Message, LogType.WARNING);
                return null;
            }
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (FileStream s = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // Downloads a manifest entry to 'target' and keeps it only if the SHA256 matches.
        public async Task<bool> DownloadVerifiedAsync(ManifestFile f, string target)
        {
            if (f == null || string.IsNullOrEmpty(f.Path) || string.IsNullOrEmpty(f.Sha256))
                return false;
            string part = target + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                logger.Log($"Downloading {f.Path} ({f.Version})");
                using (HttpClient c = Client(TimeSpan.FromMinutes(10)))
                using (Stream src = await c.GetStreamAsync(UpdateSource.Raw(f.Path)))
                using (FileStream dst = File.Create(part))
                    await src.CopyToAsync(dst);
                string actual = Sha256(part);
                if (!actual.Equals(f.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    logger.Log($"{f.Path}: SHA256 mismatch (expected {f.Sha256}, got {actual}); discarded", LogType.ERROR);
                    File.Delete(part);
                    return false;
                }
                if (File.Exists(target))
                    File.Delete(target);
                File.Move(part, target);
                return true;
            }
            catch (Exception ex)
            {
                logger.Log($"Download of {f.Path} failed: {ex.Message}", LogType.ERROR);
                try
                {
                    File.Delete(part);
                }
                catch { }
                return false;
            }
        }

        // Fills in anything missing from Data\ (fresh install with a partial folder).
        public async Task EnsureFilesAsync(Manifest m)
        {
            foreach (string key in DataFiles)
            {
                string target = Path.Combine(dataDir, key.Replace('/', '\\'));
                if (key == "Tor.zip" && File.Exists(Path.Combine(dataDir, "Tor", "tor.exe")))
                    continue;
                if (File.Exists(target))
                    continue;
                if (await DownloadVerifiedAsync(m.Get(key), target) && key == "Tor.zip")
                    InstallTorZip(target, m.Get(key).Version);
            }
        }

        // ---------- version.dll (updated automatically) ----------

        string DllVerFile => Path.Combine(dataDir, "DllVer");

        public string InstalledDllVersion => File.Exists(DllVerFile) ? File.ReadAllText(DllVerFile).Trim() : "";

        public async Task<bool> UpdateDllAsync(Manifest m)
        {
            ManifestFile x64 = m.Get("x64/version.dll"), x86 = m.Get("x86/version.dll");
            if (x64 == null || x86 == null || x64.Version == InstalledDllVersion)
                return false;
            string tmp = Path.Combine(dataDir, "dll-update");
            bool ok = await DownloadVerifiedAsync(x64, Path.Combine(tmp, "x64", "version.dll"))
                && await DownloadVerifiedAsync(x86, Path.Combine(tmp, "x86", "version.dll"));
            if (ok)
            {
                foreach (string arch in new[] { "x64", "x86" })
                {
                    Directory.CreateDirectory(Path.Combine(dataDir, arch));
                    File.Copy(Path.Combine(tmp, arch, "version.dll"), Path.Combine(dataDir, arch, "version.dll"), true);
                }
                File.WriteAllText(DllVerFile, x64.Version);
                logger.Log($"version.dll updated to {x64.Version}");
            }
            try
            {
                Directory.Delete(tmp, true);
            }
            catch { }
            return ok;
        }

        // ---------- Tor (updated from Settings) ----------

        string TorDir => Path.Combine(dataDir, "Tor");
        string TorVerFile => Path.Combine(TorDir, "TorVer");

        public string InstalledTorVersion()
        {
            try
            {
                if (File.Exists(TorVerFile))
                    return File.ReadAllText(TorVerFile).Trim();
                string exe = Path.Combine(TorDir, "tor.exe");
                if (!File.Exists(exe))
                    return null;
                var p = Process.Start(new ProcessStartInfo(exe, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                Match mt = Regex.Match(output, @"Tor version (\d+(\.\d+)+)");
                if (!mt.Success)
                    return null;
                File.WriteAllText(TorVerFile, mt.Groups[1].Value);
                return mt.Groups[1].Value;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsNewer(string candidate, string installed)
        {
            Version a, b;
            if (!Version.TryParse(candidate ?? "", out a))
                return false;
            if (!Version.TryParse(installed ?? "", out b))
                return true;
            return a > b;
        }

        public bool TorUpdateAvailable(Manifest m)
        {
            ManifestFile f = m?.Get("Tor.zip");
            return f != null && IsNewer(f.Version, InstalledTorVersion());
        }

        // Downloads only; call InstallTorZip after tor.exe has been stopped.
        public async Task<string> DownloadTorAsync(Manifest m)
        {
            ManifestFile f = m.Get("Tor.zip");
            string tmp = Path.Combine(dataDir, "Tor.zip.new");
            return await DownloadVerifiedAsync(f, tmp) ? tmp : null;
        }

        // Replaces tor.exe and the pluggable transports; keeps Data\Tor\data (cached consensus).
        public bool InstallTorZip(string zipPath, string version)
        {
            try
            {
                string pt = Path.Combine(TorDir, "PluggableTransports");
                if (Directory.Exists(pt))
                    Directory.Delete(pt, true);
                Directory.CreateDirectory(TorDir);
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string dest = Path.Combine(TorDir, entry.FullName);
                        if (entry.FullName.EndsWith("/"))
                        {
                            Directory.CreateDirectory(dest);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        entry.ExtractToFile(dest, true);
                    }
                }
                string final = Path.Combine(dataDir, "Tor.zip");
                if (!zipPath.Equals(final, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(final))
                        File.Delete(final);
                    File.Move(zipPath, final);
                }
                File.WriteAllText(TorVerFile, version ?? "");
                logger.Log($"Tor {version} installed");
                return true;
            }
            catch (Exception ex)
            {
                logger.Log("Tor install failed: " + ex.Message, LogType.ERROR);
                return false;
            }
        }
    }
}
