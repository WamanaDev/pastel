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

                if (exists && !force && iconOk &&
                    string.Equals(DesktopEntry.ReadId(f.LauncherPath!), f.Id, StringComparison.OrdinalIgnoreCase)) return true;

                Directory.CreateDirectory(Paths.DesktopDir);
                Directory.CreateDirectory(Paths.IconsDir);
                if (!exists)
                    f.LauncherPath = DesktopEntry.UniquePath(Paths.DesktopDir, DesktopEntry.Sanitize(f.Name) + ".desktop");

                f.IconRev++;
                string icon = IconPath(f)!;
                File.WriteAllBytes(icon, FolderIcon.RenderPng(f));
                DesktopEntry.WriteExecutable(f.LauncherPath!, DesktopEntry.BuildFolderLauncher(f, exe, icon));

                if (oldIcon != null && oldIcon != icon) try { File.Delete(oldIcon); } catch { }
                return true;
            }
            catch { return false; }
        }

        public static void Rename(FolderModel f, string newName)
        {
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
            var icon = IconPath(f);
            try { if (icon != null) File.Delete(icon); } catch { }
        }
    }
}
