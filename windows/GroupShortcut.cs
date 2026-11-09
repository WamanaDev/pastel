using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pastel
{
    /// <summary>
    /// Cada grupo é um arquivo .wfgroup na área de trabalho. O próprio arquivo é um ícone (formato
    /// .ico + o id do grupo no final), e o tipo de arquivo usa "%1" como ícone. Assim ele se
    /// comporta como um atalho (posição, grade, renomear e excluir pelo Explorer), mas sem a seta.
    /// </summary>
    internal static class GroupShortcut
    {
        public const string Ext = ".wfgroup";
        public const string GroupFileArg = "--group-file";

        private const string Marker = "\nWFOLDER-GROUP:";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);
        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
        private static extern void SHChangeNotifyIndex(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        // ---------- nomes ----------

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return name.Length == 0 ? "Pasta" : name;
        }

        private static string NewPath(string name) =>
            DesktopFiles.UniquePath(DesktopFiles.UserDesktop, Sanitize(name) + Ext);

        public static bool IsLegacyLnk(string? path) =>
            path != null && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

        // ---------- identificar o grupo pelo arquivo ----------

        /// <summary>Lê o id do grupo gravado no final do arquivo (sobrevive a renomear/mover).</summary>
        public static string? ReadId(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int n = (int)Math.Min(fs.Length, 96);
                fs.Seek(-n, SeekOrigin.End);
                var buf = new byte[n];
                int read = fs.Read(buf, 0, n);
                string tail = Encoding.UTF8.GetString(buf, 0, read);
                int i = tail.LastIndexOf(Marker, StringComparison.Ordinal);
                return i < 0 ? null : tail.Substring(i + Marker.Length).Trim();
            }
            catch { return null; }
        }

        /// <summary>Acha o arquivo do grupo na área de trabalho, mesmo que tenha sido renomeado.</summary>
        public static string? FindById(string id)
        {
            try
            {
                foreach (var p in Directory.EnumerateFiles(DesktopFiles.UserDesktop, "*" + Ext))
                    if (string.Equals(ReadId(p), id, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch { }
            return null;
        }

        // ---------- criar / atualizar ----------

        /// <summary>
        /// Garante o arquivo do grupo. Sem force, só cria se faltar (e migra o .lnk da versão
        /// anterior); com force, também regenera o ícone (depois de mudar os apps).
        /// </summary>
        /// <returns>false se não deu para gravar o arquivo (ex.: Explorer segurando); tenta de novo depois.</returns>
        public static bool Ensure(FolderModel f, bool force = false)
        {
            if (IsLegacyLnk(f.LnkPath))
            {
                // Versão anterior usava .lnk (com seta): troca pelo .wfgroup, mesmo nome.
                string old = f.LnkPath!;
                string baseName = Path.GetFileNameWithoutExtension(old);
                f.LnkPath = null;
                f.Name = baseName;
                bool ok = Ensure(f, force: true);
                try { File.Delete(old); } catch { }
                Notify(0x00000004 /*DELETE*/, old);
                return ok;
            }

            if (f.LnkPath != null && !File.Exists(f.LnkPath) && IsStashed(f))
            {
                SyncHidden(f); // pasta vazia guardada fora da área de trabalho: volta só se já não deve ficar oculta
                if (IsStashed(f))
                {
                    if (force) WriteReplacing(StashPath(f), BuildIcon(f)); // mantém o ícone (tema) em dia
                    return true;
                }
            }

            bool exists = f.LnkPath != null && File.Exists(f.LnkPath);
            if (exists && !force && string.Equals(ReadId(f.LnkPath!), f.Id, StringComparison.OrdinalIgnoreCase)) { SyncHidden(f); return true; }
            if (!exists) f.LnkPath = NewPath(f.Name);

            if (!WriteReplacing(f.LnkPath!, BuildIcon(f))) return false;
            RefreshShell(f.LnkPath!, created: !exists);
            SyncHidden(f); // o arquivo novo nasce sem o atributo Oculto
            return true;
        }

        // ---------- ocultar pastas vazias ----------

        /// <summary>Id da pasta com o balão aberto (sendo editada): nunca é ocultada enquanto isso.</summary>
        public static string? EditingId;

        /// <summary>Onde o arquivo da pasta fica guardado enquanto ela está oculta (fora da área de trabalho).</summary>
        public static string StashPath(FolderModel f) => Path.Combine(ConfigStore.Dir, "Ocultas", f.Id + Ext);

        public static bool IsStashed(FolderModel f) => File.Exists(StashPath(f));

        /// <summary>
        /// Aplica a opção "Ocultar pastas vazias" a uma pasta: quando está vazia (e não é recém-criada), o arquivo sai da
        /// área de trabalho e fica guardado em Ocultas\ dentro dos dados do Pastel; ao receber itens (ou desligar a opção)
        /// volta ao mesmo lugar. Não depende do "mostrar arquivos ocultos" do Explorer. f.LnkPath continua sendo o lugar
        /// de origem na área de trabalho.
        /// </summary>
        public static void SyncHidden(FolderModel f)
        {
            if (f.Items.Count > 0) f.Fresh = false;
            if (f.LnkPath == null || IsLegacyLnk(f.LnkPath)) return;
            try
            {
                bool want = ConfigStore.Current.HideEmptyFolders && !f.Fresh && f.Items.Count == 0
                            && !f.IsMirror && !string.Equals(EditingId, f.Id, StringComparison.OrdinalIgnoreCase);
                string stash = StashPath(f);
                bool stashed = File.Exists(stash), onDesk = File.Exists(f.LnkPath);

                if (onDesk)
                {
                    // Versão anterior só marcava o arquivo como Oculto (aparecia com "mostrar arquivos ocultos"): desfaz.
                    var attrs = File.GetAttributes(f.LnkPath);
                    if (attrs.HasFlag(FileAttributes.Hidden)) File.SetAttributes(f.LnkPath, attrs & ~FileAttributes.Hidden);
                }

                if (want && onDesk && !stashed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stash)!);
                    File.Move(f.LnkPath, stash, overwrite: true);
                    Notify(0x00000004 /*DELETE*/, f.LnkPath);
                    Notify(0x00001000 /*UPDATEDIR*/, DesktopFiles.UserDesktop);
                }
                else if (!want && stashed && !onDesk)
                {
                    File.Move(stash, f.LnkPath);
                    Notify(0x00000002 /*CREATE*/, f.LnkPath);
                    Notify(0x00001000 /*UPDATEDIR*/, DesktopFiles.UserDesktop);
                }
            }
            catch { }
        }

        /// <summary>
        /// Grava o arquivo novo ao lado e troca pelo antigo. O Explorer só descarta o ícone em cache
        /// quando o arquivo é substituído E avisado (ver RefreshShell); regravar por cima não basta.
        /// </summary>
        private static bool WriteReplacing(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Move(tmp, path, overwrite: true); return true; }
                    catch (IOException) when (attempt < 4) { System.Threading.Thread.Sleep(120); } // Explorer lendo o ícone
                }
            }
            catch { return false; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        /// <summary>
        /// Faz o Explorer trocar o ícone que ele guardou em cache. Testado: o aviso comum de "item
        /// atualizado" sozinho NÃO basta; junto com o aviso de "imagem do ícone atualizada" (índice do
        /// ícone na lista de imagens do sistema) a troca é imediata.
        /// </summary>
        private static void RefreshShell(string path, bool created)
        {
            try
            {
                // Ao atualizar, o Explorer às vezes mantém o ícone antigo (testado na versão autônoma: pasta nova ficava
                // com o quadrado vazio). Avisar que o item sumiu e voltou o faz reler o ícone.
                if (!created) Notify(0x00000004 /*DELETE*/, path);
                Notify(0x00000002 /*CREATE*/, path);
                Notify(0x00002000 /*UPDATEITEM*/, path);
                var fi = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x4000 /*SYSICONINDEX*/);
                SHChangeNotifyIndex(0x00008000 /*UPDATEIMAGE*/, 0x0003 /*DWORD*/, (IntPtr)fi.iIcon, IntPtr.Zero);
            }
            catch { }
        }

        public static void Rename(FolderModel f, string newName)
        {
            if (f.LnkPath != null && IsStashed(f) && !File.Exists(f.LnkPath))
            {
                // Guardada (oculta): só o nome de origem muda; o arquivo continua em Ocultas\.
                f.LnkPath = NewPath(newName);
                f.Name = Path.GetFileNameWithoutExtension(f.LnkPath);
                return;
            }
            if (f.LnkPath == null || !File.Exists(f.LnkPath)) { f.Name = newName; return; }
            string target = NewPath(newName);
            File.Move(f.LnkPath, target);
            f.LnkPath = target;
            f.Name = Path.GetFileNameWithoutExtension(target);
        }

        public static void Delete(FolderModel f)
        {
            try { File.Delete(StashPath(f)); } catch { }
            try { if (f.LnkPath != null) File.Delete(f.LnkPath); } catch { }
            if (f.LnkPath != null) Notify(0x00000004 /*DELETE*/, f.LnkPath);
        }

        private static void Notify(int eventId, string path)
        {
            try { SHChangeNotify(eventId, 0x0005 /*PATHW*/, path, IntPtr.Zero); } catch { }
        }

        // ---------- ícone ----------

        private static byte[] RenderPng(FolderModel f, int size)
        {
            const double S = 256;
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / S, size / S));
                // Vidro translúcido no tema do Windows: base clara ou escura, com um toque da cor de destaque.
                bool light = WindowsTheme.IsLight;
                var accent = WindowsTheme.Visible(WindowsTheme.Accent, light);
                var baseColor = light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1E, 0x22, 0x2C);
                var glass = WindowsTheme.WithAlpha(WindowsTheme.Mix(baseColor, accent, 0.18), light ? (byte)0xC4 : (byte)0xB4);
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(glass),
                    new Pen(new SolidColorBrush(WindowsTheme.WithAlpha(accent, 0xA0)), 5),
                    new Rect(12, 12, S - 24, S - 24), 36, 36);

                int i = 0;
                foreach (var item in f.Items.Take(4))
                {
                    var src = IconService.GetStyled(item.Path, 192); // já arredondado e padronizado
                    if (src != null)
                        dc.DrawImage(src, new Rect(29 + (i % 2) * 104, 29 + (i / 2) * 104, 94, 94));
                    i++;
                }
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            png.Save(ms);
            return ms.ToArray();
        }

        /// <summary>.ico (PNG em vários tamanhos, para ficar nítido em qualquer escala) seguido do id da pasta.</summary>
        private static byte[] BuildIcon(FolderModel f)
        {
            int[] sizes = { 256, 128, 96, 72, 64, 48, 32 };
            var images = sizes.Select(s => RenderPng(f, s)).ToArray();

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length); // ICONDIR
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] % 256); // 256 => 0
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
            w.Write(Encoding.UTF8.GetBytes(Marker + f.Id)); // ignorado pelo leitor de ícones
            w.Flush();
            return ms.ToArray();
        }
    }
}
