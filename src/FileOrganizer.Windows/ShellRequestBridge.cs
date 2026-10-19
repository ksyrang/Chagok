using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal static class ShellRequestBridge
{
    private static string PipeName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return $"Chagok-{identity.User?.Value}-{process.SessionId}";
        }
    }

    public static async Task<bool> SendAsync(string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await ShellLaunch.WriteRequestAsync(pipe, destination, timeout.Token);
        var response = new byte[1]; await pipe.ReadExactlyAsync(response, timeout.Token);
        return response[0] == 1;
    }

    public static async Task ListenAsync(Func<string, Task<bool>> accept, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var destination = await ShellLaunch.ReadRequestAsync(pipe, timeout.Token);
                var accepted = await accept(destination);
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or NotSupportedException) { }
        }
    }
}
