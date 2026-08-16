namespace ShowWhere.Core;

public static class UserLanguage
{
    public static bool IsKorean(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(character => character is >= '\uAC00' and <= '\uD7A3');

    public static string Select(string? userText, string korean, string english) =>
        IsKorean(userText) ? korean : english;
}
