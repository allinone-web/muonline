namespace Client.Main.Controls
{
    /// <summary>
    /// Exposes frame-specific rendering metrics for debugging or performance monitoring.
    /// </summary>
    public sealed class TerrainFrameMetrics
    {
        public int DrawCalls { get; internal set; }
        public int DrawnTriangles { get; internal set; }
        public int DrawnBlocks { get; internal set; }
        public int DrawnCells { get; internal set; }
        public int GrassFlushes { get; internal set; }
        public int IndexedCells { get; internal set; }
        public int StreamedCells { get; internal set; }
        public int IndexUploads { get; internal set; }
        public int VertexUploads { get; internal set; }
        public int UploadedIndices { get; internal set; }
        public int UploadedVertices { get; internal set; }
        public bool UsedIndexBatching { get; internal set; }

        public void Reset()
        {
            DrawCalls = 0;
            DrawnTriangles = 0;
            DrawnBlocks = 0;
            DrawnCells = 0;
            GrassFlushes = 0;
            IndexedCells = 0;
            StreamedCells = 0;
            IndexUploads = 0;
            VertexUploads = 0;
            UploadedIndices = 0;
            UploadedVertices = 0;
            UsedIndexBatching = false;
        }
    }
}
