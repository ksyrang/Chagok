using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Chagok.Core;
using Microsoft.Win32;

namespace Chagok.Windows;

internal static class ModernShellIntegration
{
    private const string RegistryPath = @"Software\ksyrang\Chagok\Shell";
    private const string Marker = "15193BE9-9D6D-4D58-9A90-23910F1544AE";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string[] Payload = ["AppxManifest.xml", "Chagok.ShellExtension.dll", "ShellExtension-LICENSES.txt", "Assets/StoreLogo.png", "Assets/Square150x150Logo.png", "Assets/Square44x44Logo.png"];
    public static string CacheRoot => Path.Combine(SessionLease.DefaultDirectory, "shell-extension");
    public static string Status { get; private set; } = "첫 메뉴 연동은 Windows 11 테스트용입니다.";

    public static bool DeveloperModeEnabled
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
                return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int value && value == 1;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    private static byte[] ReadPayload(string path)
    {
        using var source = typeof(ModernShellIntegration).Assembly.GetManifestResourceStream("Chagok.Shell." + path.Replace('/', '.'))
            ?? throw new IOException("탐색기 연동 리소스가 없습니다: " + path);
        using var memory = new MemoryStream(); source.CopyTo(memory); return memory.ToArray();
    }

    public static async Task<string> EnsureRegisteredAsync(CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || RuntimeInformation.OSArchitecture != Architecture.X64)
            return Status = "첫 메뉴 테스트 연동은 x64 Windows 11에서 지원합니다.";
        using (var existing = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true))
        {
            if (existing?.GetValue("ChagokOwner") as string == Marker && Environment.ProcessPath is { } current)
                existing.SetValue("ExecutablePath", current);
        }
        if (!DeveloperModeEnabled)
            return Status = "첫 메뉴 테스트 연동은 Windows 개발자 모드가 필요합니다. 기존 탐색기 메뉴는 사용할 수 있습니다.";
        await Gate.WaitAsync(token);
        try
        {
            Status = "Windows 11 첫 메뉴 연동을 확인하고 있습니다…";
            var executable = Environment.ProcessPath ?? throw new IOException("실행 파일 위치를 확인할 수 없습니다.");
            _ = ShellLaunch.BuildCommand(executable);
            var manifest = ShellLaunch.BuildPackageManifest(ReadPayload("AppxManifest.xml"), executable); var dll = ReadPayload("Chagok.ShellExtension.dll");
            var version = typeof(ModernShellIntegration).Assembly.GetName().Version!.ToString(3);
            using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            fingerprint.AppendData(manifest); fingerprint.AppendData(dll);
            var revision = Convert.ToHexString(fingerprint.GetHashAndReset())[..12];
            var root = Path.Combine(CacheRoot, version + "-" + revision);
            PathSafety.AssertNoLinks(root); Directory.CreateDirectory(root);
            foreach (var path in Payload)
            {
                token.ThrowIfCancellationRequested();
                var data = path == "AppxManifest.xml" ? manifest : path == "Chagok.ShellExtension.dll" ? dll : ReadPayload(path);
                var destination = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                PathSafety.AssertNoLinks(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    using var actual = File.OpenRead(destination);
                    if (actual.Length == data.Length && SHA256.HashData(actual).AsSpan().SequenceEqual(SHA256.HashData(data))) continue;
                }
                var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
                try { File.WriteAllBytes(temporary, data); File.Move(temporary, destination, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key.GetValue("ChagokOwner") is string owner && owner != Marker)
                throw new IOException("다른 탐색기 연동 정보가 있어 변경하지 않았습니다.");
            key.SetValue("ChagokOwner", Marker); key.SetValue("ExecutablePath", executable);
            var previousRoot = key.GetValue("RegisteredRoot") as string ?? "";
            var previousLocation = key.GetValue("ExternalLocation") as string ?? "";
            var location = Path.GetDirectoryName(executable)!;
            const string script = """
                $ErrorActionPreference = 'Stop'
                $name = 'ksyrang.Chagok.ShellIntegration'
                $package = Get-AppxPackage -Name $name | Where-Object { $_.Publisher -eq 'CN=ksyrang' }
                if ($package -and $package.Version.ToString() -eq $env:CHAGOK_SHELL_VERSION -and $env:CHAGOK_SHELL_PREVIOUS_ROOT -eq $env:CHAGOK_SHELL_ROOT -and $env:CHAGOK_SHELL_PREVIOUS_LOCATION -eq $env:CHAGOK_SHELL_LOCATION) { exit 0 }
                foreach ($item in $package) { Remove-AppxPackage -Package $item.PackageFullName -ErrorAction Stop }
                Add-AppxPackage -Register (Join-Path $env:CHAGOK_SHELL_ROOT 'AppxManifest.xml') -ExternalLocation $env:CHAGOK_SHELL_LOCATION -ErrorAction Stop
                """;
            await RunPowerShellAsync(script, new Dictionary<string, string>
            {
                ["CHAGOK_SHELL_ROOT"] = root, ["CHAGOK_SHELL_LOCATION"] = location,
                ["CHAGOK_SHELL_PREVIOUS_ROOT"] = previousRoot, ["CHAGOK_SHELL_PREVIOUS_LOCATION"] = previousLocation,
                ["CHAGOK_SHELL_VERSION"] = version + ".0"
            }, token);
            key.SetValue("RegisteredRoot", root); key.SetValue("ExternalLocation", location);
            return Status = "Windows 11 첫 메뉴 연동(테스트)이 등록되어 있습니다.";
        }
        catch { Status = "첫 메뉴 연동을 등록하지 못했습니다. 기존 탐색기 메뉴는 별도로 사용할 수 있습니다."; throw; }
        finally { Gate.Release(); }
    }

    public static async Task UnregisterAsync(CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key is null) return;
            if (key.GetValue("ChagokOwner") as string != Marker) throw new IOException("다른 연동 정보는 삭제하지 않습니다.");
            const string script = """
                $ErrorActionPreference = 'Stop'
                Get-AppxPackage -Name 'ksyrang.Chagok.ShellIntegration' | Where-Object { $_.Publisher -eq 'CN=ksyrang' } | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }
                """;
            await RunPowerShellAsync(script, new Dictionary<string, string>(), token);
            key.Close(); Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            Status = "첫 메뉴 연동이 해제되었습니다.";
        }
        finally { Gate.Release(); }
    }

    private static async Task RunPowerShellAsync(string script, IReadOnlyDictionary<string, string> variables, CancellationToken token)
    {
        // 리디렉션된 진행 정보(CLIXML)를 빼고 예외 본문만 UTF-8로 반환한다.
        script = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n$ProgressPreference = 'SilentlyContinue'\ntry {\n"
            + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        foreach (var (name, value) in variables) start.Environment[name] = value;
        using var process = Process.Start(start) ?? throw new IOException("탐색기 연동 등록을 시작하지 못했습니다.");
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw;
            throw new IOException("탐색기 연동 등록 시간이 초과되었습니다.");
        }
        var message = await error; _ = await output;
        if (process.ExitCode != 0) throw new IOException("Windows 첫 메뉴 등록 실패: " + message.Trim());
    }
}
