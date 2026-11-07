using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pastel
{
    internal static class Program
    {
        public static string[] Args = Array.Empty<string>();

        // Ponto de entrada próprio: a 2ª instância (duplo clique num atalho de grupo) só avisa a
        // 1ª por um pipe e sai, sem nem carregar o WPF.
        [STAThread]
        private static int Main(string[] args)
        {
            // Chamado pelo desinstalador: devolve os atalhos, apaga as pastas e os dados. Sem janela.
            if (args.Length > 0 && args[0] == "--uninstall-cleanup") return UninstallCleanup.Run();

            // Chamado pelo instalador: fecha um Pastel antigo (que seguraria a instância única) e refaz os registros.
            if (args.Length > 0 && args[0] == "--register") return UninstallCleanup.Register();

            using var mutex = new Mutex(true, "Pastel.SingleInstance", out bool created);
            if (!created)
            {
                Ipc.Send(args);
                return 0;
            }

            RunPrimary(args);
            try { mutex.ReleaseMutex(); } catch { }
            return 0;
        }

        // Separado de Main para o JIT não carregar o WPF (App) na 2ª instância, que só avisa a 1ª.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunPrimary(string[] args)
        {
            Args = args;
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }

    /// <summary>Desinstalação: os atalhos voltam para a área de trabalho e o que o Pastel criou é apagado.</summary>
    internal static class UninstallCleanup
    {
        private static void KillOthers()
        {
            int me = Environment.ProcessId;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Pastel"))
            {
                if (p.Id == me) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        public static int Register()
        {
            try
            {
                KillOthers();
                ConfigStore.Load();
                ShellIntegration.RegisterGroupType();
                if (ConfigStore.Current.DesktopMenu) ShellIntegration.Register();
                ShellIntegration.RegisterSendTo(ConfigStore.Current.Folders, ConfigStore.Current.Floats);
                return 0;
            }
            catch { return 1; }
        }

        public static int Run()
        {
            try
            {
                KillOthers(); // para ele não regravar nada

                ConfigStore.Load();
                var cfg = ConfigStore.Current;
                foreach (var f in cfg.Folders)
                {
                    foreach (var item in f.Items) DesktopFiles.Restore(item);
                    GroupShortcut.Delete(f); // apaga o .wfgroup da área de trabalho
                }
                foreach (var m in cfg.Floats)
                    foreach (var item in m.Items) DesktopFiles.Restore(item);

                // a pasta de dados (config e Grupos\) só sai depois de devolver os atalhos
                try { Directory.Delete(ConfigStore.Dir, true); } catch { }
                return 0;
            }
            catch { return 1; }
        }
    }

    /// <summary>Comunicação entre a 2ª instância e a 1ª (quem está na bandeja).</summary>
    internal static class Ipc
    {
        private const string PipeName = "Pastel.Pipe";

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);

        public static void Send(string[] args)
        {
            AllowSetForegroundWindow(-1); // deixa a 1ª instância trazer o popup para a frente
            GetCursorPos(out var p);
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(3000);
                using var w = new StreamWriter(client, new UTF8Encoding(false));
                w.WriteLine($"{p.x},{p.y}");
                foreach (var a in args) w.WriteLine(a);
            }
            catch { }
        }

        public static void Listen(Action<string[], int, int> onMessage)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync();
                        using var r = new StreamReader(server, Encoding.UTF8);

                        var lines = new List<string>();
                        string? line;
                        while ((line = await r.ReadLineAsync()) != null) lines.Add(line);
                        if (lines.Count == 0) continue;

                        var xy = lines[0].Split(',');
                        int.TryParse(xy[0], out int x);
                        int.TryParse(xy.Length > 1 ? xy[1] : "0", out int y);
                        onMessage(lines.GetRange(1, lines.Count - 1).ToArray(), x, y);
                    }
                    catch { await Task.Delay(200); }
                }
            });
        }
    }
}
