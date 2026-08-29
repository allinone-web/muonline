namespace Client.Data.OBJS
{
    /// <summary>
    /// <see cref="OBJReader"/> 的寫入端。<c>.obj</c> 一直都是舊版 <see cref="FileCryptor"/> 格式，
    /// 所以這裡是唯一與原始檔案能做 byte-exact 往返的地圖檔。
    /// </summary>
    public class OBJWriter : BaseWriter<OBJ>
    {
        protected override byte[] Write(OBJ model)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                bw.Write(model.Version);
                bw.Write((byte)model.MapNumber);
                bw.Write((short)model.Objects.Length);

                foreach (var obj in model.Objects)
                    WriteObject(bw, model.Version, obj);
            }

            return FileCryptor.Encrypt(ms.ToArray());
        }

        private static void WriteObject(BinaryWriter bw, byte version, IMapObject obj)
        {
            switch (version)
            {
                case 0: bw.WriteStruct(Coerce<MapObjectV0>(obj)); break;
                case 1: bw.WriteStruct(Coerce<MapObjectV1>(obj)); break;
                case 2: bw.WriteStruct(Coerce<MapObjectV2>(obj)); break;
                case 3: bw.WriteStruct(Coerce<MapObjectV3>(obj)); break;
                case 4: bw.WriteStruct(Coerce<MapObjectV4>(obj)); break;
                case 5: bw.WriteStruct(Coerce<MapObjectV5>(obj)); break;
                default: throw new NotImplementedException($"Version {version} not implemented");
            }
        }

        /// <summary>
        /// 物件若已經是目標版本就原樣輸出（保留該版本才有的 Unknown 欄位）；
        /// 否則只搬共通的四個欄位，版本專屬欄位留預設值。
        /// </summary>
        private static T Coerce<T>(IMapObject obj) where T : struct, IMapObject
        {
            if (obj is T exact)
                return exact;

            var result = default(T);
            object boxed = result;

            typeof(T).GetProperty(nameof(IMapObject.Type))!.SetValue(boxed, obj.Type);
            typeof(T).GetProperty(nameof(IMapObject.Position))!.SetValue(boxed, obj.Position);
            typeof(T).GetProperty(nameof(IMapObject.Angle))!.SetValue(boxed, obj.Angle);
            typeof(T).GetProperty(nameof(IMapObject.Scale))!.SetValue(boxed, obj.Scale);

            return (T)boxed;
        }
    }
}
