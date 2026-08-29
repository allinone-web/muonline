using System.Drawing;

namespace Client.Data.OZB
{
    public class OZB
    {
        public byte Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Color[] Data { get; set; } = [];

        /// <summary>
        /// 檔案類型："BM8"（高度圖，8-bit 灰階）或 "BM6"（光照圖，24-bit）。
        /// </summary>
        public string FileType { get; set; } = OZBFileType.BM8;

        /// <summary>
        /// 像素資料之前的所有位元組（含 "BMx" 前綴、BMP 標頭、BM8 的調色盤）。
        /// <see cref="OZBReader"/> 原樣保留、<see cref="OZBWriter"/> 直接回寫，
        /// 讓往返能做到 byte-exact；為 null 時由 Writer 自行合成標頭。
        /// </summary>
        public byte[]? RawHeader { get; set; }
    }

    public static class OZBFileType
    {
        public const string BM6 = "BM6";
        public const string BM8 = "BM8";

        /// <summary>BM8 在像素資料前的標頭總長：3 + 1 + 14 + 40 + 1026。</summary>
        public const int BM8HeaderLength = 1084;

        /// <summary>BM6 在像素資料前的標頭總長：3 + 1 + 14 + 40。</summary>
        public const int BM6HeaderLength = 58;
    }
}
