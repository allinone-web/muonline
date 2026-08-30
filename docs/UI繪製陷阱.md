# UI 繪製陷阱：為什麼「畫面靜止、UI 全部消失」不會有任何錯誤訊息

> 2026-08-30 實際踩到並修好。有人改了一整晚的 UI 沒發現，因為這個 bug **完全不出聲**。
> 改任何 UI 繪製程式碼之前先讀這一份。

---

## 1. 症狀長什麼樣

| 現象 | |
|---|---|
| 畫面靜止不動 | 就是一張定格的舊圖 |
| 所有 UI 元素消失 | 按鈕、血條、attack 鈕全不見 |
| **但按鈕還有點擊聲、功能還會執行** | 輸入與 Update 完全正常 |
| 角色動不了 | 搖桿／點擊移動失效 |
| 重登才會好 | 換場景才會重置 |
| **沒有崩潰、沒有錯誤訊息** | 這是最要命的一點 |

看起來像「客戶端當掉」，其實**只有繪製死了**，遊戲邏輯活得好好的。

## 2. 為什麼一聲不吭

`MuGame.Draw` 把整個繪製包在 try/catch 裡：

```csharp
catch (Exception exception)
{
    RecordDrawException(exception, _currentDrawPhase);
    DrawEmergencyFallbackFrame();   // 把最後一張完好的畫面重新貼出來
}
```

於是繪製只要丟例外：

1. `DrawEmergencyFallbackFrame` 貼出上一張好圖 → **畫面看起來像定格**
2. 那一幀的 UI 根本沒畫 → **UI 全部消失**
3. Update 與輸入不受影響 → **按鈕有聲音、角色狀態照跑**
4. 只要造成例外的東西還在（例如視窗沒關），**每一幀重來一次**（實測連續 12000 幀以上）

而 `RecordDrawException` 原本只用 `_logger.LogError`，**裝置的 console provider 預設關閉**，
真機上等於沒有輸出。錯誤一直在發生，只是沒有人看得見。

**現在已改成同時 `Console.WriteLine`**，`devicectl --console` 讀得到：

```
[DrawEx] frame=3433 phase=Scene.DrawUi System.InvalidOperationException: Begin must be called before calling End.
[DrawEx]    at ...
```

`phase` 有四段：`Scene.Draw` / `Scene.DrawAfter` / `Scene.DrawUi` / `FrameworkDraw`。
**看到 `[DrawEx]` 就是踩到這一類問題，不要忽略它。**

## 3. 真正的成因：SpriteBatch 巢狀狀態

`SpriteBatchScope` 用一個 thread-static 堆疊記錄「目前開著哪個批次、什麼狀態」。
兩條規則：

- **建構子**：堆疊頂端若狀態相同 → 直接重用（不動批次）；狀態不同 → **先 `End()` 掉頂端那個**，再 `Begin` 自己。
- **`BeginRenderTarget`**：切 render target 之前必須先把外層批次送出去，否則外層排隊中的東西會被畫進新的 target 裡。

當年的 bug 是 `RenderTargetSection` 建構子 `End()` 了外層批次，卻用 `Peek` 把它**留在堆疊上**。
於是 section 內部再開一個狀態不同的 scope 時，建構子以為它還開著，對一個已經 End 的批次再 End 一次：

```
InvalidOperationException: Begin must be called before calling End.
```

已修正為 `Pop`／離開時 `Push` 回去並重新 `Begin`。

**為什麼只有倉庫視窗炸**：`VaultControl.EnsureStaticSurface` 內層指定 `SamplerState.LinearClamp`，
跟外層 UI 批次的取樣狀態**不同**，才會走進「先 End 外層」那條路。
狀態剛好相同的控制項走的是重用分支，碰不到。
**這代表同一個 bug 會因為取樣／混色狀態的巧合而時有時無 —— 不要用「我這樣改沒事」當作正確的證據。**

## 4. 改 UI 繪製時的規矩

1. **不要直接呼叫 `spriteBatch.Begin()` / `End()`**。一律用 `using (new SpriteBatchScope(...))`。
   直接呼叫會讓堆疊的記錄與實際狀態脫節，症狀就是本文第 1 節。
2. **切 render target 一律用 `SpriteBatchScope.BeginRenderTarget(gd, target)`**，
   不要自己 `gd.SetRenderTarget(...)`。自己切會把外層排隊中的東西烤進你的貼圖裡
   （真的發生過：聊天訊息被烤進小地圖的貼圖）。
3. **兩者都必須用 `using`**，不要手動 Dispose，也不要在中間 `return`。
   繪製中途 `return` 而沒有離開 scope，堆疊就永遠對不上。
4. **繪製路徑裡不要吞例外**。`catch { }` 在這裡等於把畫面靜止的原因埋掉。
5. **改完一定要在真機上跑一輪，並且確認 `[DrawEx]` 一行都沒有。**
   桌面版看不出來 —— 這個 bug 是在 iPhone 上才被發現的。

## 5. 怎麼驗

```bash
tools/mu ios --committed --console          # 建置 + 安裝 + 接主控台
```

進遊戲後把你改到的視窗**全部開關一遍**（背包、倉庫、商店、交易、合成、角色面板），
再換一張地圖。主控台只要出現 `[DrawEx]`，就是還沒改好。

沒有 `[DrawEx]` 才算通過。**「我看畫面正常」不算** ——
上一張好圖會被重新貼出來，光看畫面分不出來是正常繪製還是定格。

## 6. 判斷表

| 你看到的 | 幾乎可以確定是 |
|---|---|
| 畫面定格、UI 消失、但按鈕有聲音 | 繪製丟例外 → 看 `[DrawEx]` |
| 某個貼圖裡混進了不該有的東西（例如聊天文字被烤進小地圖） | 自己切了 render target，沒先送出外層批次 |
| 開某個視窗才會出事，別的視窗沒事 | 那個視窗的 sampler／blend 狀態跟外層不同 |
| 角色完全動不了、但按鈕還能按 | 看不見但邏輯上開著的視窗抓住 `MouseControl`，`IsPointOverOpenWindow` 不看座標只看它 |
