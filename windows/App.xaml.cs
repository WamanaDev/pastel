using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Pastel
{
    public partial class App : Application
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "Pastel";

        private System.Windows.Forms.NotifyIcon? _tray;
        private FileSystemWatcher? _watcher;
        private DispatcherTimer? _reconcileTimer;
        private FolderPopupWindow? _popup;
        private SettingsWindow? _settings;
        private readonly Dictionary<string, FloatingWindow> _docks = new();
        private readonly HashSet<string> _iconPending = new();
        private string _sendSig = "";
        private FolderMirror? _mirror;
        private readonly HashSet<string> _mirrorDirty = new();
        private DispatcherTimer? _mirrorTimer;
        private FileSystemWatcher? _ruleWatcher;
        private DispatcherTimer? _ruleTimer;
        private readonly HashSet<string> _ruleCandidates = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Avisa as janelas abertas (ex.: configurações) que algo mudou.</summary>
        public event Action? StateChanged;

        private static AppConfig Cfg => ConfigStore.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ConfigStore.Load();
            ShellIntegration.RemoveLegacyKeys(); // chaves do nome anterior (WFolder)
            MigrateAutoStart();
            ShellIntegration.RegisterGroupType(); // tipo de arquivo das pastas (.wfgroup)
            if (Cfg.DesktopMenu) ShellIntegration.Register(); // atualiza o caminho do .exe

            ReconcileAll();
            StartMirrors();
            StartRuleWatcher();
            RefreshSendTo();
            foreach (var m in Cfg.Floats) OpenFloat(m);
            BuildTray();
            StartUpdateChecks();
            StartWatcher();
            RefreshThemeIcons(); // o tema do Windows pode ter mudado com o Pastel fechado
            PurgeRecents();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Ipc.Listen((args, x, y) => Dispatcher.BeginInvoke(new Action(() => Handle(args, x, y))));

            var p = System.Windows.Forms.Cursor.Position;
            Handle(Program.Args, p.X, p.Y);

            // Primeira abertura: pergunta se pode organizar a área de trabalho (depois que tudo já subiu).
            if (Program.Args.Length == 0)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(OfferOrganize));
        }

        /// <summary>
        /// Na primeira abertura de uma instalação nova, oferece arrumar a área de trabalho por tipo.
        /// Quem já tem pastas ou regras (veio de uma versão anterior) não é incomodado. Responder "Agora não" não muda nada.
        /// </summary>
        private void OfferOrganize()
        {
            if (Cfg.OrganizeAsked) return;
            if (Cfg.Folders.Count > 0 || Cfg.Rules.Count > 0 || Cfg.Floats.Count > 0)
            {
                Cfg.OrganizeAsked = true;
                ConfigStore.Save();
                return;
            }

            var files = new List<string>();
            try
            {
                files.AddRange(Directory.EnumerateFiles(DesktopFiles.UserDesktop)
                    .Where(f => AutoRules.Check(f) != AutoRules.Eligibility.Never && !AutoRules.IsTemporary(f)
                                && !DesktopFiles.IsShortNameDuplicate(f)));
            }
            catch { }
            var found = OrganizeOffer.Scan(files);

            Cfg.OrganizeAsked = true; // pergunta uma vez só, qualquer que seja a resposta
            ConfigStore.Save();
            if (found.Count == 0) return;

            var chosen = OrganizeOffer.Ask(found);
            if (chosen == null) return; // "Agora não": tudo como está

            foreach (var id in chosen) AddPresetRule(id);
            int moved = ApplyRulesNow();
            RefreshSendTo();
            Notify();
            MessageBox.Show(
                moved == 0 ? "Nenhum arquivo foi movido."
                           : $"Pronto! {moved} {(moved == 1 ? "item foi organizado" : "itens foram organizados")} em {chosen.Count} {(chosen.Count == 1 ? "pasta" : "pastas")}.\n\nPara desfazer, exclua a pasta ou remova o item dela: tudo volta para a área de trabalho.",
                "Pastel", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _watcher?.Dispose();
            _ruleWatcher?.Dispose();
            _mirror?.Dispose();
            _tray?.Dispose();
            ConfigStore.Save();
            base.OnExit(e);
        }

        // ---------- comandos (duplo clique na pasta, soltar arquivos, menus do botão direito) ----------

        private void Handle(string[] args, int x, int y)
        {
            if (args.Length == 0) return;
            var rest = args.Skip(2).ToList();

            switch (args[0])
            {
                case ShellIntegration.NewGroupArg:
                    NewGroup();
                    break;

                case ShellIntegration.NewFloatArg:
                    NewFloat();
                    break;

                case ShellIntegration.SettingsArg:
                    ShowSettings(args.Length > 1 ? args[1] : null);
                    break;

                case GroupShortcut.GroupFileArg when args.Length >= 2:
                {
                    var f = FindGroupByFile(args[1]);
                    if (f == null) return;
                    // Arquivos soltos sobre a pasta chegam como argumentos extras.
                    if (rest.Count > 0) AddPaths(f, rest);
                    ShowPopup(f, x, y);
                    SchedulePurgeRecents();
                    break;
                }

                // Menu do botão direito: Pastel > Enviar para pasta / flutuante
                case ShellIntegration.SendFolderArg when args.Length >= 3:
                {
                    var f = Cfg.Folders.FirstOrDefault(g => g.Id == args[1]);
                    if (f != null) AddPaths(f, rest);
                    break;
                }
                case ShellIntegration.SendFloatArg when args.Length >= 3:
                {
                    var m = Cfg.Floats.FirstOrDefault(g => g.Id == args[1]);
                    if (m != null) AddPaths(m, rest);
                    break;
                }
                case ShellIntegration.SendFolderNewArg when args.Length >= 2:
                    QueueNew("folder", args.Skip(1));
                    break;
                case ShellIntegration.SendFloatNewArg when args.Length >= 2:
                    QueueNew("float", args.Skip(1));
                    break;
            }
        }

        // O Explorer abre um processo por arquivo selecionado; juntamos os pedidos de "novo" para perguntar o nome uma vez só.
        private string? _newKind;
        private readonly List<string> _newPaths = new();
        private DispatcherTimer? _newTimer;

        private void QueueNew(string kind, IEnumerable<string> paths)
        {
            if (_newKind != null && _newKind != kind) FlushNew();
            _newKind = kind;
            _newPaths.AddRange(paths);
            _newTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _newTimer.Tick -= NewTick;
            _newTimer.Tick += NewTick;
            _newTimer.Stop();
            _newTimer.Start();
        }

        private void NewTick(object? sender, EventArgs e) { _newTimer?.Stop(); FlushNew(); }

        private void FlushNew()
        {
            var kind = _newKind;
            var paths = _newPaths.ToList();
            _newKind = null;
            _newPaths.Clear();
            if (kind == null) return;

            IItemOwner? owner = kind == "folder" ? NewGroup() : NewFloat();
            if (owner != null && paths.Count > 0) AddPaths(owner, paths);
        }

        private static FolderModel? FindGroupByFile(string path)
        {
            var groups = Cfg.Folders;
            var f = groups.FirstOrDefault(g => string.Equals(g.LnkPath, path, StringComparison.OrdinalIgnoreCase));
            if (f != null) return f;

            // Renomeado/movido com o Pastel fechado: o id gravado no arquivo identifica a pasta.
            var id = GroupShortcut.ReadId(path);
            f = groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (f == null) return null;
            f.LnkPath = path;
            f.Name = Path.GetFileNameWithoutExtension(path);
            ConfigStore.Save();
            return f;
        }

        // ---------- popup ----------

        public void ShowPopup(FolderModel f, int x, int y)
        {
            try { _popup?.Close(); } catch (InvalidOperationException) { }
            // Ancora no ícone da pasta (o mouse pode já ter saído de cima dele quando o Explorer
            // terminou de iniciar o programa); sem achar o ícone, usa a posição do mouse.
            string name = f.LnkPath != null ? Path.GetFileNameWithoutExtension(f.LnkPath) : f.Name;
            var anchor = DesktopAnchor.IconRect(name) ?? DesktopAnchor.IconRect(name + GroupShortcut.Ext)
                         ?? new System.Drawing.Rectangle(x - 1, y - 40, 2, 80);

            var pop = new FolderPopupWindow(f, anchor);
            _popup = pop;
            GroupShortcut.EditingId = f.Id;
            pop.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, pop)) _popup = null;
                if (GroupShortcut.EditingId == f.Id) GroupShortcut.EditingId = null;
                if (Cfg.Folders.Contains(f)) GroupShortcut.SyncHidden(f); // ficou vazia com o balão aberto: some agora
            }; // o antigo pode fechar depois do novo abrir
            pop.Show();
        }

        /// <summary>Abre o balão no meio da tela (usado pelo botão "Abrir" das configurações).</summary>
        public void ShowPopupCentered(FolderModel f)
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
            ShowPopup(f, wa.Left + wa.Width / 2, wa.Top + wa.Height / 3);
        }

        // ---------- configurações ----------

        public void ShowSettings(string? page = null)
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(page);
                _settings.Closed += (_, _) => _settings = null;
                _settings.Show();
            }
            else if (page != null) _settings.ShowPage(page);

            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
        }

        private void Notify() => StateChanged?.Invoke();

        // ---------- pastas ----------

        public FolderModel? NewGroup()
        {
            var name = Prompt.Ask("Nova pasta", "Pasta");
            if (name == null) return null; // cancelou
            return CreateGroup(string.IsNullOrWhiteSpace(name) ? "Pasta" : name.Trim());
        }

        private FolderModel CreateGroup(string name)
        {
            var f = new FolderModel { Name = name, Fresh = true };
            Cfg.Folders.Add(f);
            GroupShortcut.Ensure(f, force: true);
            f.Name = Path.GetFileNameWithoutExtension(f.LnkPath ?? f.Name);
            ConfigStore.Save();
            RefreshSendTo();
            Notify();
            return f;
        }

        public void RenameGroup(FolderModel f)
        {
            var name = Prompt.Ask("Renomear pasta", f.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            try { GroupShortcut.Rename(f, name.Trim()); }
            catch { MessageBox.Show("Opa, tropecei aqui. Tenta de novo ou reinicia o Pastel.", "Pastel", MessageBoxButton.OK, MessageBoxImage.Warning); }
            ConfigStore.Save();
            _popup?.Reload();
            RefreshSendTo();
            Notify();
        }

        public void DeleteGroup(FolderModel f)
        {
            var r = MessageBox.Show($"Excluir a pasta \"{f.Name}\"?\n\nOs atalhos que vieram da área de trabalho voltam pra lá.",
                "Pastel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            RemoveGroupData(f);
            ConfigStore.Save();
            RefreshSendTo();
            Notify();
        }

        /// <summary>Liga/desliga "Ocultar pastas vazias": desligar reexibe todas.</summary>
        public void SetHideEmptyFolders(bool on)
        {
            Cfg.HideEmptyFolders = on;
            foreach (var f in Cfg.Folders) GroupShortcut.SyncHidden(f);
            ConfigStore.Save();
        }

        private static void RemoveGroupData(FolderModel f)
        {
            foreach (var item in f.Items) DesktopFiles.Restore(item);
            GroupShortcut.Delete(f);
            Cfg.Folders.Remove(f);
            // As regras que apontavam para ela ficam: se casarem com algo, a pasta é recriada (ver RecreateTarget).
        }

        // ---------- pastas espelhadas (mostram um diretório real) ----------

        /// <summary>Cria uma pasta que espelha um diretório (ex.: Downloads). Nada sai de lá.</summary>
        public FolderModel? NewMirrorGroup()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Escolha a pasta que o Pastel vai mostrar. Os arquivos continuam onde estão.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK || !Directory.Exists(dlg.SelectedPath)) return null;

            string source = dlg.SelectedPath;
            string suggested = new DirectoryInfo(source).Name;
            var name = Prompt.Ask("Nova pasta espelhada", string.IsNullOrWhiteSpace(suggested) ? "Pasta" : suggested);
            if (name == null) return null;

            var f = new FolderModel { Name = string.IsNullOrWhiteSpace(name) ? "Pasta" : name.Trim(), SourcePath = source };
            FolderMirror.Refresh(f);
            Cfg.Folders.Add(f);
            GroupShortcut.Ensure(f, force: true);
            f.Name = Path.GetFileNameWithoutExtension(f.LnkPath ?? f.Name);
            ConfigStore.Save();
            _mirror?.Sync(Cfg.Folders);
            RefreshSendTo();
            Notify();
            return f;
        }

        private void StartMirrors()
        {
            _mirror = new FolderMirror(id => Dispatcher.BeginInvoke(new Action(() => MirrorDirty(id))));
            _mirror.Sync(Cfg.Folders);
            _mirrorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _mirrorTimer.Tick += (_, _) =>
            {
                _mirrorTimer.Stop();
                var ids = _mirrorDirty.ToList();
                _mirrorDirty.Clear();
                foreach (var id in ids)
                {
                    var f = Cfg.Folders.FirstOrDefault(x => x.Id == id);
                    if (f != null && FolderMirror.Refresh(f)) Changed(f);
                }
            };
        }

        private void MirrorDirty(string id)
        {
            _mirrorDirty.Add(id);
            _mirrorTimer?.Stop();
            _mirrorTimer?.Start();
        }

        // ---------- regras automáticas (área de trabalho → pasta ou flutuante) ----------

        public AutoRule NewRule()
        {
            var first = Cfg.Folders.FirstOrDefault(f => !f.IsMirror)?.Id ?? Cfg.Floats.FirstOrDefault()?.Id ?? "";
            var r = new AutoRule { Name = "Regra " + (Cfg.Rules.Count + 1), Enabled = false, TargetId = first };
            r.TargetName = FindOwner(first)?.Name ?? "";
            Cfg.Rules.Add(r);
            ConfigStore.Save();
            return r;
        }

        /// <summary>
        /// Adiciona uma regra pronta por tipo, já ligada. O destino é uma pasta com o nome da categoria
        /// (usa a que já existir com esse nome, senão cria uma). Não mexe nos arquivos que já estão na área de trabalho.
        /// </summary>
        public AutoRule? AddPresetRule(string categoryId)
        {
            var cat = RuleCatalog.Find(categoryId);
            if (cat == null || Cfg.Rules.Any(r => r.Category == categoryId)) return null;

            // A pasta só nasce quando chegar o primeiro arquivo desse tipo (ver RecreateTarget): adicionar uma regra
            // não enche a área de trabalho de pastas vazias. Se já existe uma pasta com o nome do tipo, é ela.
            var target = Cfg.Folders.FirstOrDefault(f => !f.IsMirror && string.Equals(f.Name, cat.Name, StringComparison.CurrentCultureIgnoreCase));
            var rule = new AutoRule { Name = cat.Name, Category = cat.Id, Enabled = true, TargetId = target?.Id ?? "", TargetName = target?.Name ?? cat.Name };
            Cfg.Rules.Add(rule);
            // Regras do usuário primeiro (mais específicas); as prontas depois, na ordem do catálogo.
            Cfg.Rules = Cfg.Rules.Where(r => r.Category.Length == 0)
                .Concat(Cfg.Rules.Where(r => r.Category.Length > 0)
                    .OrderBy(r => Array.FindIndex(RuleCatalog.All, c => c.Id == r.Category))).ToList();
            ConfigStore.Save();
            return rule;
        }

        /// <summary>Remove uma regra pronta. A pasta de destino e o que está dentro dela ficam como estão.</summary>
        public void RemovePresetRule(string categoryId)
        {
            if (Cfg.Rules.RemoveAll(r => r.Category == categoryId) > 0) ConfigStore.Save();
        }

        public void RuleChanged() => ConfigStore.Save();

        public void DeleteRule(AutoRule r)
        {
            Cfg.Rules.Remove(r);
            ConfigStore.Save();
        }

        /// <summary>Destinos possíveis de uma regra: pastas comuns e flutuantes (espelhadas não recebem arquivos).</summary>
        public IEnumerable<IItemOwner> RuleTargets() =>
            Cfg.Folders.Where(f => !f.IsMirror).Cast<IItemOwner>().Concat(Cfg.Floats);

        /// <summary>
        /// O destino da regra ainda não existe (regra recém-adicionada) ou foi excluído (a regra continua valendo): cria a pasta
        /// com o nome guardado, ou reaproveita uma pasta comum com esse nome. Só acontece quando um arquivo realmente casa.
        /// </summary>
        private IItemOwner? RecreateTarget(AutoRule rule)
        {
            string name = rule.TargetName;
            if (string.IsNullOrWhiteSpace(name)) name = RuleCatalog.Find(rule.Category)?.Name ?? rule.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var folder = Cfg.Folders.FirstOrDefault(f => !f.IsMirror && string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase))
                         ?? CreateGroup(name.Trim());
            rule.TargetId = folder.Id;
            rule.TargetName = folder.Name;
            ConfigStore.Save();
            RefreshSendTo();
            return folder;
        }

        private IItemOwner? FindOwner(string id) => RuleTargets().FirstOrDefault(o => o.Id == id);

        private void StartRuleWatcher()
        {
            try
            {
                _ruleWatcher = new FileSystemWatcher(DesktopFiles.UserDesktop)
                {
                    NotifyFilter = NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
            }
            catch { return; }

            _ruleTimer = new DispatcherTimer { Interval = AutoRules.Settle + TimeSpan.FromMilliseconds(500) };
            _ruleTimer.Tick += (_, _) =>
            {
                _ruleTimer.Stop();
                var batch = _ruleCandidates.ToList();
                _ruleCandidates.Clear();
                var unsettled = RunRules(batch, out _);
                // Cópia ou download ainda rolando: tenta de novo depois.
                if (unsettled.Count > 0) { foreach (var p in unsettled) _ruleCandidates.Add(p); _ruleTimer.Start(); }
            };

            void Seen(string path) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Cfg.Rules.Any(r => r.Enabled)) return;
                _ruleCandidates.Add(path);
                _ruleTimer.Stop();
                _ruleTimer.Start();
            }));
            _ruleWatcher.Created += (_, e) => Seen(e.FullPath);
            _ruleWatcher.Renamed += (_, e) => Seen(e.FullPath);
        }

        /// <summary>Aplica as regras agora aos arquivos que já estão soltos na área de trabalho. Retorna quantos foram movidos.</summary>
        public int ApplyRulesNow(AutoRule? only = null)
        {
            var all = new List<string>();
            try { all.AddRange(Directory.EnumerateFiles(DesktopFiles.UserDesktop)); } catch { }
            RunRules(all, out int moved, ignoreSettle: true, only: only);
            return moved;
        }

        /// <summary>Move para o destino de cada regra os arquivos que casam. Devolve os que ainda "assentam".</summary>
        private List<string> RunRules(IEnumerable<string> paths, out int moved, bool ignoreSettle = false, AutoRule? only = null)
        {
            moved = 0;
            var unsettled = new List<string>();
            var touched = new HashSet<IItemOwner>();
            var rules = Cfg.Rules.Where(r => r.Enabled && (only == null || r == only)).ToList();

            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                bool returned;
                lock (DesktopFiles.Returned) returned = DesktopFiles.Returned.Contains(path);
                if (returned && !ignoreSettle) continue; // o usuário acabou de tirar da pasta
                var rule = AutoRules.Find(rules, path);
                if (rule == null) continue;
                var state = AutoRules.Check(path);
                if (state == AutoRules.Eligibility.Never || (ignoreSettle && AutoRules.IsTemporary(path))) continue;
                if (DesktopFiles.IsShortNameDuplicate(path)) continue; // cópia com nome curto deixada por versões antigas
                if (state == AutoRules.Eligibility.NotYet && !ignoreSettle) { unsettled.Add(path); continue; }

                // Só agora, com um arquivo realmente a mover, a pasta excluída é recriada.
                var target = FindOwner(rule.TargetId) ?? RecreateTarget(rule);
                if (target == null) continue;
                if (rule.TargetName != target.Name) { rule.TargetName = target.Name; ConfigStore.Save(); } // acompanha renomeações

                string name = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } n ? n : Path.GetFileName(path);
                if (!DesktopFiles.IsShortcutFile(path))
                {
                    // Arquivo ou programa que não é atalho fica na área de trabalho; a pasta recebe um atalho, uma vez só.
                    if (target.Items.Any(i => string.Equals(i.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                    var link = DesktopFiles.CreateLink(path, target.Id);
                    if (link == null) continue;
                    target.Items.Add(new ShortcutItem { Name = name, Path = link, SourcePath = path });
                }
                else
                {
                    if (!DesktopFiles.TryTake(path, target.Id, out var movedPath)) continue;
                    target.Items.Add(new ShortcutItem { Name = name, Path = movedPath, OriginalPath = path });
                }
                touched.Add(target);
                moved++;
            }
            foreach (var o in touched) Changed(o);
            return unsettled;
        }

        // ---------- flutuantes ----------

        public FloatingModel? NewFloat()
        {
            var name = Prompt.Ask("Novo flutuante", "Flutuante");
            if (name == null) return null;

            var m = new FloatingModel { Name = string.IsNullOrWhiteSpace(name) ? "Flutuante" : name.Trim() };
            Cfg.Floats.Add(m);
            ConfigStore.Save();
            OpenFloat(m);
            RefreshSendTo();
            Notify();
            return m;
        }

        private void OpenFloat(FloatingModel m)
        {
            if (!m.Visible || _docks.ContainsKey(m.Id)) return;
            var w = new FloatingWindow(m);
            _docks[m.Id] = w;
            w.Show();
        }

        private void CloseFloat(FloatingModel m)
        {
            if (!_docks.Remove(m.Id, out var w)) return;
            try { w.Close(); } catch { }
        }

        public void SetFloatVisible(FloatingModel m, bool visible)
        {
            m.Visible = visible;
            ConfigStore.Save();
            if (visible) OpenFloat(m); else CloseFloat(m);
            Notify();
        }

        /// <summary>Orientação, tamanho dos ícones ou conteúdo do flutuante mudou.</summary>
        public void FloatChanged(FloatingModel m)
        {
            ConfigStore.Save();
            if (_docks.TryGetValue(m.Id, out var w)) w.Reload();
            Notify();
        }

        public void RenameFloat(FloatingModel m)
        {
            var name = Prompt.Ask("Renomear flutuante", m.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            m.Name = name.Trim();
            FloatChanged(m);
            RefreshSendTo();
        }

        public void DeleteFloat(FloatingModel m)
        {
            var r = MessageBox.Show($"Excluir o flutuante \"{m.Name}\"?\n\nOs atalhos que vieram da área de trabalho voltam pra lá.",
                "Pastel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            foreach (var item in m.Items) DesktopFiles.Restore(item);
            CloseFloat(m);
            Cfg.Floats.Remove(m);
            ConfigStore.Save();
            RefreshSendTo();
            Notify();
        }

        // ---------- apps dentro de pastas e flutuantes ----------

        public void PickFiles(IItemOwner owner)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Escolha os apps ou atalhos",
                Filter = "Apps e atalhos|*.exe;*.lnk;*.url|Todos os arquivos|*.*",
                DereferenceLinks = false
            };
            if (dlg.ShowDialog() == true) AddPaths(owner, dlg.FileNames);
        }

        public void AddPaths(IItemOwner owner, IEnumerable<string> paths)
        {
            if (owner is FolderModel { IsMirror: true }) return; // pasta espelhada é só leitura
            bool changed = false;
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string p = DesktopFiles.ToLongPath(raw); // o Explorer pode entregar o nome curto (8.3)
                var existing = owner.Items.FirstOrDefault(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase) ||
                                                               string.Equals(i.OriginalPath, p, StringComparison.OrdinalIgnoreCase) ||
                                                               string.Equals(i.SourcePath, p, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // Já estava aqui mas o atalho ainda está na área de trabalho: passa a ser gerenciado.
                    if (Adopt(owner, existing)) changed = true;
                    continue;
                }

                string name = Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p);
                if (string.IsNullOrEmpty(name)) name = p;

                // Atalho que estava na área de trabalho sai de lá (vai para a pasta gerenciada).
                // Arquivo, programa ou pasta (que não é atalho) não se move: a pasta ganha um atalho para ele.
                string? original = null, source = null;
                string path = p;
                if (!DesktopFiles.IsShortcutFile(p) && (File.Exists(p) || Directory.Exists(p)))
                {
                    var link = DesktopFiles.CreateLink(p, owner.Id);
                    if (link != null) { path = link; source = p; }
                }
                else if (DesktopFiles.TryTake(p, owner.Id, out var moved)) { original = p; path = moved; }

                owner.Items.Add(new ShortcutItem { Name = name, Path = path, OriginalPath = original, SourcePath = source });
                changed = true;
            }
            if (changed) Changed(owner);
        }

        /// <summary>Move para a pasta gerenciada um atalho que ainda está solto na área de trabalho.</summary>
        private static bool Adopt(IItemOwner owner, ShortcutItem item)
        {
            if (item.OriginalPath != null) return false;
            if (!DesktopFiles.TryTake(item.Path, owner.Id, out var moved)) return false;
            item.OriginalPath = item.Path;
            item.Path = moved;
            return true;
        }

        /// <summary>O usuário arrastou o item para fora (ex.: área de trabalho): sai da lista.</summary>
        public void ItemMovedOut(IItemOwner owner, ShortcutItem item)
        {
            // O Explorer pode terminar de mover o arquivo um instante depois do arrasto.
            int tries = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (_, _) =>
            {
                if (File.Exists(item.Path))
                {
                    if (++tries < 12) return; // continua esperando (até ~2 s)
                    t.Stop();
                    return;
                }
                t.Stop();
                // Foi parar em algum lugar (em geral a área de trabalho): as regras não podem pegar de volta.
                lock (DesktopFiles.Returned)
                    DesktopFiles.Returned.Add(Path.Combine(DesktopFiles.UserDesktop, Path.GetFileName(item.Path)));
                owner.Items.Remove(item);
                Changed(owner);
            };
            t.Start();
        }

        public void RemoveItem(IItemOwner owner, ShortcutItem item)
        {
            if (owner is FolderModel { IsMirror: true }) return; // não mexe nos arquivos do diretório de origem
            DesktopFiles.Restore(item); // devolve à área de trabalho, se veio de lá
            if (item.SourcePath != null) try { File.Delete(item.Path); } catch { } // atalho criado pelo Pastel; o original não é tocado
            owner.Items.Remove(item);
            Changed(owner);
        }

        /// <summary>A lista de apps mudou: salva e atualiza a pasta (ícone e balão) ou o flutuante.</summary>
        private void Changed(IItemOwner owner)
        {
            ConfigStore.Save();
            if (owner is FloatingModel fm)
            {
                if (_docks.TryGetValue(fm.Id, out var w)) w.Reload();
                Notify();
                return;
            }

            var f = (FolderModel)owner;
            _popup?.Reload();
            Notify();

            // O ícone da pasta é regenerado depois, para não atrasar o balão nem o resto.
            if (_iconPending.Add(f.Id))
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _iconPending.Remove(f.Id);
                    if (!Cfg.Folders.Contains(f)) return;
                    GroupShortcut.Ensure(f, force: true);
                    ConfigStore.Save();
                }));
        }

        // ---------- sincronia com a área de trabalho ----------

        /// <summary>
        /// Garante um arquivo por pasta. Se o usuário apagou o arquivo no Explorer, a pasta é
        /// removida (e os atalhos devolvidos); se renomeou, a pasta acompanha o nome.
        /// </summary>
        private void ReconcileAll()
        {
            foreach (var f in Cfg.Folders.ToList())
            {
                if (f.LnkPath != null && !File.Exists(f.LnkPath) && !GroupShortcut.IsStashed(f))
                {
                    var found = GroupShortcut.IsLegacyLnk(f.LnkPath) ? null : GroupShortcut.FindById(f.Id);
                    if (found == null) { RemoveGroupData(f); continue; } // o usuário apagou a pasta
                    f.LnkPath = found;
                }
                if (f.LnkPath != null) f.Name = Path.GetFileNameWithoutExtension(f.LnkPath);

                if (f.IsMirror)
                {
                    // Pasta espelhada: os itens vêm do diretório, nada a "adotar" nem a devolver.
                    GroupShortcut.Ensure(f, force: FolderMirror.Refresh(f));
                    continue;
                }

                // Itens gerenciados cujo arquivo sumiu (movido por fora) saem da pasta;
                // itens antigos que ficaram soltos na área de trabalho passam a ser gerenciados.
                bool changed = f.Items.RemoveAll(i => i.OriginalPath != null && !File.Exists(i.Path)) > 0;
                foreach (var item in f.Items) if (Adopt(f, item)) changed = true;

                GroupShortcut.Ensure(f, force: changed); // também migra o .lnk da versão anterior
            }
            foreach (var m in Cfg.Floats)
                m.Items.RemoveAll(i => i.OriginalPath != null && !File.Exists(i.Path));

            ConfigStore.Save();
        }

        private void StartWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(DesktopFiles.UserDesktop, "*" + GroupShortcut.Ext)
                {
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
            }
            catch { return; }

            _reconcileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _reconcileTimer.Tick += (_, _) =>
            {
                _reconcileTimer.Stop();
                ReconcileAll();
                _popup?.Reload();
                RefreshSendTo();
                Notify();
            };

            void Poke() => Dispatcher.BeginInvoke(new Action(() => { _reconcileTimer.Stop(); _reconcileTimer.Start(); }));
            _watcher.Deleted += (_, _) => Poke();
            _watcher.Renamed += (_, _) => Poke();
        }

        // ---------- Recentes do Menu Iniciar ----------

        private void PurgeRecents() =>
            ShellIntegration.PurgeRecents(Cfg.Folders.Select(f => f.LnkPath).Where(p => !string.IsNullOrEmpty(p))!);

        private DispatcherTimer? _purgeTimer;

        /// <summary>O Explorer registra a pasta em Recentes ao abrir; apagamos a entrada logo depois.</summary>
        private void SchedulePurgeRecents()
        {
            _purgeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _purgeTimer.Tick -= PurgeTick;
            _purgeTimer.Tick += PurgeTick;
            _purgeTimer.Stop();
            _purgeTimer.Start();
        }

        private void PurgeTick(object? sender, EventArgs e) { _purgeTimer?.Stop(); PurgeRecents(); }

        // ---------- menu "Enviar para" ----------

        /// <summary>Recria o submenu quando a lista de pastas/flutuantes (ou seus nomes) mudou.</summary>
        private void RefreshSendTo()
        {
            if (!Cfg.SendToMenu) return;
            var targets = Cfg.Folders.Where(f => !f.IsMirror).ToList(); // espelhada não recebe arquivos
            string sig = string.Join("|", targets.Select(f => f.Id + f.Name)) + "#" + string.Join("|", Cfg.Floats.Select(f => f.Id + f.Name));
            if (sig == _sendSig) return;
            _sendSig = sig;
            ShellIntegration.RegisterSendTo(targets, Cfg.Floats);
        }

        public void SetSendToMenu(bool on)
        {
            Cfg.SendToMenu = on;
            ConfigStore.Save();
            _sendSig = "";
            if (on) RefreshSendTo(); else ShellIntegration.UnregisterSendTo();
        }

        public void SetDesktopMenu(bool on)
        {
            Cfg.DesktopMenu = on;
            ConfigStore.Save();
            if (on) ShellIntegration.Register(); else ShellIntegration.Unregister();
        }

        public void OpenDataFolder()
        {
            try { Process.Start("explorer.exe", ConfigStore.Dir); } catch { }
        }

        // ---------- ícone da bandeja ----------

        private void BuildTray()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Nova pasta", null, (_, _) => NewGroup());
            menu.Items.Add("Novo flutuante", null, (_, _) => NewFloat());
            menu.Items.Add("Configurações…", null, (_, _) => ShowSettings());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Sair", null, (_, _) => Shutdown());

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Pastel",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (_, _) => ShowSettings();
            _tray.BalloonTipClicked += (_, _) => { if (_pending != null) _ = OfferUpdateAsync(_pending); };
        }

        // ---------- atualização ----------

        private Updater.Release? _pending;
        private System.Windows.Forms.ToolStripItem? _updateItem;
        private DispatcherTimer? _updateTimer;

        /// <summary>Procura sozinho pouco depois de abrir e a cada 12 horas (se o usuário não desligou).</summary>
        private void StartUpdateChecks()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer!.Interval = TimeSpan.FromHours(12);
                if (Cfg.CheckUpdates) await CheckForUpdateAsync(manual: false);
            };
            _updateTimer.Start();
        }

        /// <summary>Retorna a mensagem para o usuário quando a checagem foi pedida por ele.</summary>
        public async System.Threading.Tasks.Task<string?> CheckForUpdateAsync(bool manual)
        {
            var (rel, ok) = await Updater.CheckAsync();
            if (!ok) return manual ? "Não consegui falar com o GitHub agora. Tente de novo mais tarde." : null;
            if (rel == null) return manual ? "Você já está na versão mais recente." : null;

            _pending = rel;
            if (_tray != null && _updateItem == null)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem(
                    $"Atualizar para {rel.Version.Major}.{rel.Version.Minor}.{rel.Version.Build}…", null,
                    (_, _) => _ = OfferUpdateAsync(rel));
                _tray.ContextMenuStrip!.Items.Insert(0, item);
                _updateItem = item;
            }
            if (manual) { await OfferUpdateAsync(rel); return null; }
            if (Cfg.SkippedVersion != rel.Tag)
                _tray?.ShowBalloonTip(8000, "Pastel: versão nova", $"A versão {rel.Tag} está disponível. Clique para atualizar.",
                    System.Windows.Forms.ToolTipIcon.Info);
            return null;
        }

        private async System.Threading.Tasks.Task OfferUpdateAsync(Updater.Release rel)
        {
            var r = MessageBox.Show($"Tem uma versão nova do Pastel: {rel.Tag} (você está na {Updater.Current}).\n\nAtualizar agora? O Pastel fecha e abre de novo sozinho.",
                "Pastel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) { Cfg.SkippedVersion = rel.Tag; ConfigStore.Save(); return; }

            if (await Updater.DownloadAndRunAsync(rel)) { Shutdown(); return; }
            MessageBox.Show("Não consegui baixar ou conferir a atualização, então não instalei nada. Tente de novo mais tarde.",
                "Pastel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                using var s = typeof(App).Assembly.GetManifestResourceStream("Pastel.ico");
                if (s != null) return new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        // ---------- tema ----------

        private DispatcherTimer? _themeTimer, _retryTimer;

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle) return;

            // Vem de outra thread; espera um instante porque o Windows dispara vários avisos seguidos.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _themeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _themeTimer.Tick -= ThemeTick;
                _themeTimer.Tick += ThemeTick;
                _themeTimer.Stop();
                _themeTimer.Start();
            }));
        }

        private void ThemeTick(object? sender, EventArgs e)
        {
            _themeTimer?.Stop();
            ApplyAppearance();
        }

        /// <summary>O tema ou a cor de destaque mudou (no Windows ou nas configurações): refaz o que depende deles.</summary>
        public void ApplyAppearance()
        {
            IconService.ClearStyled();
            RefreshThemeIcons();
            foreach (var w in _docks.Values) w.Reload();
            Notify();
        }

        /// <summary>
        /// Refaz o ícone de cada pasta se o tema, o destaque ou o desenho mudou. A assinatura só é
        /// salva quando TODAS as pastas foram gravadas; se alguma falhar, tenta de novo em seguida.
        /// </summary>
        private void RefreshThemeIcons()
        {
            string sig = WindowsTheme.Signature;
            if (Cfg.ThemeSig == sig) return;

            IconService.ClearStyled();
            bool allOk = true;
            foreach (var f in Cfg.Folders.ToList())
                if (!GroupShortcut.Ensure(f, force: true)) allOk = false;

            if (allOk)
            {
                Cfg.ThemeSig = sig;
                ConfigStore.Save();
                return;
            }

            _retryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick -= RetryTick;
            _retryTimer.Tick += RetryTick;
            _retryTimer.Stop();
            _retryTimer.Start();
        }

        private void RetryTick(object? sender, EventArgs e)
        {
            _retryTimer?.Stop();
            RefreshThemeIcons();
        }

        // ---------- iniciar com o Windows ----------

        /// <summary>Se o app iniciava com o Windows sob o nome antigo (WFolder), passa para o nome novo.</summary>
        private static void MigrateAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key?.GetValue("WFolder") == null) return;
                key.DeleteValue("WFolder", false);
                key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            }
            catch { }
        }

        public static bool IsAutoStart()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) != null;
        }

        public static void SetAutoStart(bool on)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunName, false);
        }
    }
}
