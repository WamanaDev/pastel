using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pastel
{
    /// <summary>
    /// Procura uma versão nova na página de versões do GitHub (a mais recente que não é pré-lançamento) e,
    /// se o usuário aceitar, baixa o instalador, confere a impressão digital (SHA-256) e o executa em silêncio.
    /// A única conexão de rede do Pastel; pode ser desligada em Configurações > Geral.
    /// </summary>
    internal static class Updater
    {
        private const string Repo = "WamanaDev/pastel";
        private const string SetupName = "Pastel-Setup.exe";
        private const string SumsName = "SHA256SUMS.txt";

        internal sealed record Release(Version Version, string Tag, string SetupUrl, string SumsUrl);

        private static readonly HttpClient Http = Create();

        private static HttpClient Create()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Pastel-Updater");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version Current
        {
            get
            {
                var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>A versão nova, ou null se já está na mais recente (ou se não deu para checar).</summary>
        public static async Task<(Release? release, bool ok)> CheckAsync()
        {
            try
            {
                using var s = await Http.GetStreamAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = await JsonDocument.ParseAsync(s);
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return (null, false);
                if (v <= Current) return (null, true);

                string? setup = null, sums = null;
                foreach (var a in root.GetProperty("assets").EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    string url = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (name == SetupName) setup = url;
                    else if (name == SumsName) sums = url;
                }
                if (setup == null || sums == null) return (null, false); // sem impressão digital, não oferece
                return (new Release(v, tag, setup, sums), true);
            }
            catch { return (null, false); }
        }

        /// <summary>Baixa, confere e inicia o instalador. false se algo falhou (nada é executado).</summary>
        public static async Task<bool> DownloadAndRunAsync(Release r)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Pastel-Update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, SetupName);

                var bytes = await Http.GetByteArrayAsync(r.SetupUrl);
                string sums = await Http.GetStringAsync(r.SumsUrl);

                string expected = sums.Split('\n')
                    .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(p => p.Length == 2 && p[1].TrimStart('*', ' ') == SetupName)
                    .Select(p => p[0]).FirstOrDefault() ?? "";
                string actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) return false;

                await File.WriteAllBytesAsync(file, bytes);
                Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }
    }
}
