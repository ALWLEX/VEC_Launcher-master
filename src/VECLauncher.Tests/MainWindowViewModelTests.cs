using VECLauncher.ViewModels;

namespace VECLauncher.Tests;

public class MainWindowViewModelTests
{
    // ── Human() ──

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(1048576, "1 МБ")]
    [InlineData(1073741824, "1 ГБ")]
    [InlineData(2147483648L, "2 ГБ")]
    public void Human_FormatsBytesCorrectly(long bytes, string expected)
    {
        var result = MainWindowViewModel.Human(bytes);
        Assert.Equal(expected, result);
    }

    // ── Plural() ──

    [Theory]
    [InlineData(1, "файл", "файла", "файлов", "1 файл")]
    [InlineData(2, "файл", "файла", "файлов", "2 файла")]
    [InlineData(5, "файл", "файла", "файлов", "5 файлов")]
    [InlineData(11, "файл", "файла", "файлов", "11 файлов")]
    [InlineData(21, "файл", "файла", "файлов", "21 файл")]
    [InlineData(22, "файл", "файла", "файлов", "22 файла")]
    [InlineData(25, "файл", "файла", "файлов", "25 файлов")]
    [InlineData(111, "файл", "файла", "файлов", "111 файлов")]
    [InlineData(0, "мир", "мира", "миров", "0 миров")]
    public void Plural_ReturnsCorrectRussianForm(int n, string one, string few, string many, string expected)
    {
        var result = MainWindowViewModel.Plural(n, one, few, many);
        Assert.Equal(expected, result);
    }

    // ── FormatMinutes() ──

    [Theory]
    [InlineData(30, "30 с")]
    [InlineData(59, "59 с")]
    [InlineData(60, "1 мин")]
    [InlineData(120, "2 мин")]
    [InlineData(90, "1 мин")]
    [InlineData(3600, "1 ч 0 мин")]
    [InlineData(3661, "1 ч 1 мин")]
    [InlineData(7200, "2 ч 0 мин")]
    [InlineData(5400, "1 ч 30 мин")]
    public void FormatMinutes_FormatsTimeCorrectly(long seconds, string expected)
    {
        var result = MainWindowViewModel.FormatMinutes(seconds);
        Assert.Equal(expected, result);
    }

    // ── MatchesLogLevel() ──

    [Theory]
    [InlineData("[ERROR] NullPointerException", "error", true)]
    [InlineData("[error] something failed", "error", true)]
    [InlineData("java.lang.Exception in main", "error", true)]
    [InlineData("ошибка загрузки файла", "error", true)]
    [InlineData("не удалось подключиться", "error", true)]
    [InlineData("SEVERE: crash", "error", true)]
    [InlineData("[INFO] Game started", "error", false)]
    [InlineData("[WARN] Deprecated API", "error", false)]
    [InlineData("[WARN] Deprecated API", "warn", true)]
    [InlineData("[INFO] All good", "warn", false)]
    [InlineData("[INFO] All good", "all", false)]
    public void MatchesLogLevel_DetectsCorrectLevel(string line, string level, bool expected)
    {
        var result = MainWindowViewModel.MatchesLogLevel(line, level);
        Assert.Equal(expected, result);
    }
}
