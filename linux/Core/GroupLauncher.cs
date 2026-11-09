using System;
using System.IO;

namespace Pastel
{
    /// <summary>
    /// Cada pasta é um atalho .desktop na área de trabalho, com o ícone gerado (grade 2x2 dos apps).
    /// O Plasma cuida de posição, grade, renomear e excluir; o Pastel acompanha (ver Library.ReconcileAll).
    /// </summary>
    internal static class GroupLauncher
    {
        public static string? IconPath(FolderModel f) =>
            f.IconRev <= 0 ? null : Path.Combine(Paths.IconsDir, $"{f.Id}-{f.IconRev}.png");

        /// <summary>
        /// Garante o atalho e o ícone da pasta. Sem force, só recria se faltar; com force, refaz o ícone
        /// (depois de mudar os apps ou o tema). Cada versão do ícone tem um nome novo: o KDE guarda ícones
        /// em cache por caminho, então trocar o nome é o que faz o ícone novo aparecer na hora.
        /// </summary>
        public static bool Ensure(FolderModel f, bool force = false)
        {
            try
            {
                string exe = Paths.ExePath ?? "pastel";
                bool exists = f.LauncherPath != null && File.Exists(f.LauncherPath);
                string? oldIcon = IconPath(f);
                bool iconOk = oldIcon != null && File.Exists(oldIcon);

                // Pasta guardada em Ocultas/ (vazia): não recria o atalho na área de trabalho. Volta só se já não deve ficar oculta.
                bool stashed = !exists && f.LauncherPath != null && IsStashed(f);
                if (stashed)
                {
                    SyncHidden(f);
                    stashed = IsStashed(f);
                    exists = f.LauncherPath != null && File.Exists(f.LauncherPath);
                    if (stashed && !force && iconOk) return true;
                }

                if (!stashed && exists && !force && iconOk &&
                    string.Equals(DesktopEntry.ReadId(f.LauncherPath!), f.Id, StringComparison.OrdinalIgnoreCase)) { SyncHidden(f); return true; }

                Directory.CreateDirectory(Paths.IconsDir);
                if (!stashed)
                {
                    Directory.CreateDirectory(Paths.DesktopDir);
                    if (!exists)
                        f.LauncherPath = DesktopEntry.UniquePath(Paths.DesktopDir, DesktopEntry.Sanitize(f.Name) + ".desktop");
                }

                f.IconRev++;
                string icon = IconPath(f)!;
                File.WriteAllBytes(icon, FolderIcon.RenderPng(f));
                DesktopEntry.WriteExecutable(stashed ? StashPath(f) : f.LauncherPath!, DesktopEntry.BuildFolderLauncher(f, exe, icon));

                if (oldIcon != null && oldIcon != icon) try { File.Delete(oldIcon); } catch { }
                if (!stashed) SyncHidden(f); // o arquivo recém-criado já nasce no lugar certo
                return true;
            }
            catch { return false; }
        }

        // ---------- ocultar pastas vazias ----------

        /// <summary>Id da pasta com o balão aberto: nunca é ocultada enquanto isso.</summary>
        public static string? EditingId;

        public static string StashDir => Path.Combine(Paths.DataDir, "Ocultas");

        /// <summary>Onde o atalho da pasta fica enquanto ela está oculta (fora da área de trabalho; não usa atributo oculto).</summary>
        public static string StashPath(FolderModel f) => Path.Combine(StashDir, f.Id + ".desktop");

        public static bool IsStashed(FolderModel f) => File.Exists(StashPath(f));

        /// <summary>
        /// Aplica "Ocultar pastas vazias": pasta vazia e não recém-criada tem o atalho MOVIDO da área de trabalho para
        /// Ocultas/; ao receber item, ao desligar a opção ou com o balão aberto, volta ao mesmo caminho. Idempotente
        /// (o watcher da área de trabalho dispara com o nosso próprio move, e isso não muda nada na segunda passada).
        /// f.LauncherPath continua sendo o lugar de origem na área de trabalho.
        /// </summary>
        public static void SyncHidden(FolderModel f)
        {
            if (f.Items.Count > 0) f.Fresh = false;
            if (f.LauncherPath == null) return;
            try
            {
                bool want = ConfigStore.Current.HideEmptyFolders && !f.Fresh && f.Items.Count == 0
                            && !string.Equals(EditingId, f.Id, StringComparison.OrdinalIgnoreCase);
                string stash = StashPath(f);
                bool stashed = File.Exists(stash), onDesk = File.Exists(f.LauncherPath);

                if (want && onDesk && !stashed)
                {
                    Directory.CreateDirectory(StashDir);
                    File.Move(f.LauncherPath, stash, overwrite: true);
                }
                else if (!want && stashed)
                {
                    if (onDesk && string.Equals(DesktopEntry.ReadId(f.LauncherPath), f.Id, StringComparison.OrdinalIgnoreCase))
                    { File.Delete(stash); return; } // já voltou por outro caminho: descarta a cópia guardada
                    Directory.CreateDirectory(Paths.DesktopDir);
                    string dest = onDesk ? DesktopEntry.UniquePath(Paths.DesktopDir, Path.GetFileName(f.LauncherPath)) : f.LauncherPath;
                    File.Move(stash, dest);
                    f.LauncherPath = dest;
                }
            }
            catch { }
        }

        public static void Rename(FolderModel f, string newName)
        {
            if (f.LauncherPath != null && !File.Exists(f.LauncherPath) && IsStashed(f))
            {
                // Guardada (oculta): só o nome de origem muda; o arquivo continua em Ocultas/.
                f.Name = newName;
                f.LauncherPath = DesktopEntry.UniquePath(Paths.DesktopDir, DesktopEntry.Sanitize(newName) + ".desktop");
                Ensure(f, force: true);
                return;
            }
            string? old = f.LauncherPath;
            f.Name = newName;
            if (old != null && File.Exists(old))
            {
                string target = DesktopEntry.UniquePath(Paths.DesktopDir, DesktopEntry.Sanitize(newName) + ".desktop");
                File.Move(old, target);
                f.LauncherPath = target;
            }
            Ensure(f, force: true); // reescreve o Name= dentro do atalho
        }

        public static void Delete(FolderModel f)
        {
            try { if (f.LauncherPath != null) File.Delete(f.LauncherPath); } catch { }
            try { File.Delete(StashPath(f)); } catch { } // pasta oculta: apaga também o guardado
            var icon = IconPath(f);
            try { if (icon != null) File.Delete(icon); } catch { }
        }
    }
}
