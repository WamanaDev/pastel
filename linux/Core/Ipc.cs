using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Pastel
{
    /// <summary>
    /// Só uma instância do Pastel roda por usuário. Um clique numa pasta (ou um item do menu de contexto)
    /// inicia outro processo, que entrega os argumentos para o que já está rodando e sai.
    /// No Linux o "pipe" nomeado vira um socket em /tmp, protegido para o próprio usuário.
    /// </summary>
    internal static class Ipc
    {
        private static string PipeName => "pastel-" + Environment.UserName + (Environment.GetEnvironmentVariable("PASTEL_SANDBOX") is { Length: > 0 } s ? "-" + Math.Abs(s.GetHashCode()) : "");

        /// <summary>Entrega os argumentos à instância em execução. false = não há instância (quem chamou vira a principal).</summary>
        public static bool TrySend(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(400);
                X11.TryGetPointer(out int px, out int py);
                using var w = new StreamWriter(client, new UTF8Encoding(false));
                w.WriteLine($"{px},{py}");
                foreach (var a in args) w.WriteLine(a);
                return true;
            }
            catch { return false; }
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
