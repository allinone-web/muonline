using System.Runtime.InteropServices;

namespace Client.Data
{
    public static class BinaryWriterExtensions
    {
        public static void WriteStruct<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this BinaryWriter bw, T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buffer = new byte[size];
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            try
            {
                Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                handle.Free();
            }

            bw.Write(buffer);
        }

        public static void WriteStructArray<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this BinaryWriter bw, T[] values) where T : struct
        {
            for (int i = 0; i < values.Length; i++)
                bw.WriteStruct(values[i]);
        }
    }
}
