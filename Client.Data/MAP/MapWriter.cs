namespace Client.Data.MAP
{
    /// <summary>
    /// <see cref="MapReader"/> 的寫入端。輸出舊版（FileCryptor / XOR）格式：
    /// 不帶 "MAP\1" 魔數，因此 <see cref="MapReader"/> 會走 <see cref="FileCryptor"/> 分支。
    /// ModulusCryptor 只有解密實作，無法輸出 Season 20 的新格式。
    /// </summary>
    public class MapWriter : BaseWriter<TerrainMapping>
    {
        protected override byte[] Write(TerrainMapping model)
        {
            const int size = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;

            var plain = new byte[2 + (size * 3)];
            plain[0] = model.Version;
            plain[1] = model.MapNumber;

            CopyLayer(model.Layer1, plain, 2, nameof(model.Layer1));
            CopyLayer(model.Layer2, plain, 2 + size, nameof(model.Layer2));
            CopyLayer(model.Alpha, plain, 2 + (size * 2), nameof(model.Alpha));

            return FileCryptor.Encrypt(plain);
        }

        private static void CopyLayer(byte[] layer, byte[] target, int offset, string name)
        {
            const int size = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;

            if (layer is null)
                throw new ArgumentNullException(name, $"TerrainMapping.{name} is null.");

            if (layer.Length != size)
                throw new ArgumentException($"TerrainMapping.{name} must be {size} bytes, was {layer.Length}.", name);

            Buffer.BlockCopy(layer, 0, target, offset, size);
        }
    }
}
