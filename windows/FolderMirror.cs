using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pastel
{
    /// <summary>
    /// Pasta espelhada: lista o conteúdo de um diretório real (só leitura) e avisa quando ele muda.
    /// Nada é movido, copiado nem apagado do diretório de origem.
    /// </summary>
    internal sealed class FolderMirror : IDisposable
    {
        /// <summary>Teto de itens mostrados, para um diretório enorme não pesar a configuração nem o balão.</summary>
        public const int MaxItems = 200;

        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private readonly Action<string> _changed;

        /// <param name="changed">Chamado (em thread de segundo plano) com o Id da pasta cujo diretório mudou.</param>
        public FolderMirror(Action<string> changed) => _changed = changed;

        public static bool IsAvailable(FolderModel f) => f.IsMirror && Directory.Exists(f.SourcePath);

        /// <summary>Refaz a lista de itens a partir do diretório. Retorna true se mudou.</summary>
        public static bool Refresh(FolderModel f)
        {
            if (!f.IsMirror) return false;
            var fresh = new List<ShortcutItem>();
            try
            {
                if (Directory.Exists(f.SourcePath))
                {
                    var entries = new DirectoryInfo(f.SourcePath!).EnumerateFileSystemInfos()
                        .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .OrderByDescending(e => (e.Attributes & FileAttributes.Directory) != 0)
                        .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Take(MaxItems);
                    foreach (var e in entries)
                    {
                        bool dir = (e.Attributes & FileAttributes.Directory) != 0;
                        string name = dir ? e.Name : Path.GetFileNameWithoutExtension(e.Name);
                        if (string.IsNullOrEmpty(name)) name = e.Name;
                        fresh.Add(new ShortcutItem { Name = name, Path = e.FullName });
                    }
                }
            }
            catch { /* sem acesso: fica vazia */ }

            bool same = fresh.Count == f.Items.Count &&
                        fresh.Zip(f.Items, (a, b) => a.Path == b.Path).All(x => x);
            if (same) return false;
            f.Items = fresh;
            return true;
        }

        /// <summary>Liga um vigia para cada pasta espelhada e desliga os que não servem mais.</summary>
        public void Sync(IEnumerable<FolderModel> folders)
        {
            var wanted = folders.Where(IsAvailable).ToDictionary(f => f.Id);
            foreach (var id in _watchers.Keys.Where(k => !wanted.ContainsKey(k)).ToList()) Drop(id);

            foreach (var f in wanted.Values)
            {
                if (_watchers.TryGetValue(f.Id, out var w) &&
                    string.Equals(w.Path, f.SourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                Drop(f.Id);
                try
                {
                    string id = f.Id;
                    var nw = new FileSystemWatcher(f.SourcePath!)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        EnableRaisingEvents = true
                    };
                    nw.Created += (_, _) => _changed(id);
                    nw.Deleted += (_, _) => _changed(id);
                    nw.Renamed += (_, _) => _changed(id);
                    _watchers[id] = nw;
                }
                catch { /* diretório sem acesso: segue sem vigia */ }
            }
        }

        private void Drop(string id)
        {
            if (_watchers.Remove(id, out var w)) w.Dispose();
        }

        public void Dispose()
        {
            foreach (var w in _watchers.Values) w.Dispose();
            _watchers.Clear();
        }
    }
}
