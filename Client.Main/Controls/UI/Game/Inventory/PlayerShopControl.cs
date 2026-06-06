#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// View-only control for seeing another player's personal shop items.
    /// Shows store name, item icons with prices, and a buy button.
    /// </summary>
    public class PlayerShopControl : UIControl
    {
        private string _storeName = string.Empty;
        private ushort _ownerId;
        private string _ownerName = string.Empty;
        private readonly List<ShopItemEntry> _items = new();
        private SpriteFont? _font;

        private const int ItemSlotSize = 48;
        private const int GridCols = 4;
        private const int TitleHeight = 36;
        private const int FooterHeight = 32;

        public event Action<ushort, string, byte>? BuyRequested;

        public PlayerShopControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = new Color(24, 28, 36, 240);
            BorderColor = ModernHudTheme.BorderOuter;
            BorderThickness = 2;
            ControlSize = new Point(ItemSlotSize * GridCols + 24, ItemSlotSize * 4 + TitleHeight + FooterHeight + 30);
            ViewSize = ControlSize;
            Visible = false;
        }

        public void OpenShop(ushort ownerId, string ownerName, string storeName, IReadOnlyList<(byte Slot, byte[] ItemData, uint Price)> items)
        {
            _ownerId = ownerId;
            _ownerName = ownerName;
            _storeName = storeName;
            _items.Clear();

            for (int i = 0; i < items.Count; i++)
            {
                _items.Add(new ShopItemEntry
                {
                    Slot = items[i].Slot,
                    ItemData = items[i].ItemData,
                    Price = items[i].Price
                });
            }

            Visible = true;
        }

        public void CloseShop()
        {
            Visible = false;
            _items.Clear();
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (spriteBatch == null || pixel == null || _font == null) return;

            Rectangle rect = DisplayRectangle;

            // Background
            spriteBatch.Draw(pixel, rect, BackgroundColor * Alpha);
            // Border
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), ModernHudTheme.BorderOuter * Alpha);

            // Title: store name
            string title = $"{_ownerName}'s Shop - {_storeName}";
            var titleSize = _font.MeasureString(title) * 0.7f;
            float titleX = rect.X + (rect.Width - titleSize.X) / 2f;
            float titleY = rect.Y + 8;
            spriteBatch.DrawString(_font, title, new Vector2(titleX + 1, titleY + 1), Color.Black * 0.7f, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
            spriteBatch.DrawString(_font, title, new Vector2(titleX, titleY), ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);

            // Items grid
            int gridX = rect.X + 12;
            int gridY = rect.Y + TitleHeight;
            for (int i = 0; i < _items.Count; i++)
            {
                int col = i % GridCols;
                int row = i / GridCols;
                var itemRect = new Rectangle(gridX + col * ItemSlotSize, gridY + row * ItemSlotSize, ItemSlotSize - 2, ItemSlotSize - 2);

                spriteBatch.Draw(pixel, itemRect, ModernHudTheme.SlotBg * Alpha);
                spriteBatch.Draw(pixel, new Rectangle(itemRect.X, itemRect.Y, itemRect.Width, 1), ModernHudTheme.BorderInner * Alpha);

                // Price text
                string price = _items[i].Price.ToString("N0");
                var priceSize = _font.MeasureString(price) * 0.4f;
                float px = itemRect.X + (itemRect.Width - priceSize.X) / 2f;
                float py = itemRect.Y + itemRect.Height - priceSize.Y - 2;
                spriteBatch.DrawString(_font, price, new Vector2(px + 1, py + 1), Color.Black * 0.7f, 0f, Vector2.Zero, 0.4f, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, price, new Vector2(px, py), ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.4f, SpriteEffects.None, 0f);
            }

            // Footer: close button hint
            string footer = "Click item to buy  •  Press ESC to close";
            var footerSize = _font.MeasureString(footer) * 0.45f;
            float fx = rect.X + (rect.Width - footerSize.X) / 2f;
            float fy = rect.Bottom - FooterHeight + 8;
            spriteBatch.DrawString(_font, footer, new Vector2(fx, fy), ModernHudTheme.TextGray * Alpha, 0f, Vector2.Zero, 0.45f, SpriteEffects.None, 0f);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible) return;

            // Close on ESC
            var kb = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            if (kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
                CloseShop();
        }

        private class ShopItemEntry
        {
            public byte Slot;
            public byte[] ItemData = [];
            public uint Price;
        }
    }
}
