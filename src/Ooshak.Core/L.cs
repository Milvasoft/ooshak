namespace Ooshak.Core;

/// <summary>Translates interface and spoken text. The Turkish source sentence is the lookup key.</summary>
public static class L
{
    public const string English = "en";
    public const string Turkish = "tr";

    public static string Language { get; set; } = Turkish;

    public static bool IsEnglish => Language == English;

    public static string T(string turkish) =>
        IsEnglish && Translations.English.TryGetValue(turkish, out var english) ? english : turkish;

    public static string T(string turkish, params object?[] args) => string.Format(T(turkish), args);
}
