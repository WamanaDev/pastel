using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pastel
{
    /// <summary>
    /// Procura uma versão nova na página de versões do GitHub (a mais recente que não é pré-lançamento) e, se o usuário
    /// aceitar, baixa o pacote, confere o SHA-256 e roda o install.sh dele. É a única conexão de rede do Pastel;
    /// pode ser desligada em Configurações > Geral.
    /// </summary>
    internal static class Updater
    {
        private const string Repo = "WamanaDev/pastel";
        private const string PackageName = "pastel-linux-x64.tar.gz";
        private const string SumsName = "SHA256SUMS.txt";

        internal sealed record Release(Version Version, string Tag, string PackageUrl, string SumsUrl);

        private static readonly HttpClient Http = Create();

        private static HttpClient Create()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

        /// <summary>Lê o JSON de "releases/latest". Separado da rede para dar para testar.</summary>
        internal static Release? Parse(string json, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v) || v <= current) return null;

            string? pkg = null, sums = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                string url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (name == PackageName) pkg = url;
                else if (name == SumsName) sums = url;
            }
            return pkg == null || sums == null ? null : new Release(v, tag, pkg, sums); // sem impressão digital, não oferece
        }

        /// <summary>release = a versão nova (ou null); ok = false se não deu para checar.</summary>
        public static async Task<(Release? release, bool ok)> CheckAsync()
        {
            try
            {
                string json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                return (Parse(json, Current), true);
            }
            catch { return (null, false); }
        }

        internal static string? ExpectedHash(string sums) =>
            sums.Split('\n')
                .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2 && p[1].TrimStart('*', ' ') == PackageName)
                .Select(p => p[0]).FirstOrDefault();

        /// <summary>Baixa, confere e inicia o install.sh do pacote, solto deste processo. false = nada foi instalado.</summary>
        public static async Task<bool> DownloadAndInstallAsync(Release r)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(r.PackageUrl);
                string sums = await Http.GetStringAsync(r.SumsUrl);
                string actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(ExpectedHash(sums), actual, StringComparison.OrdinalIgnoreCase)) return false;

                string dir = Path.Combine(Path.GetTempPath(), "pastel-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                using (var ms = new MemoryStream(bytes))
                using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                    TarFile.ExtractToDirectory(gz, dir, overwriteFiles: true);

                string script = Path.Combine(dir, "pastel-linux-x64", "install.sh");
                if (!File.Exists(script)) return false;

                // setsid: o instalador fecha este processo e abre o novo; ele tem que sobreviver a isso
                Process.Start(new ProcessStartInfo("setsid", $"bash \"{script}\"") { UseShellExecute = false });
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Desinstalação: os atalhos voltam para a área de trabalho e o que o Pastel criou é apagado.</summary>
    internal static class UninstallCleanup
    {
        public static int Run()
        {
            try
            {
                ConfigStore.Load();
                var cfg = ConfigStore.Current;
                foreach (var f in cfg.Folders.ToList()) Library.RemoveGroupData(f); // devolve os atalhos e apaga o lançador
                foreach (var m in cfg.Floats)
                    foreach (var item in m.Items) DesktopFiles.Restore(item);

                // só depois de devolver os atalhos: apaga os dados e a configuração
                try { Directory.Delete(Paths.DataDir, true); } catch { }
                try { Directory.Delete(Paths.ConfigDir, true); } catch { }
                return 0;
            }
            catch { return 1; }
        }
    }
}
