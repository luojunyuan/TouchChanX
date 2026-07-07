namespace TouchChanX;

internal static class AssetLoader
{
    private static Stream GetImageStream(string fileName)
    {
        string resourcePath = $"{typeof(AssetLoader).Namespace}.{fileName}";

        var stream = typeof(AssetLoader).Assembly.GetManifestResourceStream(resourcePath);

        return stream ?? throw new FileNotFoundException(resourcePath);
    }

    public static Stream AppSplashIcon => GetImageStream("touchchan_hires.png");
}
