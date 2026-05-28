namespace TelegramDrive.Helpers;

/// <summary>
/// Formats byte counts into human-readable size strings.
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long sizeBytes)
    {
        if (sizeBytes <= 0) return "0 B";

        int i = (int)Math.Floor(Math.Log(sizeBytes, 1024));
        i = Math.Min(i, Units.Length - 1);
        double p = Math.Pow(1024, i);
        double s = Math.Round(sizeBytes / p, 2);
        return $"{s} {Units[i]}";
    }
}
