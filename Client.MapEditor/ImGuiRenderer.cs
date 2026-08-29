using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// ImGui 的 MonoGame 後端：字型圖集、每幀的頂點/索引上傳、剪裁矩形，以及鍵鼠輸入。
/// </summary>
/// <remarks>
/// 刻意用內建的 <see cref="BasicEffect"/> 而不是自訂 shader —— macOS 上編不了 MGFX
/// （MGCB 的 shader 編譯需要 Wine，見 HANDOFF.md），而 BasicEffect 是 MonoGame 預先編好的。
/// </remarks>
public sealed class ImGuiRenderer : IDisposable
{
    private readonly Game _game;
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly RasterizerState _rasterizer;

    private readonly Dictionary<IntPtr, Texture2D> _boundTextures = new();
    private int _nextTextureId = 1;
    private IntPtr? _fontTextureId;

    private byte[] _vertexData = [];
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;

    private byte[] _indexData = [];
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;

    /// <summary>實際載入的 CJK 字型路徑；沿用 ImGui 內建字型時為 null。</summary>
    public string? LoadedFontPath { get; }

    private int _scrollWheelValue;
    private readonly List<int> _pressedKeys = [];

    public ImGuiRenderer(Game game, float fontSize = 17f)
    {
        _game = game;
        _device = game.GraphicsDevice;

        ImGui.SetCurrentContext(ImGui.CreateContext());

        var io = ImGui.GetIO();
        // 不開 NavEnableKeyboard：編輯器不靠鍵盤導覽，
        // 而它會讓清單項目在沒有明確點擊的情況下被啟動。
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        ImGui.StyleColorsDark();

        // 字型必須在建字型圖集之前掛上去。
        LoadedFontPath = EditorFonts.LoadCjkFont(fontSize);
        if (LoadedFontPath is null)
            Console.WriteLine("[ImGui] 找不到含 CJK 的系統字型，介面中文會顯示為 '?'。");

        _effect = new BasicEffect(_device)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };

        _rasterizer = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0,
        };

        RebuildFontAtlas();
    }

    /// <summary>把一張 MonoGame 貼圖登記給 ImGui，回傳可以丟給 <c>ImGui.Image</c> 的 id。</summary>
    public IntPtr BindTexture(Texture2D texture)
    {
        var id = new IntPtr(_nextTextureId++);
        _boundTextures.Add(id, texture);
        return id;
    }

    public void UnbindTexture(IntPtr id) => _boundTextures.Remove(id);

    public void BeginLayout(GameTime gameTime)
    {
        ImGui.GetIO().DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateInput();
        ImGui.NewFrame();
    }

    public void EndLayout()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);

        var pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);

        var texture = new Texture2D(_device, width, height, mipmap: false, SurfaceFormat.Color);
        texture.SetData(pixels);

        if (_fontTextureId.HasValue)
            UnbindTexture(_fontTextureId.Value);

        _fontTextureId = BindTexture(texture);
        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    private void UpdateInput()
    {
        if (!_game.IsActive)
            return;

        var io = ImGui.GetIO();
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        foreach (int key in _pressedKeys)
            io.AddKeyEvent(TranslateKey((Keys)key), false);

        _pressedKeys.Clear();

        foreach (var key in keyboard.GetPressedKeys())
        {
            var mapped = TranslateKey(key);
            if (mapped == ImGuiKey.None)
                continue;

            io.AddKeyEvent(mapped, true);
            _pressedKeys.Add((int)key);
        }

        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));

        var viewport = _device.PresentationParameters;
        io.DisplaySize = new System.Numerics.Vector2(viewport.BackBufferWidth, viewport.BackBufferHeight);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);

        // 滑鼠座標是「視窗座標」，但 ImGui 的版面是在「繪圖緩衝區座標」裡算的。
        // 全螢幕時兩者不一致（實測視窗 1470x956、緩衝區 1470x923），
        // 畫面被拉伸後點擊就會偏移 —— 越往下偏越多，最後差到整整一列。
        var bounds = _game.Window.ClientBounds;
        float mouseScaleX = bounds.Width > 0 ? viewport.BackBufferWidth / (float)bounds.Width : 1f;
        float mouseScaleY = bounds.Height > 0 ? viewport.BackBufferHeight / (float)bounds.Height : 1f;

        io.AddMousePosEvent(mouse.X * mouseScaleX, mouse.Y * mouseScaleY);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);

        int wheelDelta = mouse.ScrollWheelValue - _scrollWheelValue;
        _scrollWheelValue = mouse.ScrollWheelValue;
        if (wheelDelta != 0)
            io.AddMouseWheelEvent(0f, wheelDelta / 120f);
    }

    private void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0)
            return;

        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_device, ImGuiVertex.Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * ImGuiVertex.Size];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_device, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        int vertexOffset = 0;
        int indexOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            Marshal.Copy(cmdList.VtxBuffer.Data, _vertexData, vertexOffset * ImGuiVertex.Size, cmdList.VtxBuffer.Size * ImGuiVertex.Size);
            Marshal.Copy(cmdList.IdxBuffer.Data, _indexData, indexOffset * sizeof(ushort), cmdList.IdxBuffer.Size * sizeof(ushort));

            vertexOffset += cmdList.VtxBuffer.Size;
            indexOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * ImGuiVertex.Size);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0)
            return;

        var previousViewport = _device.Viewport;
        var previousScissor = _device.ScissorRectangle;
        var previousRasterizer = _device.RasterizerState;
        var previousBlend = _device.BlendState;
        var previousDepth = _device.DepthStencilState;
        var previousSampler = _device.SamplerStates[0];

        _device.BlendFactor = Color.White;
        _device.BlendState = BlendState.NonPremultiplied;
        _device.RasterizerState = _rasterizer;
        _device.DepthStencilState = DepthStencilState.None;
        _device.SamplerStates[0] = SamplerState.LinearClamp;

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        _device.Viewport = new Viewport(0, 0, _device.PresentationParameters.BackBufferWidth, _device.PresentationParameters.BackBufferHeight);

        UpdateBuffers(drawData);

        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0f,
            _device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight,
            0f,
            -1f,
            1f);

        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;

        int vertexOffset = 0;
        int indexOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[i];

                if (!_boundTextures.TryGetValue(cmd.TextureId, out var texture))
                    throw new InvalidOperationException($"ImGui 要求了未登記的貼圖 id {cmd.TextureId}。請先呼叫 BindTexture。");

                _device.ScissorRectangle = new Rectangle(
                    (int)cmd.ClipRect.X,
                    (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                    (int)(cmd.ClipRect.W - cmd.ClipRect.Y));

                _effect.Texture = texture;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: (int)(vertexOffset + cmd.VtxOffset),
                        startIndex: (int)(indexOffset + cmd.IdxOffset),
                        primitiveCount: (int)cmd.ElemCount / 3);
                }
            }

            vertexOffset += cmdList.VtxBuffer.Size;
            indexOffset += cmdList.IdxBuffer.Size;
        }

        _device.Viewport = previousViewport;
        _device.ScissorRectangle = previousScissor;
        _device.RasterizerState = previousRasterizer;
        _device.BlendState = previousBlend;
        _device.DepthStencilState = previousDepth;
        _device.SamplerStates[0] = previousSampler;
    }

    private static ImGuiKey TranslateKey(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
        >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
        >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
        >= Keys.F1 and <= Keys.F12 => ImGuiKey.F1 + (key - Keys.F1),
        Keys.Back => ImGuiKey.Backspace,
        Keys.Delete => ImGuiKey.Delete,
        Keys.Down => ImGuiKey.DownArrow,
        Keys.End => ImGuiKey.End,
        Keys.Enter => ImGuiKey.Enter,
        Keys.Escape => ImGuiKey.Escape,
        Keys.Home => ImGuiKey.Home,
        Keys.Insert => ImGuiKey.Insert,
        Keys.Left => ImGuiKey.LeftArrow,
        Keys.PageDown => ImGuiKey.PageDown,
        Keys.PageUp => ImGuiKey.PageUp,
        Keys.Right => ImGuiKey.RightArrow,
        Keys.Space => ImGuiKey.Space,
        Keys.Tab => ImGuiKey.Tab,
        Keys.Up => ImGuiKey.UpArrow,
        _ => ImGuiKey.None,
    };

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect.Dispose();
        _rasterizer.Dispose();

        foreach (var texture in _boundTextures.Values)
            texture.Dispose();

        _boundTextures.Clear();
    }
}

/// <summary>ImGui 的頂點佈局：Vector2 pos、Vector2 uv、uint col。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ImGuiVertex
{
    public Vector2 Position;
    public Vector2 TexCoord;
    public uint Color;

    public static readonly int Size = Marshal.SizeOf<ImGuiVertex>();

    public static readonly VertexDeclaration Declaration = new(
        Size,
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));
}
