namespace Client.Data.ATT
{
    /// <summary>
    /// <see cref="ATTReader"/> 的寫入端。輸出舊版（1 byte/tile）格式：
    /// 先鋪上 4 byte 標頭與屬性，再套 MASK，最後 <see cref="FileCryptor"/> 加密 —— 正好是 Reader 的反序。
    /// </summary>
    /// <remarks>
    /// 只寫低 8 位。<see cref="ATTReader"/> 讀 extended（2 byte/tile）檔時也會做 <c>b &amp;= 0xFF</c>
    /// 並在 <c>b &gt;= 0x80</c> 時丟例外，所以客戶端本來就用不到高位屬性。
    /// </remarks>
    public class ATTWriter : BaseWriter<TerrainAttribute>
    {
        private static readonly byte[] MASK = { 0xFC, 0xCF, 0xAB };

        protected override byte[] Write(TerrainAttribute model)
        {
            const int size = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;

            if (model.TerrainWall.Length != size)
                throw new ArgumentException($"TerrainWall must be {size} entries, was {model.TerrainWall.Length}.", nameof(model));

            var buffer = new byte[size + 4];
            buffer[0] = model.Version;
            buffer[1] = model.Index;
            buffer[2] = 255;
            buffer[3] = 255;

            for (int i = 0; i < size; i++)
            {
                var value = (ushort)model.TerrainWall[i] & 0xFF;

                if (value >= 0x80)
                    throw new ArgumentException($"TerrainWall[{i}] low byte is 0x{value:X2}; the reader rejects values >= 0x80.", nameof(model));

                buffer[i + 4] = (byte)value;
            }

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] ^= MASK[i % MASK.Length];

            return FileCryptor.Encrypt(buffer);
        }

        /// <summary>
        /// 產生 OpenMU <c>GameMapDefinition.TerrainData</c> 用的未加密佈局：
        /// 3 byte 標頭（version、width、height）+ 1 byte/tile，索引順序與客戶端相同。
        /// 對應 <c>MUnique.OpenMU.GameLogic.GameMapTerrain.ReadTerrainData</c>（讀 <c>AsSpan(3)</c>）。
        /// </summary>
        public byte[] ToServerTerrainData(TerrainAttribute model)
        {
            const int size = Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE;

            var buffer = new byte[size + 3];
            buffer[0] = model.Version;
            buffer[1] = 255;
            buffer[2] = 255;

            for (int i = 0; i < size; i++)
                buffer[i + 3] = (byte)((ushort)model.TerrainWall[i] & 0xFF);

            return buffer;
        }
    }
}
