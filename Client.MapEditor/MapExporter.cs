using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;
using Client.Data.OZB;

namespace Client.MapEditor;

public sealed record ExportResult(bool Success, string[] Files, string[] BackedUp, string? Error);

/// <summary>
/// 把 <see cref="MapDocument"/> 寫回客戶端讀得懂的五個檔案。
/// </summary>
/// <remarks>
/// 輸出一律是舊版 XOR 格式（不帶 <c>MAP\x01</c> / <c>ATT\x01</c> 魔數）——
/// <c>MAPReader</c> / <c>ATTReader</c> 沒有魔數時會走舊格式路徑，muonline 讀得到。
/// Season 20 的 ModulusCryptor 只有解密沒有加密，寫不出新格式。
///
/// <b>覆寫既有檔案前一律先備份成 <c>.bak</c></b>。原始資源包是 2.5GB 的官方檔案，
/// 沒有版本控制，寫壞了沒得救。
/// </remarks>
public static class MapExporter
{
    public static async Task<ExportResult> ExportAsync(MapDocument document, string targetDirectory, int worldIndex)
    {
        var written = new List<string>();
        var backedUp = new List<string>();

        try
        {
            Directory.CreateDirectory(targetDirectory);

            string mapPath = Path.Combine(targetDirectory, $"EncTerrain{worldIndex}.map");
            Backup(mapPath, backedUp);
            await new MapWriter().Save(mapPath, new TerrainMapping
            {
                Version = document.MapVersion,
                MapNumber = document.MapNumber,
                Layer1 = document.Layer1,
                Layer2 = document.Layer2,
                Alpha = document.Alpha,
            });
            written.Add(Path.GetFileName(mapPath));

            string attPath = Path.Combine(targetDirectory, $"EncTerrain{worldIndex}.att");
            Backup(attPath, backedUp);
            await new ATTWriter().Save(attPath, BuildAttribute(document));
            written.Add(Path.GetFileName(attPath));

            if (document.Objects.Count > 0)
            {
                string objPath = Path.Combine(targetDirectory, $"EncTerrain{worldIndex}.obj");
                Backup(objPath, backedUp);
                await new OBJWriter().Save(objPath, new OBJ
                {
                    Version = document.ObjVersion,
                    MapNumber = worldIndex,
                    Objects = document.Objects.Select(o => o.To(document.ObjVersion)).ToArray(),
                });
                written.Add(Path.GetFileName(objPath));
            }

            if (document.Height is not null)
            {
                string heightPath = Path.Combine(targetDirectory, "TerrainHeight.OZB");
                Backup(heightPath, backedUp);
                await new OZBWriter().Save(heightPath, document.Height);
                written.Add(Path.GetFileName(heightPath));
            }

            if (document.Light is not null)
            {
                string lightPath = Path.Combine(targetDirectory, "TerrainLight.OZB");
                Backup(lightPath, backedUp);
                await new OZBWriter().Save(lightPath, document.Light);
                written.Add(Path.GetFileName(lightPath));
            }

            return new ExportResult(true, [.. written], [.. backedUp], null);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, [.. written], [.. backedUp], ex.Message);
        }
    }

    /// <summary>
    /// 產生 OpenMU 的 <c>GameMapDefinition.TerrainData</c>：3 byte 標頭 + 1 byte/格，未加密。
    /// </summary>
    public static byte[] BuildServerTerrainData(MapDocument document)
        => new ATTWriter().ToServerTerrainData(BuildAttribute(document));

    private static TerrainAttribute BuildAttribute(MapDocument document)
    {
        var attribute = new TerrainAttribute
        {
            Version = document.AttVersion,
            Index = document.AttIndex,
            Width = 255,
            Height = 255,
        };

        int count = Math.Min(document.Attributes.Length, attribute.TerrainWall.Length);
        for (int i = 0; i < count; i++)
        {
            // ATTWriter 只寫低 8 位，而且拒絕 >= 0x80 的值
            // （ATTReader 本來就會這樣拒，高位屬性客戶端沒在用）。
            attribute.TerrainWall[i] = (TWFlags)((ushort)document.Attributes[i] & 0x7F);
        }

        return attribute;
    }

    private static void Backup(string path, List<string> backedUp)
    {
        if (!File.Exists(path))
            return;

        string backupPath = path + ".bak";

        // 已經有 .bak 就不覆蓋 —— 第一份備份才是原始檔。
        if (File.Exists(backupPath))
            return;

        File.Copy(path, backupPath);
        backedUp.Add(Path.GetFileName(backupPath));
    }
}
