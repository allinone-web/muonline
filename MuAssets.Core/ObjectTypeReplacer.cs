namespace MuAssets.Core;

/// <summary>
/// 把地圖上某一種物件整批換成另一種 —— 「這張圖的樹全部換成那棵樹」。
/// </summary>
/// <remarks>
/// 換的是 <see cref="MapObjectInstance.Type"/>，也就是「這個位置擺的是哪個模型」，
/// 位置、角度、縮放全部原樣保留。這樣做的好處是**完全可逆**：
/// 換回去就是再換一次 type，原始的 <c>.bmd</c> 一個位元組都沒動。
///
/// （另一條路是覆蓋 <c>Object{N}/ObjectXX.bmd</c> 檔案本身。那會動到資源、
/// 影響所有引用它的地方，而且要備份才救得回來 —— 不是「換一棵樹」該付的代價。）
///
/// <b>尺寸不自動補償。</b> 兩個模型高矮差很多時，換完會明顯不對，
/// 但要放大多少是美術判斷，不是算得出來的。這裡只把量到的比例回報出去
/// （<see cref="Preview"/>），要不要套用由使用者決定 —— 猜一個係數然後靜默套用，
/// 出問題時沒人知道那個數字哪來的。
/// </remarks>
public static class ObjectTypeReplacer
{
    public sealed record Result(short FromType, short ToType, int Replaced, float ScaleMultiplier);

    /// <summary>換之前先看看：這張圖有幾個、兩個模型的尺寸差多少。</summary>
    public sealed record Preview(
        short FromType,
        short ToType,
        int Count,
        ModelShape? FromShape,
        ModelShape? ToShape)
    {
        /// <summary>把新模型調成和舊模型差不多高，需要乘的倍率。量不到時是 null。</summary>
        public float? SuggestedScale =>
            FromShape is { Height: > 0.01f } f && ToShape is { Height: > 0.01f } t
                ? f.Height / t.Height
                : null;
    }

    /// <summary>統計一張圖裡每種 type 各有幾個。</summary>
    public static Dictionary<short, int> CountByType(MapDocument document)
    {
        var counts = new Dictionary<short, int>();
        foreach (var o in document.Objects)
            counts[o.Type] = counts.GetValueOrDefault(o.Type) + 1;
        return counts;
    }

    /// <param name="dataPath">量模型尺寸用；傳 null 就只給數量。</param>
    public static Preview Inspect(
        MapDocument document,
        short fromType,
        short toType,
        string? dataPath = null)
    {
        int count = document.Objects.Count(o => o.Type == fromType);

        ModelShape? from = null, to = null;
        if (dataPath is not null)
        {
            from = MeasureType(dataPath, document.WorldIndex, fromType);
            to = MeasureType(dataPath, document.WorldIndex, toType);
        }

        return new Preview(fromType, toType, count, from, to);
    }

    /// <summary>
    /// 換型。回傳一筆可撤銷的批次編輯（呼叫端負責 <c>Push</c> 進歷史）；沒有東西可換時回 null。
    /// </summary>
    /// <param name="scaleMultiplier">
    /// 每個物件的 <see cref="MapObjectInstance.Scale"/> 要乘的倍率。1 = 不動。
    /// 想用建議值的話由呼叫端從 <see cref="Preview.SuggestedScale"/> 取，這裡不自己決定。
    /// </param>
    public static (ObjectEdit? Edit, Result Result) Replace(
        MapDocument document,
        short fromType,
        short toType,
        float scaleMultiplier = 1f)
    {
        if (fromType == toType && Math.Abs(scaleMultiplier - 1f) < 0.0001f)
            return (null, new Result(fromType, toType, 0, scaleMultiplier));

        var edits = new List<ObjectEdit>();

        foreach (var instance in document.Objects)
        {
            if (instance.Type != fromType)
                continue;

            // Transform 記的是「改動前的完整狀態」，所以要先複製再改。
            var before = instance.Clone();
            instance.Type = toType;

            if (Math.Abs(scaleMultiplier - 1f) > 0.0001f)
                instance.Scale *= scaleMultiplier;

            edits.Add(ObjectEdit.Transform(instance, before));
        }

        if (edits.Count == 0)
            return (null, new Result(fromType, toType, 0, scaleMultiplier));

        var batch = ObjectEdit.Batch(
            $"把 {edits.Count} 個 type {fromType} 換成 type {toType}",
            edits);

        return (batch, new Result(fromType, toType, edits.Count, scaleMultiplier));
    }

    private static ModelShape? MeasureType(string dataPath, int worldIndex, short type)
    {
        string directory = Path.Combine(dataPath, $"Object{worldIndex}");
        string path = Path.Combine(directory, $"Object{type + 1:00}.bmd");
        return File.Exists(path) ? ModelShapeClassifier.Measure(path, directory) : null;
    }
}
