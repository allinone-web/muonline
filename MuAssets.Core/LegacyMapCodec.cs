namespace MuAssets.Core;

/// <summary>Checks performed only when crossing into the legacy MU byte codec.</summary>
public static class LegacyMapCodec
{
    public static byte HeaderByte(int value, string field)
    {
        if (value is < byte.MinValue or > byte.MaxValue)
            throw new InvalidDataException(
                $"{field}={value} 超出 MU legacy byte 範圍 0..255；authoring 專案仍有效，但不能輸出 .map/.att。禁止取模或重編號。");

        return checked((byte)value);
    }

    public static void Validate(MapProject project)
    {
        HeaderByte(project.MapNumber, nameof(project.MapNumber));
        HeaderByte(project.AttIndex, nameof(project.AttIndex));
    }

    public static void Validate(MapDocument document)
    {
        HeaderByte(document.MapNumber, nameof(document.MapNumber));
        HeaderByte(document.AttIndex, nameof(document.AttIndex));
    }
}
