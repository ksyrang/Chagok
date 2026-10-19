using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ProcessReader
{
    public static string? ReadPath(int processId)
    {
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return null;
        var path = new StringBuilder(32768); var size = path.Capacity;
        return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
    }

    public static (IReadOnlyList<ProcessObservation> Processes, bool Complete, bool Unreadable) Snapshot(IEnumerable<string> paths, int session)
    {
        var result = new List<ProcessObservation>(); var complete = true; var unreadable = false;
        foreach (var name in paths.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { complete = false; continue; }
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session) continue;
                        var path = ReadPath(process.Id);
                        result.Add(new(process.Id, path)); unreadable |= path is null;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    { result.Add(new(process.Id, null)); unreadable = true; }
                }
            }
        }
        return (result, complete, unreadable);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
}
