namespace Client.Data
{
    /// <summary>
    /// <see cref="BaseReader{T}"/> 的對稱寫入端。
    /// 每個 Writer 的位元組佈局都必須逐欄對應到同名 Reader，
    /// 佈局有疑問時以 Reader 為準。
    /// </summary>
    public abstract class BaseWriter<T>
    {
        public async Task Save(string path, T model)
        {
            var buffer = Write(model);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllBytesAsync(path, buffer);
        }

        public async Task Save(Stream stream, T model)
        {
            var buffer = Write(model);
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }

        public byte[] ToBytes(T model) => Write(model);

        protected abstract byte[] Write(T model);
    }
}
