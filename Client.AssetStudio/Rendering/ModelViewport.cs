using Client.MapEditor;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.AssetStudio.Rendering;

/// <summary>
/// 把目前選中的模型畫進一張 render target，讓 ImGui 當成一般圖片顯示。
/// </summary>
/// <remarks>
/// 走 render target 而不是「畫在整個視窗底下、面板停在四周」（地圖編輯器的作法）：
/// 檢視器的 3D 內容是<b>面板裡的一個元素</b>，要能停靠、縮放、和其他面板並排。
/// 代價是多一次全畫面複製，對一個只畫一隻怪的工具完全無所謂。
///
/// <see cref="ImGuiRenderer.BindTexture"/> 每呼叫一次就配一個新 ID，所以只在
/// render target 真的重建時才重新綁定 —— 每幀綁一次會讓 ID 表無限成長。
/// </remarks>
public sealed class ModelViewport : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ImGuiRenderer _imgui;
    private readonly BasicEffect _effect;
    private readonly BasicEffect _lineEffect;

    private RenderTarget2D? _target;
    private IntPtr? _textureId;
    private int _width;
    private int _height;

    public ViewportCamera Camera { get; } = new();

    /// <summary>最後一次 <see cref="Render"/> 畫進去的 render target。</summary>
    /// <remarks>
    /// 給 <c>--render</c> 用：把模型直接存成 PNG，不必依賴視窗版面。
    /// 自動化截圖抓的是背景緩衝，而視埠在那個模式下只有 314×134，
    /// 模型小到看不見 —— 那個尺寸來自 ImGui 的停靠版面，
    /// 而版面在無人操作的執行裡本來就不會是好的。
    /// </remarks>
    public RenderTarget2D? Target => _target;

    public bool ShowGrid { get; set; } = true;

    public bool ShowSkeleton { get; set; }

    public bool ShowTextures { get; set; } = true;

    public bool Wireframe { get; set; }

    public Color Background { get; set; } = new(28, 30, 36);

    public ModelViewport(GraphicsDevice device, ImGuiRenderer imgui)
    {
        _device = device;
        _imgui = imgui;

        _effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = false,
            LightingEnabled = true,
            PreferPerPixelLighting = true,
        };
        _effect.EnableDefaultLighting();

        // 預設光源是給 Y 軸向上的場景調的，MU 是 Z 軸向上：不轉的話模型頂面全黑、底面全亮。
        _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.6f, -0.7f, -0.8f));
        _effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.7f, 0.5f, -0.3f));
        _effect.DirectionalLight2.Direction = Vector3.Normalize(new Vector3(0.2f, 0.8f, 0.5f));
        _effect.AmbientLightColor = new Vector3(0.42f);

        _lineEffect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
    }

    /// <summary>畫一幀。回傳可以丟給 <c>ImGui.Image</c> 的貼圖 ID。</summary>
    public IntPtr? Render(AnimatedModel? model, int width, int height)
    {
        width = Math.Clamp(width, 64, 4096);
        height = Math.Clamp(height, 64, 4096);

        EnsureTarget(width, height);

        if (_target is null)
            return null;

        var previousTargets = _device.GetRenderTargets();
        var previousViewport = _device.Viewport;

        try
        {
            _device.SetRenderTarget(_target);
            _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Background, 1f, 0);

            var view = Camera.View;
            var projection = Camera.Projection((float)width / height);

            if (ShowGrid)
                DrawGrid(view, projection);

            if (model is not null)
            {
                _effect.World = Matrix.Identity;
                _effect.View = view;
                _effect.Projection = projection;

                model.Draw(_effect, new AnimatedModel.RenderOptions(ShowTextures, Wireframe));

                if (ShowSkeleton)
                    DrawSkeleton(model, view, projection);
            }
        }
        finally
        {
            _device.SetRenderTargets(previousTargets);
            _device.Viewport = previousViewport;
        }

        return _textureId;
    }

    private void DrawSkeleton(AnimatedModel model, Matrix view, Matrix projection)
    {
        var lines = model.BuildSkeletonLines();
        if (lines.Length < 2)
            return;

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = view;
        _lineEffect.Projection = projection;

        // 骨架要看得穿過模型，否則只有貼在表面的那幾根看得到。
        _device.DepthStencilState = DepthStencilState.None;
        _device.BlendState = BlendState.NonPremultiplied;
        _device.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, lines, 0, lines.Length / 2);
        }
    }

    /// <summary>地面格線，一格 = 一個 MU 地形格（100 世界單位），用來估模型的實際大小。</summary>
    private void DrawGrid(Matrix view, Matrix projection)
    {
        const float step = 100f;
        int half = 6;
        float extent = half * step;

        var lines = new List<VertexPositionColor>((half * 2 + 1) * 4);
        var minor = new Color(70, 74, 84);
        var major = new Color(110, 116, 130);

        for (int i = -half; i <= half; i++)
        {
            var color = i == 0 ? major : minor;
            float offset = i * step;

            lines.Add(new VertexPositionColor(new Vector3(-extent, offset, 0f), color));
            lines.Add(new VertexPositionColor(new Vector3(extent, offset, 0f), color));
            lines.Add(new VertexPositionColor(new Vector3(offset, -extent, 0f), color));
            lines.Add(new VertexPositionColor(new Vector3(offset, extent, 0f), color));
        }

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = view;
        _lineEffect.Projection = projection;

        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = RasterizerState.CullNone;

        var array = lines.ToArray();

        foreach (var pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, array, 0, array.Length / 2);
        }
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target is not null && _width == width && _height == height)
            return;

        if (_textureId is IntPtr old)
            _imgui.UnbindTexture(old);

        _target?.Dispose();

        _target = new RenderTarget2D(
            _device, width, height,
            mipMap: false,
            SurfaceFormat.Color,
            DepthFormat.Depth24,
            preferredMultiSampleCount: 0,
            RenderTargetUsage.DiscardContents);

        _textureId = _imgui.BindTexture(_target);
        _width = width;
        _height = height;
    }

    public void Dispose()
    {
        if (_textureId is IntPtr id)
            _imgui.UnbindTexture(id);

        _target?.Dispose();
        _effect.Dispose();
        _lineEffect.Dispose();
    }
}
