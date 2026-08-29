namespace Client.MapEditor;

using System.Text.Json;
using Microsoft.Xna.Framework;

/// <summary>
/// 黃金影像的一個鏡位。
/// </summary>
/// <remarks>
/// 這個型別的重點不是「相機參數」，是<b>兩個引擎讀同一份檔案</b>。
/// 各自寫死一組相機，比出來的差異分不清是「渲染不一樣」還是「根本沒看同一個地方」——
/// 那種比對會一直紅，然後被關掉。
///
/// 契約在 <c>tools/golden/shots.json</c>。Godot 那側的渲染器做好之後讀同一份。
/// </remarks>
public sealed record GoldenShot(
    string Name,
    int World,
    CameraMode Mode,
    Vector3 Focus,
    float Distance,
    float YawDegrees,
    float PitchDegrees,
    int Width,
    int Height)
{
    /// <summary>從契約檔讀一個鏡位；<paramref name="name"/> 給 null 就回全部。</summary>
    public static GoldenShot[] Load(string path, string? name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var shots = document.RootElement.GetProperty("shots").EnumerateArray()
            .Select(Parse)
            .Where(shot => name is null || shot.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (name is not null && shots.Length == 0)
            throw new ArgumentException($"契約檔裡沒有這個鏡位：{name}");

        return shots;
    }

    private static GoldenShot Parse(JsonElement element)
    {
        var focus = element.GetProperty("focus");
        var size = element.GetProperty("size");

        return new GoldenShot(
            Name: element.GetProperty("name").GetString()!,
            World: element.GetProperty("world").GetInt32(),
            Mode: element.GetProperty("mode").GetString()!.Equals("topdown", StringComparison.OrdinalIgnoreCase)
                ? CameraMode.TopDown
                : CameraMode.Orbit,
            Focus: new Vector3(focus[0].GetSingle(), focus[1].GetSingle(), focus[2].GetSingle()),
            Distance: element.GetProperty("distance").GetSingle(),
            YawDegrees: element.GetProperty("yaw").GetSingle(),
            PitchDegrees: element.GetProperty("pitch").GetSingle(),
            Width: size[0].GetInt32(),
            Height: size[1].GetInt32());
    }

    /// <summary>把鏡位套到相機上。每一幀都套 —— 見 <see cref="MapEditorScene"/> 的呼叫處。</summary>
    public void ApplyTo(EditorCamera camera)
    {
        camera.Mode = this.Mode;
        camera.Focus = this.Focus;
        camera.Distance = this.Distance;
        camera.Yaw = MathHelper.ToRadians(this.YawDegrees);
        camera.Pitch = MathHelper.ToRadians(this.PitchDegrees);
    }
}
