using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Pastel
{
    internal static class UiHost
    {
        public static AppBuilder BuildApp() => AppBuilder.Configure<PastelApp>().UsePlatformDetect().LogToTrace();

        public static int Run() => BuildApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
    }

    internal sealed class PastelApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // o Pastel vive na bandeja, mesmo sem janelas
                if (GuiSelfTest.Active) GuiSelfTest.Start(desktop); else Controller.Start(this, desktop);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    /// <summary>
    /// O "cérebro" do app: recebe os comandos (clique numa pasta, menu de contexto, bandeja), abre e fecha as janelas,
    /// acompanha o que acontece na área de trabalho e o tema do KDE. As regras ficam em Library; aqui é só a ligação.
    /// </summary>
    internal static class Controller
    {
        private static Application? _app;
        private static IClassicDesktopStyleApplicationLifetime? _lifetime;
        private static PopupWindow? _popup;
        private static SettingsWindow? _settings;
        private static readonly Dictionary<string, FloatingWindow> Docks = new();
        private static TrayIcon? _tray;
        private static string _sendSig = "";
        private static AppConfig Cfg => ConfigStore.Current;

        /// <summary>Avisa as janelas abertas (ex.: configurações) que algo mudou.</summary>
        public static event Action? StateChanged;
        private static void Notify() => StateChanged?.Invoke();

        private static string IconFile => Path.Combine(Paths.DataDir, "pastel.png");
        private static string Exe => Paths.ExePath ?? "pastel";

        // ---------- partida ----------

        public static void Start(Application app, IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _app = app;
            _lifetime = lifetime;

            ConfigStore.Load();
            EnsureAppIcon();
            ApplyThemeVariant();

            ShellMenus.RegisterApplication(Exe, IconFile);
            Library.ReconcileAll();
            RefreshServiceMenu();
            foreach (var m in Cfg.Floats) OpenFloat(m);
            BuildTray();
            StartUpdateChecks();
            StartWatchers();
            RefreshThemeIcons();

            Library.OwnerChanged += OnOwnerChanged;
            Library.ListChanged += () => { RefreshServiceMenu(); Notify(); };
            Ipc.Listen((args, x, y) => Dispatcher.UIThread.Post(() => Handle(args, x, y)));

            X11.TryGetPointer(out int px, out int py);
            Handle(Program.StartupArgs, px, py);
        }

        private static void EnsureAppIcon()
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                using var s = AssetLoader.Open(new Uri("avares://pastel/Assets/pastel.png"));
                using var f = File.Create(IconFile);
                s.CopyTo(f);
            }
            catch { }
        }

        public static WindowIcon WindowIcon() => new(new Bitmap(AssetLoader.Open(new Uri("avares://pastel/Assets/pastel.png"))));

        private static void ApplyThemeVariant()
        {
            if (_app != null) _app.RequestedThemeVariant = PastelTheme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        // ---------- comandos ----------

        private static void Handle(string[] args, int x, int y)
        {
            if (args.Length == 0) return;
            var rest = args.Skip(2).ToList();

            switch (args[0])
            {
                case ShellMenus.BackgroundArg: break; // iniciado com a sessão: só fica na bandeja
                case ShellMenus.NewFolderArg: NewFolder(); break;
                case ShellMenus.NewFloatArg: NewFloat(); break;
                case ShellMenus.SettingsArg: ShowSettings(args.Length > 1 ? args[1] : null); break;

                case ShellMenus.FolderArg when args.Length >= 2:
                {
                    var f = Cfg.Folders.FirstOrDefault(g => g.Id == args[1]);
                    if (f == null) return;
                    if (rest.Count > 0) AddPaths(f, rest); // arquivos soltos sobre o atalho chegam como argumentos extras
                    ShowPopup(f, x, y);
                    break;
                }
                case ShellMenus.SendFolderArg when args.Length >= 3:
                {
                    var f = Cfg.Folders.FirstOrDefault(g => g.Id == args[1]);
                    if (f != null) AddPaths(f, rest);
                    break;
                }
                case ShellMenus.SendFloatArg when args.Length >= 3:
                {
                    var m = Cfg.Floats.FirstOrDefault(g => g.Id == args[1]);
                    if (m != null) AddPaths(m, rest);
                    break;
                }
                case ShellMenus.SendFolderNewArg when args.Length >= 2: QueueNew("folder", args.Skip(1)); break;
                case ShellMenus.SendFloatNewArg when args.Length >= 2: QueueNew("float", args.Skip(1)); break;
            }
        }

        // O gerenciador de arquivos manda todos os arquivos de uma vez (%F), mas juntamos pedidos próximos
        // para perguntar o nome uma vez só.
        private static string? _newKind;
        private static readonly List<string> NewPaths = new();
        private static DispatcherTimer? _newTimer;

        private static void QueueNew(string kind, IEnumerable<string> paths)
        {
            if (_newKind != null && _newKind != kind) FlushNew();
            _newKind = kind;
            NewPaths.AddRange(paths);
            _newTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _newTimer.Tick -= NewTick;
            _newTimer.Tick += NewTick;
            _newTimer.Stop();
            _newTimer.Start();
        }

        private static void NewTick(object? s, EventArgs e) { _newTimer?.Stop(); FlushNew(); }

        private static async void FlushNew()
        {
            var kind = _newKind;
            var paths = NewPaths.ToList();
            _newKind = null;
            NewPaths.Clear();
            if (kind == null) return;
            IItemOwner? owner = kind == "folder" ? await NewFolderAsync() : await NewFloatAsync();
            if (owner != null && paths.Count > 0) AddPaths(owner, paths);
        }

        // ---------- balão ----------

        public static void ShowPopup(FolderModel f, int x, int y)
        {
            try { _popup?.Close(); } catch { }

            PixelRect anchor;
            if (x == 0 && y == 0)
            {
                // sem posição do mouse (Wayland puro): no meio da tela
                var wa = (_app?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
                anchor = new PixelRect(wa.X + wa.Width / 2 - 1, wa.Y + wa.Height / 3, 2, 80);
            }
            else anchor = new PixelRect(x - 1, y - 40, 2, 80); // o ícone da pasta está em volta do mouse

            var pop = new PopupWindow(f, anchor);
            _popup = pop;
            GroupLauncher.EditingId = f.Id; // aberta: nunca some da área de trabalho
            GroupLauncher.SyncHidden(f);
            pop.Closed += (_, _) =>
            {
                // o balão antigo pode fechar depois de o novo abrir: só o atual libera o EditingId
                if (ReferenceEquals(_popup, pop)) { _popup = null; if (GroupLauncher.EditingId == f.Id) GroupLauncher.EditingId = null; }
                if (Cfg.Folders.Contains(f)) { GroupLauncher.SyncHidden(f); ConfigStore.Save(); } // ficou vazia com o balão aberto: some agora
            };
            pop.ShowAtAnchor();
        }

        public static void ShowPopupCentered(FolderModel f) => ShowPopup(f, 0, 0);

        // ---------- configurações ----------

        public static void ShowSettings(string? page = null)
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

        // ---------- pastas ----------

        public static async void NewFolder() => await NewFolderAsync();

        public static async Task<FolderModel?> NewFolderAsync()
        {
            var name = await Dialogs.AskText("Nova pasta", "Pasta");
            return name == null ? null : Library.CreateFolder(name);
        }

        public static async void RenameFolder(FolderModel f)
        {
            var name = await Dialogs.AskText("Renomear pasta", f.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            try { Library.RenameFolder(f, name); }
            catch { Dialogs.Info("Pastel", "Opa, tropecei aqui. Tenta de novo ou reinicia o Pastel."); }
        }

        public static async void DeleteFolder(FolderModel f)
        {
            if (await Dialogs.Confirm("Excluir pasta", $"Excluir a pasta \"{f.Name}\"?\n\nOs atalhos que vieram da área de trabalho voltam pra lá."))
                Library.DeleteFolder(f);
        }

        // ---------- flutuantes ----------

        public static async void NewFloat() => await NewFloatAsync();

        public static async Task<FloatingModel?> NewFloatAsync()
        {
            var name = await Dialogs.AskText("Novo flutuante", "Flutuante");
            if (name == null) return null;
            var m = Library.CreateFloat(name);
            OpenFloat(m);
            return m;
        }

        private static void OpenFloat(FloatingModel m)
        {
            if (!m.Visible || Docks.ContainsKey(m.Id)) return;
            var w = new FloatingWindow(m);
            Docks[m.Id] = w;
            w.ShowPlaced();
        }

        private static void CloseFloat(FloatingModel m)
        {
            if (!Docks.Remove(m.Id, out var w)) return;
            try { w.Close(); } catch { }
        }

        public static void SetFloatVisible(FloatingModel m, bool visible)
        {
            m.Visible = visible;
            ConfigStore.Save();
            if (visible) OpenFloat(m); else CloseFloat(m);
            Notify();
        }

        public static void FloatChanged(FloatingModel m)
        {
            ConfigStore.Save();
            if (Docks.TryGetValue(m.Id, out var w)) w.Reload();
            Notify();
        }

        public static void RelayoutFloats() { foreach (var w in Docks.Values) w.Relayout(); }

        public static async void RenameFloat(FloatingModel m)
        {
            var name = await Dialogs.AskText("Renomear flutuante", m.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            m.Name = name.Trim();
            FloatChanged(m);
            Library.RaiseListChanged();
        }

        public static async void DeleteFloat(FloatingModel m)
        {
            if (!await Dialogs.Confirm("Excluir flutuante", $"Excluir o flutuante \"{m.Name}\"?\n\nOs atalhos que vieram da área de trabalho voltam pra lá.")) return;
            CloseFloat(m);
            Library.DeleteFloat(m);
        }

        // ---------- apps dentro de pastas e flutuantes ----------

        public static void AddPaths(IItemOwner owner, IEnumerable<string> paths) => Library.AddPaths(owner, paths);

        public static void RemoveItem(IItemOwner owner, ShortcutItem item) => Library.RemoveItem(owner, item);

        public static void Launch(ShortcutItem item)
        {
            if (!AppLauncher.TryOpen(item.Path))
                Dialogs.Info("Pastel", $"Não consegui abrir \"{item.Name}\". Confere se o app ainda existe e tenta de novo.");
        }

        /// <summary>O usuário arrastou o item para fora (ex.: área de trabalho): sai da lista quando o arquivo some.</summary>
        public static void ItemMovedOut(IItemOwner owner, ShortcutItem item)
        {
            int tries = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (_, _) =>
            {
                if (File.Exists(item.Path)) { if (++tries < 12) return; t.Stop(); return; }
                t.Stop();
                owner.Items.Remove(item);
                ConfigStore.Save();
                OnOwnerChanged(owner);
            };
            t.Start();
        }

        /// <summary>Escolher arquivos: usa o seletor do próprio KDE (kdialog) ou do GNOME (zenity).</summary>
        public static async void PickFiles(IItemOwner owner)
        {
            var files = await Task.Run(() => FilePicker.Pick());
            if (files.Count > 0) AddPaths(owner, files);
            else if (!FilePicker.Available)
                Dialogs.Info("Pastel", "Não achei o seletor de arquivos (kdialog). Arraste os apps para a pasta, ou instale o pacote kdialog.");
        }

        private static void OnOwnerChanged(IItemOwner owner)
        {
            if (owner is FloatingModel fm)
            {
                if (Docks.TryGetValue(fm.Id, out var w)) w.Reload();
                Notify();
                return;
            }

            var f = (FolderModel)owner;
            _popup?.Reload();
            Notify();
            // o ícone da pasta é refeito depois, para não atrasar o balão
            Dispatcher.UIThread.Post(() =>
            {
                if (!Cfg.Folders.Contains(f)) return;
                GroupLauncher.Ensure(f, force: true);
                ConfigStore.Save();
            }, DispatcherPriority.Background);
        }

        // ---------- observar a área de trabalho e o tema ----------

        private static FileSystemWatcher? _desktopWatcher, _themeWatcher;
        private static DispatcherTimer? _reconcileTimer, _themeTimer, _retryTimer;

        private static void StartWatchers()
        {
            try
            {
                Directory.CreateDirectory(Paths.DesktopDir);
                _desktopWatcher = new FileSystemWatcher(Paths.DesktopDir, "*.desktop") { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
                _reconcileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _reconcileTimer.Tick += (_, _) =>
                {
                    _reconcileTimer!.Stop();
                    Library.ReconcileAll();
                    _popup?.Reload();
                    RefreshServiceMenu();
                    Notify();
                };
                void Poke() => Dispatcher.UIThread.Post(() => { _reconcileTimer!.Stop(); _reconcileTimer.Start(); });
                _desktopWatcher.Deleted += (_, _) => Poke();
                _desktopWatcher.Renamed += (_, _) => Poke();
            }
            catch { }

            try
            {
                // o KDE grava as cores em kdeglobals: ao mudar o tema, refaz os ícones e as janelas
                if (Directory.Exists(Paths.ConfigHome))
                {
                    _themeWatcher = new FileSystemWatcher(Paths.ConfigHome, "kdeglobals") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
                    _themeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    _themeTimer.Tick += (_, _) => { _themeTimer!.Stop(); ApplyAppearance(); };
                    void Poke() => Dispatcher.UIThread.Post(() => { _themeTimer!.Stop(); _themeTimer.Start(); });
                    _themeWatcher.Changed += (_, _) => Poke();
                    _themeWatcher.Created += (_, _) => Poke();
                    _themeWatcher.Renamed += (_, _) => Poke();
                }
            }
            catch { }
        }

        /// <summary>O tema ou a cor de destaque mudou (no KDE ou nas configurações): refaz o que depende deles.</summary>
        public static void ApplyAppearance()
        {
            IconCache.Clear();
            ApplyThemeVariant();
            RefreshThemeIcons();
            foreach (var w in Docks.Values) w.Reload();
            Notify();
        }

        private static void RefreshThemeIcons()
        {
            if (Library.RefreshThemeIcons()) return;
            _retryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick -= RetryTick;
            _retryTimer.Tick += RetryTick;
            _retryTimer.Stop();
            _retryTimer.Start();
        }

        private static void RetryTick(object? s, EventArgs e) { _retryTimer?.Stop(); RefreshThemeIcons(); }

        // ---------- menus do sistema ----------

        private static void RefreshServiceMenu()
        {
            if (!Cfg.SendToMenu) return;
            string sig = string.Join("|", Cfg.Folders.Select(f => f.Id + f.Name)) + "#" + string.Join("|", Cfg.Floats.Select(f => f.Id + f.Name));
            if (sig == _sendSig) return;
            _sendSig = sig;
            ShellMenus.RegisterServiceMenu(Cfg.Folders, Cfg.Floats, Exe, IconFile);
        }

        public static void SetSendToMenu(bool on)
        {
            Cfg.SendToMenu = on;
            ConfigStore.Save();
            _sendSig = "";
            if (on) RefreshServiceMenu(); else ShellMenus.UnregisterServiceMenu();
        }

        /// <summary>Liga/desliga "Ocultar pastas vazias": desligar devolve todas à área de trabalho.</summary>
        public static void SetHideEmptyFolders(bool on)
        {
            Cfg.HideEmptyFolders = on;
            foreach (var f in Cfg.Folders) GroupLauncher.SyncHidden(f);
            ConfigStore.Save();
        }

        public static void SetAutostart(bool on) => ShellMenus.SetAutostart(on, Exe, IconFile);

        public static void OpenDataFolder() => AppLauncher.OpenFolder(Paths.DataDir);

        // ---------- atualização ----------

        private static Updater.Release? _pending;
        private static NativeMenuItem? _updateItem;
        private static Avalonia.Threading.DispatcherTimer? _updateTimer;

        /// <summary>Procura sozinho pouco depois de abrir e a cada 12 horas (se o usuário não desligou).</summary>
        private static void StartUpdateChecks()
        {
            _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer!.Interval = TimeSpan.FromHours(12);
                if (Cfg.CheckUpdates) await CheckForUpdateAsync(manual: false);
            };
            _updateTimer.Start();
        }

        /// <summary>Devolve a mensagem para mostrar quando a checagem foi pedida pelo usuário.</summary>
        public static async Task<string?> CheckForUpdateAsync(bool manual)
        {
            var (rel, ok) = await Updater.CheckAsync();
            if (!ok) return manual ? "Não consegui falar com o GitHub agora. Tente de novo mais tarde." : null;
            if (rel == null) return manual ? "Você já está na versão mais recente." : null;

            _pending = rel;
            if (_tray?.Menu != null && _updateItem == null)
            {
                _updateItem = new NativeMenuItem($"Atualizar para {rel.Version.Major}.{rel.Version.Minor}.{rel.Version.Build}…");
                _updateItem.Click += (_, _) => _ = OfferUpdateAsync(rel);
                _tray.Menu.Items.Insert(0, _updateItem);
            }
            if (manual) { await OfferUpdateAsync(rel); return null; }
            if (Cfg.SkippedVersion != rel.Tag) Toast("Pastel: versão nova", $"A versão {rel.Tag} está disponível. Clique no ícone do Pastel na bandeja para atualizar.");
            return null;
        }

        private static async Task OfferUpdateAsync(Updater.Release rel)
        {
            if (!await Dialogs.Confirm("Pastel: versão nova",
                    $"Tem uma versão nova do Pastel: {rel.Tag} (você está na {Updater.Current}).\n\nAtualizar agora? O Pastel fecha e abre de novo sozinho.", "Atualizar"))
            {
                Cfg.SkippedVersion = rel.Tag; ConfigStore.Save(); return;
            }
            if (await Updater.DownloadAndInstallAsync(rel)) { _lifetime?.Shutdown(); return; }
            Dialogs.Info("Pastel", "Não consegui baixar ou conferir a atualização, então não instalei nada. Tente de novo mais tarde.");
        }

        /// <summary>Notificação do desktop (libnotify / KDE). Se não houver, o item do menu da bandeja ainda avisa.</summary>
        private static void Toast(string title, string body)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("notify-send") { UseShellExecute = false };
                foreach (var a in new[] { "-a", "Pastel", "-i", IconFile, title, body }) psi.ArgumentList.Add(a);
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        // ---------- bandeja do sistema ----------

        private static void BuildTray()
        {
            try
            {
                var menu = new NativeMenu();
                NativeMenuItem Item(string header, Action action)
                {
                    var mi = new NativeMenuItem(header);
                    mi.Click += (_, _) => action();
                    return mi;
                }
                menu.Add(Item("Nova pasta", NewFolder));
                menu.Add(Item("Novo flutuante", NewFloat));
                menu.Add(Item("Configurações…", () => ShowSettings()));
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(Item("Sair", () => _lifetime?.Shutdown()));

                _tray = new TrayIcon { Icon = WindowIcon(), ToolTipText = "Pastel", Menu = menu };
                _tray.Clicked += (_, _) => ShowSettings();
                TrayIcon.SetIcons(_app!, new TrayIcons { _tray });
            }
            catch { /* sem bandeja (alguns ambientes): o app continua funcionando pelas pastas */ }
        }
    }

    /// <summary>Seletor de arquivos nativo (o KDE tem kdialog; o GNOME, zenity).</summary>
    internal static class FilePicker
    {
        public static bool Available { get; private set; } = true;

        public static List<string> Pick()
        {
            foreach (var (tool, args) in new (string, string[])[]
            {
                ("kdialog", new[] { "--multiple", "--separate-output", "--title", "Escolha os apps ou atalhos", "--getopenfilename", Paths.Home }),
                ("zenity", new[] { "--file-selection", "--multiple", "--separator=\n", "--title=Escolha os apps ou atalhos" })
            })
            {
                try
                {
                    var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true };
                    foreach (var a in args) psi.ArgumentList.Add(a);
                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists).ToList();
                }
                catch { /* não instalado: tenta o próximo */ }
            }
            Available = false;
            return new List<string>();
        }
    }
}
