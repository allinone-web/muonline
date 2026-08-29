using System.Drawing;

namespace Client.Data.OZB
{
    /// <summary>
    /// <see cref="OZBReader"/> 的寫入端。OZB 沒有加密，是「BMx」前綴 + 標準 BMP 標頭 + 像素資料。
    /// </summary>
    /// <remarks>
    /// 若 <see cref="OZB.RawHeader"/> 有值就原樣回寫，未改動的檔案可做到 byte-exact 往返；
    /// 新建地圖時 RawHeader 為 null，由這裡合成標頭。
    /// </remarks>
    public class OZBWriter : BaseWriter<OZB>
    {
        protected override byte[] Write(OZB model)
        {
            return model.FileType switch
            {
                OZBFileType.BM6 => WriteBM6(model),
                OZBFileType.BM8 => WriteBM8(model),
                _ => throw new NotSupportedException($"Unsupported OZB file type '{model.FileType}'. Expected BM6 or BM8."),
            };
        }

        private static byte[] WriteBM8(OZB model)
        {
            int pixels = model.Width * model.Height;
            EnsureDataLength(model, pixels);

            var header = model.RawHeader is { Length: OZBFileType.BM8HeaderLength }
                ? model.RawHeader
                : BuildBM8Header(model);

            var buffer = new byte[header.Length + pixels];
            header.CopyTo(buffer, 0);

            // BM8 的高度值在讀取時被放進 R 通道（OZBReader.ReadBM8）。
            for (int i = 0; i < pixels; i++)
                buffer[header.Length + i] = model.Data[i].R;

            return buffer;
        }

        private static byte[] WriteBM6(OZB model)
        {
            int pixels = model.Width * model.Height;
            EnsureDataLength(model, pixels);

            var header = model.RawHeader is { Length: OZBFileType.BM6HeaderLength }
                ? model.RawHeader
                : BuildBM6Header(model);

            var buffer = new byte[header.Length + (pixels * 3)];
            header.CopyTo(buffer, 0);

            int offset = header.Length;
            for (int i = 0; i < pixels; i++)
            {
                var color = model.Data[i];
                buffer[offset++] = color.B;
                buffer[offset++] = color.G;
                buffer[offset++] = color.R;
            }

            return buffer;
        }

        private static void EnsureDataLength(OZB model, int pixels)
        {
            if (model.Data.Length != pixels)
                throw new ArgumentException($"OZB.Data must hold {pixels} entries for {model.Width}x{model.Height}, was {model.Data.Length}.", nameof(model));
        }

        private static byte[] BuildBM8Header(OZB model)
        {
            // 3 + 1 前綴、14 檔案標頭、40 資訊標頭、1026 調色盤區（1024 色表 + 2 byte 填充，
            // 對齊 OZBReader.ReadBM8 讀取的固定長度）。
            var buffer = new byte[OZBFileType.BM8HeaderLength];
            using var ms = new MemoryStream(buffer);
            using var bw = new BinaryWriter(ms);

            int pixels = model.Width * model.Height;

            WritePrefixAndHeaders(bw, model, OZBFileType.BM8, bitCount: 8, offBits: 54 + 1024, imageSize: pixels);

            // 灰階調色盤：BGRA，256 項。
            for (int i = 0; i < 256; i++)
            {
                bw.Write((byte)i);
                bw.Write((byte)i);
                bw.Write((byte)i);
                bw.Write((byte)0);
            }

            return buffer;
        }

        private static byte[] BuildBM6Header(OZB model)
        {
            var buffer = new byte[OZBFileType.BM6HeaderLength];
            using var ms = new MemoryStream(buffer);
            using var bw = new BinaryWriter(ms);

            WritePrefixAndHeaders(bw, model, OZBFileType.BM6, bitCount: 24, offBits: 54, imageSize: model.Width * model.Height * 3);

            return buffer;
        }

        private static void WritePrefixAndHeaders(BinaryWriter bw, OZB model, string fileType, short bitCount, int offBits, int imageSize)
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes(fileType));
            bw.Write(model.Version);

            // file header (14)
            bw.Write((short)0x4D42); // 'BM'
            bw.Write(offBits + imageSize);
            bw.Write((short)0);
            bw.Write((short)0);
            bw.Write(offBits);

            // info header (40)
            bw.Write(40);
            bw.Write(model.Width);
            bw.Write(model.Height);
            bw.Write((short)1);
            bw.Write(bitCount);
            bw.Write(0);
            bw.Write(imageSize);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
        }
    }
}
