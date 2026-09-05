using Microsoft.Xna.Framework;

namespace Client.AssetStudio.Rendering;

/// <summary>
/// 檢視器的環繞相機。永遠繞著模型的中心轉，距離由包圍盒決定。
/// </summary>
/// <remarks>
/// MU 的世界是 Z 軸向上（見 <c>AGENTS.md</c> 的 Game Client Facts），
/// 模型的座標也是，所以 up 向量是 <c>UnitZ</c> 而不是 <c>UnitY</c>。
/// 用錯的話所有模型都會躺著，而且因為看起來「只是角度怪」很容易被當成資料問題。
/// </remarks>
public sealed class ViewportCamera
{
    /// <summary>
    /// 預設要讓角色<b>面向鏡頭</b>。
    /// </summary>
    /// <remarks>
    /// 實測：MU 的角色朝 −Y，所以相機要繞到 <b>280°</b> 才看得到正面
    /// （35° 是背面，這也是 <c>--render-yaw</c> 以前預設 180 的原因 ——
    /// 而 180 其實只轉到側背，正面在 235 之後）。
    /// 留 10° 不對正是為了有點立體感；完全對正（270°）會扁得像立繪。
    ///
    /// 驗證方式：<c>tools/mu browser --open 幻影騎士 --render x.png</c>，看得到臉就是對的。
    /// </remarks>
    private float _yaw = MathHelper.ToRadians(280f);
    private float _pitch = MathHelper.ToRadians(20f);

    public Vector3 Target { get; set; }

    public float Distance { get; set; } = 300f;

    /// <summary>包圍盒半徑，用來換算縮放與近遠平面。</summary>
    public float Radius { get; private set; } = 100f;

    public float Yaw
    {
        get => _yaw;
        set => _yaw = WrapAngle(value);
    }

    public float Pitch
    {
        get => _pitch;
        // 貼到極點時 up 向量會與視線共線，矩陣退化，畫面整個翻掉。
        set => _pitch = MathHelper.Clamp(value, MathHelper.ToRadians(-88f), MathHelper.ToRadians(88f));
    }

    public Vector3 Position
    {
        get
        {
            float horizontal = MathF.Cos(_pitch) * Distance;

            return Target + new Vector3(
                horizontal * MathF.Cos(_yaw),
                horizontal * MathF.Sin(_yaw),
                MathF.Sin(_pitch) * Distance);
        }
    }

    public Matrix View => Matrix.CreateLookAt(Position, Target, Vector3.UnitZ);

    public Matrix Projection(float aspect) => Matrix.CreatePerspectiveFieldOfView(
        MathHelper.ToRadians(FieldOfViewDegrees),
        MathF.Max(aspect, 0.01f),
        MathF.Max(Distance - (Radius * 4f), Radius * 0.01f),
        Distance + (Radius * 8f));

    /// <summary>視角。垂直的，水平由長寬比推導。</summary>
    private const float FieldOfViewDegrees = 40f;

    /// <summary>把相機擺到「整個模型剛好入鏡」的位置。</summary>
    /// <remarks>
    /// 距離由視角推導而不是拍腦袋乘一個倍數：包住模型的球半徑是 r 時，
    /// 要讓它整顆入鏡需要 <c>r / sin(視角/2)</c>，40 度視角就是 2.92r。
    /// 拿 2.8r 這種「差不多」的數字會讓比較高的模型上下各切掉一點點 ——
    /// 看起來像模型有問題，其實是相機太近。留 10% 餘裕。
    /// </remarks>
    public void Frame(BoundingBox bounds)
    {
        Target = (bounds.Min + bounds.Max) * 0.5f;
        Radius = MathF.Max((bounds.Max - bounds.Min).Length() * 0.5f, 1f);
        Distance = Radius / MathF.Sin(MathHelper.ToRadians(FieldOfViewDegrees) * 0.5f) * 1.1f;
    }

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw = _yaw + deltaYaw;
        Pitch = _pitch + deltaPitch;
    }

    /// <summary>滾輪縮放。用比例而不是固定步長，遠近的手感才一致。</summary>
    public void Zoom(float steps)
    {
        Distance = MathHelper.Clamp(
            Distance * MathF.Pow(0.88f, steps),
            Radius * 0.2f,
            Radius * 40f);
    }

    /// <summary>在螢幕平面上平移視點。</summary>
    public void Pan(float dx, float dy)
    {
        var forward = Vector3.Normalize(Target - Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);

        float scale = Distance * 0.0016f;
        Target += (right * -dx * scale) + (up * dy * scale);
    }

    private static float WrapAngle(float radians)
    {
        while (radians > MathF.PI)
            radians -= MathF.Tau;
        while (radians < -MathF.PI)
            radians += MathF.Tau;

        return radians;
    }
}
