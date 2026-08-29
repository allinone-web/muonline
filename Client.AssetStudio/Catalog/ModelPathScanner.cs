using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Client.AssetStudio.Catalog;

/// <summary>一個類別載入的模型。<see cref="Source"/> 說明是怎麼推出來的。</summary>
public readonly record struct ModelReference(string Path, string Source);

/// <summary>
/// 從 <c>Client.Main</c> 的怪物／NPC 類別裡挖出它們載入哪些 <c>.bmd</c>。
/// </summary>
/// <remarks>
/// 這層知識只存在於程式碼裡 —— <c>Bali</c> 這個類別掛著 <c>[NpcInfo(150, "Bali")]</c>，
/// 但它的模型是 <c>Monster/Monster33.bmd</c>，兩個編號完全不同，沒有任何資料檔記錄這個對應。
///
/// 取得的方式是走訪該類別方法的 IL，而不是實例化它再呼叫 <c>Load()</c>：
/// <c>MonsterObject.Load()</c> 會碰 <c>BMDLoader</c>、<c>World</c>、<c>GraphicsDevice</c>，
/// 為了讀一個字串把整個遊戲拉起來不划算，而且在沒有視窗的 CLI 模式下根本做不到。
///
/// 兩種來源：
/// <list type="bullet">
/// <item><b>直接路徑</b>：<c>Prepare("Monster/Monster33.bmd")</c> 這種字面值。</item>
/// <item><b>身體部位</b>：<c>SetBodyPartsAsync("Npc/", "ManHead", …, 2)</c> 組出
/// <c>Npc/ManHead02.bmd</c> 等五個檔案。NPC 的可見身體全部來自這裡 ——
/// 主模型 <c>Man01.bmd</c> 本身<b>一個網格都沒有</b>，只是骨架。</item>
/// </list>
///
/// 撿不到的情況有一種而且是預期的：<c>Prepare(item.TexturePath)</c> 這種
/// 執行期才決定的武器模型（見 <c>DeathGorgon</c>、<c>SkeletonArcher</c>）。
/// </remarks>
public static class ModelPathScanner
{
    private const string BodyPartsMethod = "SetBodyPartsAsync";

    /// <summary>掃一個類別（含其基底類別）用到的所有 <c>.bmd</c> 相對路徑，維持出現順序。</summary>
    public static ModelReference[] Scan(Type type)
    {
        var found = new List<ModelReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            // 基底類別（MonsterObject / NPCObject）的通用路徑排在自己的後面，
            // 這樣「這個類別自己指定的模型」永遠是第一個。
            foreach (var reference in ScanDeclaredMembers(current))
            {
                if (seen.Add(reference.Path))
                    found.Add(reference);
            }
        }

        return found.ToArray();
    }

    private static IEnumerable<ModelReference> ScanDeclaredMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.DeclaredOnly;

        // Load() 通常排在建構子後面，但模型路徑幾乎都在 Load() 裡，所以先掃它。
        var methods = type.GetMethods(flags)
            .OrderByDescending(m => m.Name is "Load" or "LoadContent")
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(flags));

        foreach (var method in methods)
        {
            foreach (var reference in ScanMethod(method))
                yield return reference;
        }
    }

    /// <summary>
    /// 讀一個方法體。<c>async</c> 方法的內容被編譯進狀態機的 <c>MoveNext</c>，
    /// 所以要先跟著 <see cref="AsyncStateMachineAttribute"/> 轉過去。
    /// </summary>
    private static IEnumerable<ModelReference> ScanMethod(MethodBase method)
    {
        var target = ResolveBody(method);
        if (target is null)
            yield break;

        var instructions = IlWalker.Walk(target);
        var module = target.Module;

        // 呼叫指令的引數就排在它前面，所以維護一個「最近看過的常數」滑動視窗。
        var recentStrings = new List<string>();
        int? recentInt = null;

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr)
            {
                if (instruction.String is not null)
                {
                    recentStrings.Add(instruction.String);

                    // 有些類別是用字串串接組路徑（Prepare(name + ".bmd")），
                    // IL 裡就會出現一個光禿禿的 ".bmd"。那種路徑是執行期才決定的，撿不到也不該假裝撿到。
                    if (instruction.String.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)
                        && System.IO.Path.GetFileNameWithoutExtension(instruction.String).Length > 0)
                    {
                        yield return new ModelReference(Normalize(instruction.String), "直接指定");
                    }
                }

                continue;
            }

            if (instruction.Int32 is int value)
            {
                recentInt = value;
                continue;
            }

            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (!IsBodyParts(module, instruction.MetadataToken))
            {
                // 別的呼叫會吃掉引數，視窗跟著清空，才不會把上一個呼叫的字串
                // 誤算成下一個呼叫的引數。
                recentStrings.Clear();
                recentInt = null;
                continue;
            }

            foreach (var path in BuildBodyParts(recentStrings, recentInt))
                yield return new ModelReference(path, "身體部位");

            recentStrings.Clear();
            recentInt = null;
        }
    }

    /// <summary>
    /// <c>SetBodyPartsAsync(pathPrefix, helm, armor, pant, glove, boot, skinIndex)</c>
    /// → <c>{pathPrefix}{part}{skinIndex:D2}.bmd</c>（見 <c>NPCObject.SetBodyPartsAsync</c>）。
    /// </summary>
    private static IEnumerable<string> BuildBodyParts(List<string> strings, int? skinIndex)
    {
        if (strings.Count < 6 || skinIndex is not int index)
            yield break;

        var arguments = strings.TakeLast(6).ToArray();
        string prefix = arguments[0];

        // 第一個引數一定是資料夾（"Npc/"、"Player/"）。不是的話代表這六個字串
        // 不是同一個呼叫的引數 —— 例如引數是變數而不是字面值，視窗裡留的是別處的殘餘。
        if (!prefix.EndsWith('/') && !prefix.EndsWith('\\'))
            yield break;

        string suffix = index.ToString("D2");

        foreach (var part in arguments.Skip(1))
        {
            // 空字串代表那個部位不存在（有些 NPC 沒有手套或靴子）。
            if (string.IsNullOrWhiteSpace(part) || part.Contains('/') || part.Contains('\\'))
                continue;

            yield return Normalize($"{prefix}{part}{suffix}.bmd");
        }
    }

    private static bool IsBodyParts(Module module, int? token)
    {
        if (token is not int value)
            return false;

        try
        {
            return module.ResolveMethod(value)?.Name == BodyPartsMethod;
        }
        catch
        {
            // 泛型內容裡的權杖解不開是正常的，不是這個呼叫就好。
            return false;
        }
    }

    private static MethodBase? ResolveBody(MethodBase method)
    {
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachine?.StateMachineType is null)
            return method;

        return stateMachine.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? method;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
