using System;
using System.Linq;

namespace Pastel
{
    internal static class Program
    {
        /// <summary>Argumentos com que o Pastel foi iniciado (a 1ª instância trata; as outras só entregam e saem).</summary>
        public static string[] StartupArgs = Array.Empty<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args.Skip(1).ToArray());
            if (args.Length > 0 && args[0] == "--selftest-gui") return GuiSelfTest.Run();
            if (args.Length > 0 && args[0] == "--x11-windows") // ferramenta de diagnóstico: lista as janelas do X11
            {
                foreach (var w in X11Inspect.TopLevels().Where(w => w.Name is { Length: > 0 }))
                    Console.WriteLine($"{w.Name}|{w.X},{w.Y}|{w.W}x{w.H}|{w.Depth}");
                return 0;
            }
            if (args.Length > 0 && args[0] is "--version" or "-v")
            {
                Console.WriteLine("Pastel para Linux 0.1.0");
                return 0;
            }

            // Já tem um Pastel rodando? Entrega os argumentos a ele (clique numa pasta, menu de contexto) e sai.
            if (Ipc.TrySend(args)) return 0;

            StartupArgs = args;
            return UiHost.Run();
        }
    }
}
