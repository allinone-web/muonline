namespace Client.Data
{
    public static class FileCryptor
    {
        private static byte[] MAP_XOR_KEY = new byte[16]
        {
            0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27,
            0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2
        };

        public static byte[] Decrypt(byte[] src)
        {
            var dst = new byte[src.Length];
            ushort mapKey = 0x5E;
            for (int i = 0; i < src.Length; ++i)
            {
                dst[i] = (byte)((src[i] ^ MAP_XOR_KEY[i % 16]) - mapKey);
                mapKey = (ushort)(src[i] + 0x3D & 0xFF);
            }
            return dst;
        }

        /// <summary>
        /// 反向操作 <see cref="Decrypt"/>。滾動金鑰是由**密文**推進的，
        /// 所以加密時必須先算出這個位元組的密文，再用它更新金鑰。
        /// </summary>
        public static byte[] Encrypt(byte[] src)
        {
            var dst = new byte[src.Length];
            ushort mapKey = 0x5E;
            for (int i = 0; i < src.Length; ++i)
            {
                byte cipher = (byte)((byte)(src[i] + mapKey) ^ MAP_XOR_KEY[i % 16]);
                dst[i] = cipher;
                mapKey = (ushort)(cipher + 0x3D & 0xFF);
            }
            return dst;
        }
    }
}
