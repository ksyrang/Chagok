using System.Buffers.Binary;
using System.Text.Json;

namespace FileOrganizer.Core;

public static class ShellLaunch
{
    private const int MaxPayload = 256 * 1024;

    public static string? ParseArguments(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return null;
        if (args.Count != 2 || args[0] != "--add-rule")
            throw new ArgumentException("지원하지 않는 실행 인수입니다.");
        return ValidateDestination(args[1]);
    }

    public static string ValidateDestination(string value)
    {
        if (value.Length > 32768) throw new ArgumentException("폴더 경로가 너무 깁니다.");
        return PathSafety.Normalize(value);
    }

    public static string BuildCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('"')
            || executable.Any(char.IsControl) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("배포된 실행 파일에서 탐색기 메뉴를 등록하세요.");
        return $"\"{executable}\" --add-rule \"%1\"";
    }

    public static byte[] BuildPackageManifest(byte[] template, string executable)
    {
        _ = BuildCommand(executable);
        using var source = new MemoryStream(template);
        var document = System.Xml.Linq.XDocument.Load(source);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var app = document.Root?.Element(ns + "Applications")?.Element(ns + "Application")
            ?? throw new InvalidDataException("탐색기 연동 manifest의 Application이 없습니다.");
        app.SetAttributeValue("Executable", Path.GetFileName(executable));
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    public static async Task WriteRequestAsync(Stream stream, string destination, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(ValidateDestination(destination));
        if (payload.Length > MaxPayload) throw new InvalidDataException("탐색기 요청이 너무 큽니다.");
        var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length);
        await stream.WriteAsync(size, token); await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    public static async Task<string> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var size = new byte[4]; await stream.ReadExactlyAsync(size, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length <= 0 || length > MaxPayload) throw new InvalidDataException("잘못된 탐색기 요청 크기입니다.");
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload, token);
        var destination = JsonSerializer.Deserialize<string>(payload)
            ?? throw new InvalidDataException("탐색기 요청이 비어 있습니다.");
        return ValidateDestination(destination);
    }
}
