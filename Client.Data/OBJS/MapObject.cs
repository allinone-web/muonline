using System.Numerics;
using System.Runtime.InteropServices;

namespace Client.Data.OBJS
{
    public interface IMapObject
    {
        short Type { get; }
        Vector3 Position { get; }
        Vector3 Angle { get; }
        float Scale { get; }
    }

    // 這些結構透過 BinaryReaderExtensions.ReadStruct<T>（Marshal.PtrToStructure）
    // 直接對映 .obj 檔的位元組，因此 Pack = 1 與欄位順序都不可更動。
    //
    // 欄位刻意用個別 float 而非 Vector3：Pack = 1 之下 short Type 只佔 2 bytes，
    // 後面的 Vector3 會落在 offset 2。Mono 的 ARM64 AOT 後端為這種未對齊的
    // 128-bit 向量載入產生的 loadx_membase 需要 28 bytes，超過指令描述表宣告的
    // 上限 20，直接觸發 mini-arm64.c:6036 的斷言，讓 AOT 編譯器以 SIGABRT 中止
    //   wrong maximal instruction length of instruction loadx_membase (expected 20, got 28)
    // 這會擋掉所有 iOS 建置（arm64 模擬器與真機都強制 AOT）。
    // 拆成三個 float 後位元組佈局完全相同，但不再產生未對齊的向量載入。

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV0 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV1 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;
        private byte _unknownX;
        private byte _unknownY;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }

        public byte UnknownX { readonly get => _unknownX; set => _unknownX = value; }
        public byte UnknownY { readonly get => _unknownY; set => _unknownY = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV2 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;
        private byte _unknownX;
        private byte _unknownY;
        private byte _unknownZ;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }

        public byte UnknownX { readonly get => _unknownX; set => _unknownX = value; }
        public byte UnknownY { readonly get => _unknownY; set => _unknownY = value; }
        public byte UnknownZ { readonly get => _unknownZ; set => _unknownZ = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV3 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;
        private byte _unknownX;
        private byte _unknownY;
        private byte _unknownZ;
        private float _ligthningX, _ligthningY, _ligthningZ;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }

        public byte UnknownX { readonly get => _unknownX; set => _unknownX = value; }
        public byte UnknownY { readonly get => _unknownY; set => _unknownY = value; }
        public byte UnknownZ { readonly get => _unknownZ; set => _unknownZ = value; }

        public Vector3 Ligthning
        {
            readonly get => new(_ligthningX, _ligthningY, _ligthningZ);
            set { _ligthningX = value.X; _ligthningY = value.Y; _ligthningZ = value.Z; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV4 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;
        private byte _unknownX;
        private byte _unknownY;
        private byte _unknownZ;
        private float _ligthningX, _ligthningY, _ligthningZ;
        private byte _unknownByte;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }

        public byte UnknownX { readonly get => _unknownX; set => _unknownX = value; }
        public byte UnknownY { readonly get => _unknownY; set => _unknownY = value; }
        public byte UnknownZ { readonly get => _unknownZ; set => _unknownZ = value; }

        public Vector3 Ligthning
        {
            readonly get => new(_ligthningX, _ligthningY, _ligthningZ);
            set { _ligthningX = value.X; _ligthningY = value.Y; _ligthningZ = value.Z; }
        }
        public byte UnknownByte { readonly get => _unknownByte; set => _unknownByte = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapObjectV5 : IMapObject
    {
        private short _type;
        private float _positionX, _positionY, _positionZ;
        private float _angleX, _angleY, _angleZ;
        private float _scale;
        private byte _unknownX;
        private byte _unknownY;
        private byte _unknownZ;
        private float _ligthningX, _ligthningY, _ligthningZ;
        private byte _unknownByte;
        private float _unknownFloat1;
        private float _unknownFloat2;

        public short Type { readonly get => _type; set => _type = value; }
        public Vector3 Position
        {
            readonly get => new(_positionX, _positionY, _positionZ);
            set { _positionX = value.X; _positionY = value.Y; _positionZ = value.Z; }
        }
        public Vector3 Angle
        {
            readonly get => new(_angleX, _angleY, _angleZ);
            set { _angleX = value.X; _angleY = value.Y; _angleZ = value.Z; }
        }
        public float Scale { readonly get => _scale; set => _scale = value; }

        public byte UnknownX { readonly get => _unknownX; set => _unknownX = value; }
        public byte UnknownY { readonly get => _unknownY; set => _unknownY = value; }
        public byte UnknownZ { readonly get => _unknownZ; set => _unknownZ = value; }

        public Vector3 Ligthning
        {
            readonly get => new(_ligthningX, _ligthningY, _ligthningZ);
            set { _ligthningX = value.X; _ligthningY = value.Y; _ligthningZ = value.Z; }
        }
        public byte UnknownByte { readonly get => _unknownByte; set => _unknownByte = value; }
        public float UnknownFloat1 { readonly get => _unknownFloat1; set => _unknownFloat1 = value; }
        public float UnknownFloat2 { readonly get => _unknownFloat2; set => _unknownFloat2 = value; }
    }
}
