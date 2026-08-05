using System.Reflection;

namespace Client.Main.Data;

internal static class EmbeddedS6Data
{
    private const string ResourcePrefix = "Client.Main.Data.S6.";

    public static Stream Open(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded S6 resource not found: {resourceName}", resourceName);
    }
}
