using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pastel
{
    /// <summary>
    /// As regras do Pastel (iguais às da versão do Windows): quais apps estão em cada pasta ou flutuante,
    /// o que acontece com os atalhos da área de trabalho e como a lista acompanha o que o usuário faz por fora.
    /// Sem interface: a interface só escuta os eventos.
    /// </summary>
    internal static class Library
    {
        private static AppConfig Cfg => ConfigStore.Current;

        /// <summary>Os apps de uma pasta ou flutuante mudaram.</summary>
        public static event Action<IItemOwner>? OwnerChanged;
        /// <summary>Lista de pastas/flutuantes (ou algum nome) mudou.</summary>
        public static event Action? ListChanged;

        public static void RaiseListChanged() => ListChanged?.Invoke();

        // ---------- apps dentro de pastas e flutuantes ----------

        /// <summary>Adiciona os caminhos. Atalho que estava na área de trabalho sai de lá (vai para a pasta gerenciada).</summary>
        public static bool AddPaths(IItemOwner owner, IEnumerable<string> paths)
        {
            bool changed = false;
            foreach (var raw in paths)
            {
                string p = raw;
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    try { p = new Uri(p).LocalPath; } catch { continue; }

                var existing = owner.Items.FirstOrDefault(i => string.Equals(i.Path, p, StringComparison.Ordinal) ||
                                                               string.Equals(i.OriginalPath, p, StringComparison.Ordinal) ||
                                                               string.Equals(i.SourcePath, p, StringComparison.Ordinal));
                if (existing != null)
                {
                    if (Adopt(owner, existing)) changed = true; // já estava, mas o atalho ainda está solto na área de trabalho
                    continue;
                }

                string? original = null, source = null;
                string path = p;
                string name;
                if (DesktopFiles.IsShortcutFile(p))
                {
                    name = NameOf(p);
                    if (DesktopFiles.TryTake(p, owner.Id, out var moved)) { original = p; path = moved; }
                }
                else if (File.Exists(p) || Directory.Exists(p))
                {
                    // arquivo, programa ou pasta: fica onde está; o Pastel cria um atalho .desktop na pasta gerenciada
                    string? link = DesktopFiles.CreateLink(p, owner.Id);
                    if (link == null) continue;
                    source = Path.GetFullPath(p);
                    path = link;
                    name = NameOf(link);
                }
                else name = NameOf(p);
                if (string.IsNullOrEmpty(name)) name = p;

                owner.Items.Add(new ShortcutItem { Name = name, Path = path, OriginalPath = original, SourcePath = source });
                changed = true;
            }
            if (changed && owner is FolderModel fo) GroupLauncher.SyncHidden(fo); // recebeu item: deixa de ser nova e volta à área de trabalho
            if (changed) { ConfigStore.Save(); OwnerChanged?.Invoke(owner); }
            return changed;
        }

        /// <summary>O nome que aparece: o do .desktop (no idioma do usuário) ou o nome do arquivo sem extensão.</summary>
        private static string NameOf(string path)
        {
            if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                if (DesktopEntry.LocalName(DesktopEntry.Read(path)) is { Length: > 0 } n) return n;
            return Path.GetFileNameWithoutExtension(path);
        }

        private static bool Adopt(IItemOwner owner, ShortcutItem item)
        {
            if (item.OriginalPath != null || item.SourcePath != null) return false;
            if (!DesktopFiles.TryTake(item.Path, owner.Id, out var moved)) return false;
            item.OriginalPath = item.Path;
            item.Path = moved;
            return true;
        }

        public static void RemoveItem(IItemOwner owner, ShortcutItem item)
        {
            DesktopFiles.Restore(item); // devolve à área de trabalho, se veio de lá
            owner.Items.Remove(item);
            if (owner is FolderModel fo) GroupLauncher.SyncHidden(fo); // ficou vazia: pode sumir (se a opção estiver ligada)
            ConfigStore.Save();
            OwnerChanged?.Invoke(owner);
        }

        // ---------- pastas ----------

        public static void RemoveGroupData(FolderModel f)
        {
            foreach (var item in f.Items) DesktopFiles.Restore(item);
            GroupLauncher.Delete(f);
            Cfg.Folders.Remove(f);
        }

        public static FolderModel CreateFolder(string name)
        {
            var f = new FolderModel { Name = string.IsNullOrWhiteSpace(name) ? "Pasta" : name.Trim(), Fresh = true };
            Cfg.Folders.Add(f);
            GroupLauncher.Ensure(f, force: true);
            if (f.LauncherPath != null) f.Name = Path.GetFileNameWithoutExtension(f.LauncherPath);
            ConfigStore.Save();
            ListChanged?.Invoke();
            return f;
        }

        public static void RenameFolder(FolderModel f, string name)
        {
            GroupLauncher.Rename(f, name.Trim());
            ConfigStore.Save();
            OwnerChanged?.Invoke(f);
            ListChanged?.Invoke();
        }

        public static void DeleteFolder(FolderModel f)
        {
            RemoveGroupData(f);
            ConfigStore.Save();
            ListChanged?.Invoke();
        }

        // ---------- flutuantes ----------

        public static FloatingModel CreateFloat(string name)
        {
            var m = new FloatingModel { Name = string.IsNullOrWhiteSpace(name) ? "Flutuante" : name.Trim() };
            Cfg.Floats.Add(m);
            ConfigStore.Save();
            ListChanged?.Invoke();
            return m;
        }

        public static void DeleteFloat(FloatingModel m)
        {
            foreach (var item in m.Items) DesktopFiles.Restore(item);
            Cfg.Floats.Remove(m);
            ConfigStore.Save();
            ListChanged?.Invoke();
        }

        // ---------- sincronia com a área de trabalho ----------

        /// <summary>
        /// Garante um atalho por pasta. Se o usuário apagou o atalho (Dolphin, Plasma), a pasta é removida e os
        /// atalhos devolvidos; se renomeou, a pasta acompanha o nome.
        /// </summary>
        public static void ReconcileAll()
        {
            foreach (var f in Cfg.Folders.ToList())
            {
                // pasta guardada em Ocultas/ (vazia, de propósito) não é pasta apagada
                if (f.LauncherPath != null && !File.Exists(f.LauncherPath) && !GroupLauncher.IsStashed(f))
                {
                    var found = DesktopEntry.FindById(f.Id);
                    if (found == null) { RemoveGroupData(f); continue; } // o usuário apagou a pasta
                    f.LauncherPath = found;
                }

                if (f.LauncherPath != null && File.Exists(f.LauncherPath))
                {
                    var entry = DesktopEntry.Read(f.LauncherPath);
                    // o Plasma mostra o Name=; renomear por lá muda ele (e/ou o nome do arquivo)
                    string? shown = DesktopEntry.LocalName(entry);
                    if (!string.IsNullOrWhiteSpace(shown)) f.Name = shown!;
                }

                bool changed = f.Items.RemoveAll(i => (i.OriginalPath != null || i.SourcePath != null) && !File.Exists(i.Path)) > 0;
                foreach (var item in f.Items.ToList()) if (Adopt(f, item)) changed = true;

                GroupLauncher.Ensure(f, force: changed);
            }
            foreach (var m in Cfg.Floats)
                m.Items.RemoveAll(i => (i.OriginalPath != null || i.SourcePath != null) && !File.Exists(i.Path));

            ConfigStore.Save();
        }

        /// <summary>Refaz o ícone de cada pasta se o tema, o destaque ou o desenho mudou. false = alguma falhou (tentar de novo).</summary>
        public static bool RefreshThemeIcons()
        {
            string sig = PastelTheme.Signature;
            if (Cfg.ThemeSig == sig) return true;

            IconCache.Clear();
            bool allOk = true;
            foreach (var f in Cfg.Folders.ToList())
                if (!GroupLauncher.Ensure(f, force: true)) allOk = false;

            if (allOk) { Cfg.ThemeSig = sig; ConfigStore.Save(); }
            return allOk;
        }
    }
}
