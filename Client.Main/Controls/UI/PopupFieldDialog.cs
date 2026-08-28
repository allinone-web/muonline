using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Client.Main.Controls.UI
{
    /// <summary>
    /// 對話框的共用底板。
    ///
    /// 原本是九張切片組成的外框（Interface/GFx/popupfield01-09.ozd）。
    /// 那套素材是為 1024x768 的桌面畫的，在 3x 的手機螢幕上放大必糊，
    /// 而且風格與其他已經改成程式繪製的面板不一致。
    /// 現在一律用 <see cref="MobileUi.DrawPanel"/> 繪製 —— 任何解析度都銳利、
    /// 半透明、不佔資源。舊素材的清理見 docs/待清理素材.md。
    /// </summary>
    public abstract class PopupFieldDialog : DialogControl, IUiTexturePreloadable
    {
        /// <summary>標題列高度。0 表示沒有標題列。</summary>
        protected int TitleBarHeight { get; set; }

        public IEnumerable<string> GetPreloadTexturePaths() => Enumerable.Empty<string>();

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sprite = GraphicsManager.Instance.Sprite;
            if (sprite != null)
                MobileUi.DrawPanel(sprite, DisplayRectangle, TitleBarHeight);

            base.Draw(gameTime);
        }
    }
}
