using Client.Data.Texture;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 把 MU 的貼圖檔解成 <see cref="Texture2D"/>。
/// </summary>
/// <remarks>
/// 每個 reader 吐出來的通道順序不一樣，這裡集中處理：
/// <c>.ozj</c> 是 RGB、<c>.ozt</c> 是 BGRA、<c>.ozp</c> 是 RGBA、<c>.ozd</c> 是壓縮的 DXT。
///
/// 不走 <c>Client.Main.Content.TextureLoader</c>：那一套是為遊戲的資產路徑與非同步預載設計的，
/// 編輯器要的是「給我這個檔案的貼圖」這種直接的存取。
/// </remarks>
public static class TextureDecoder
{
    public static Texture2D? Decode(GraphicsDevice device, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".ozj" => FromTextureData(device, new OZJReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Rgb),
            ".ozt" => FromTextureData(device, new OZTReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Bgra),
            ".ozp" => FromTextureData(device, new OZPReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Rgba),
            ".ozd" => FromTextureData(device, new OZDReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Rgba),
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tga" => FromImageSharp(device, path),
            _ => null,
        };
    }

    /// <summary>
    /// 每個 reader 的通道順序不同，而 <see cref="TextureData"/> 沒有欄位可以表達，
    /// 所以由呼叫端依副檔名指定。
    /// </summary>
    private enum ChannelOrder
    {
        Rgb,
        Rgba,
        Bgra,
    }

    private static Texture2D FromTextureData(GraphicsDevice device, TextureData data, ChannelOrder order)
    {
        if (data.IsCompressed)
        {
            // MonoGame 直接吃 DXT，不需要在 CPU 上解壓。
            var format = data.Format switch
            {
                TextureSurfaceFormat.Dxt1 => SurfaceFormat.Dxt1,
                TextureSurfaceFormat.Dxt3 => SurfaceFormat.Dxt3,
                _ => SurfaceFormat.Dxt5,
            };

            var compressed = new Texture2D(device, data.Width, data.Height, mipmap: false, format);
            compressed.SetData(data.Data);
            return compressed;
        }

        var pixels = new Color[data.Width * data.Height];

        for (int i = 0; i < pixels.Length; i++)
        {
            int src = i * data.Components;

            pixels[i] = order switch
            {
                ChannelOrder.Rgb => new Color(data.Data[src], data.Data[src + 1], data.Data[src + 2]),
                ChannelOrder.Bgra => new Color(data.Data[src + 2], data.Data[src + 1], data.Data[src], data.Data[src + 3]),
                _ => new Color(data.Data[src], data.Data[src + 1], data.Data[src + 2], data.Data[src + 3]),
            };
        }

        var texture = new Texture2D(device, data.Width, data.Height);
        texture.SetData(pixels);
        return texture;
    }

    private static Texture2D FromImageSharp(GraphicsDevice device, string path)
    {
        using var image = Image.Load<Rgba32>(path);

        var buffer = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(buffer);

        var pixels = new Color[image.Width * image.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int src = i * 4;
            pixels[i] = new Color(buffer[src], buffer[src + 1], buffer[src + 2], buffer[src + 3]);
        }

        var texture = new Texture2D(device, image.Width, image.Height);
        texture.SetData(pixels);
        return texture;
    }
}
