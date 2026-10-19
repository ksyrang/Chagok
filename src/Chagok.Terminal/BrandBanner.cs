namespace Chagok.Terminal;

public static class BrandBanner
{
    public const int Height = 5;
    public const int SymbolWidth = 20;
    private static readonly string[] Mark =
    [
        "⠀⠀⠀⠀⠀⠀⠰⠿⠿⢿⣿⣿⣿⣶⣤⣤⣤⣤⣤⡀    ⡜⠛⡄⢸⠀⢸⠀⡜⠛⡄⢠⠛⢣⠀⡜⠛⡄⢸⠀⡜",
        "⠀⠀⠀⠿⠿⠿⣿⣿⣿⣦⣌⡙⠿⠿⠿⠿⠿⠿⠿⠇    ⡇⠀⠀⢸⣤⣼⠀⣧⣤⡇⢸⢠⣤⠀⡇⠀⡇⢸⡜⠀",
        "⣿⣿⣿⣿⣿⡆⠈⣿⣿⣿⠿⠟⠛⢉⣀⣤⣴⣶⡿⠗    ⡇⠀⡄⢸⠀⢸⠀⡇⠀⡇⢸⠀⢸⠀⡇⠀⡇⢸⠘⡄",
        "⣿⣿⣿⣿⣿⣧⡀⠉⠉⠀⠀⠶⣿⣿⣿⣯⣁⠀⠀⠀    ⠘⠛⠀⠘⠀⠘⠀⠃⠀⠃⠀⠛⠃⠀⠘⠛⠀⠘⠀⠘",
        "⢿⣿⣿⣿⣿⣿⣿⣿⣶⣦⡄⠀⠀⠈⣻⣿⣿⣿⣶⣤    ",
    ];
    private static readonly string[] Word =
    [
        "⡜⠛⡄⢸⠀⢸⠀⡜⠛⡄⢠⠛⢣⠀⡜⠛⡄⢸⠀⡜",
        "⡇⠀⠀⢸⣤⣼⠀⣧⣤⡇⢸⢠⣤⠀⡇⠀⡇⢸⡜⠀",
        "⡇⠀⡄⢸⠀⢸⠀⡇⠀⡇⢸⠀⢸⠀⡇⠀⡇⢸⠘⡄",
        "⠘⠛⠀⠘⠀⠘⠀⠃⠀⠃⠀⠛⠃⠀⠘⠛⠀⠘⠀⠘",
        "",
    ];

    // 승인된 D안의 심볼과 CHAGOK 이름을 점 문자로 표시한다. 실행 중 이미지를 읽지 않는다.
    // 좁은 터미널에서는 이름이 잘리지 않도록 심볼을 생략한다.
    public static IReadOnlyList<string> Build(int width) => width >= 80 ? Mark : Word;
}
