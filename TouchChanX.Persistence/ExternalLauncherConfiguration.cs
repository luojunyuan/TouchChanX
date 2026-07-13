namespace TouchChanX.Persistence;

public static class ExternalLauncherConfiguration
{
    public const string GamePathPlaceholder = "{GamePath}";

    public static bool IsLauncherPathValid(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    public static bool HasGamePathPlaceholder(string? arguments) =>
        arguments?.Contains(GamePathPlaceholder, StringComparison.Ordinal) is true;

    public static bool HasQuotedGamePathPlaceholder(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return false;

        var insideQuotes = false;
        var searchIndex = 0;
        while (searchIndex < arguments.Length)
        {
            var placeholderIndex = arguments.IndexOf(
                GamePathPlaceholder,
                searchIndex,
                StringComparison.Ordinal);
            var scanEnd = placeholderIndex >= 0 ? placeholderIndex : arguments.Length;

            for (; searchIndex < scanEnd; searchIndex++)
            {
                if (arguments[searchIndex] == '"' && !IsEscapedQuote(arguments, searchIndex))
                    insideQuotes = !insideQuotes;
            }

            if (placeholderIndex < 0)
                return false;
            if (insideQuotes)
                return true;

            searchIndex += GamePathPlaceholder.Length;
        }

        return false;
    }

    public static bool AreArgumentsValid(string? arguments) =>
        HasGamePathPlaceholder(arguments) && !HasQuotedGamePathPlaceholder(arguments);

    public static bool IsValid(string? path, string? arguments) =>
        IsLauncherPathValid(path) && AreArgumentsValid(arguments);

    private static bool IsEscapedQuote(string value, int quoteIndex)
    {
        var backslashCount = 0;
        for (var index = quoteIndex - 1; index >= 0 && value[index] == '\\'; index--)
            backslashCount++;

        return backslashCount % 2 != 0;
    }
}
