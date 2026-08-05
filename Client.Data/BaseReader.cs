namespace Client.Data
{
    public abstract class BaseReader<T>
    {
        public async Task<T> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);

            var buffer = await File.ReadAllBytesAsync(path);

            return Read(buffer);
        }

        public async Task<T> Load(Stream stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return Read(buffer.ToArray());
        }

        protected abstract T Read(byte[] buffer);
    }
}
