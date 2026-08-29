using System.Reflection;
using System.Reflection.Emit;

namespace Client.AssetStudio.Catalog;

/// <summary>IL 裡的一個指令，只保留這個工具用得到的部分。</summary>
public readonly record struct IlInstruction(OpCode OpCode, int? Int32, string? String, int? MetadataToken);

/// <summary>
/// 最小可用的 IL 走訪器：把方法體拆成指令序列。
/// </summary>
/// <remarks>
/// 為什麼需要真的走訪、而不是逐位元組找 <c>0x72</c>：
/// 只找 <c>ldstr</c> 的話拿得到「有哪些字串」，但拿不到<b>順序與歸屬</b> ——
/// 而 <c>SetBodyPartsAsync("Npc/", "ManHead", "ManUpper", "ManPant", "ManGlove", "ManBoots", 2)</c>
/// 這種呼叫的意義完全來自「哪六個字串連在一起、後面跟著哪個整數」。
/// NPC 的可見身體是這樣組出來的（<c>Npc/ManUpper02.bmd</c>），
/// 主模型 <c>Man01.bmd</c> 本身<b>一個網格都沒有</b>，只是骨架。
/// 沒有這一層，工具打開 NPC 只會顯示一副看不見的骨頭。
///
/// 指令長度表是從 <see cref="OpCodes"/> 反射建出來的，不是手抄的常數表 ——
/// 手抄一定會漏，而漏一個就會讓後面的位移全部錯開，症狀是「大部分對、少數莫名其妙」。
/// </remarks>
public static class IlWalker
{
    private static readonly Dictionary<short, OpCode> OpCodeByValue = BuildOpCodeTable();

    public static IReadOnlyList<IlInstruction> Walk(MethodBase method)
    {
        var instructions = new List<IlInstruction>();

        byte[]? il;
        Module module;

        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
            module = method.Module;
        }
        catch
        {
            return instructions;
        }

        if (il is null)
            return instructions;

        int offset = 0;

        while (offset < il.Length)
        {
            short value = il[offset];

            // 0xFE 開頭是兩位元組的指令碼。
            if (value == 0xFE && offset + 1 < il.Length)
            {
                value = (short)(0xFE00 | il[offset + 1]);
                offset += 2;
            }
            else
            {
                offset++;
            }

            if (!OpCodeByValue.TryGetValue(value, out var opCode))
                return instructions; // 走丟了就停手，回傳已經解出來的部分。

            int operandSize = OperandSize(opCode, il, offset);
            if (operandSize < 0 || offset + operandSize > il.Length)
                return instructions;

            instructions.Add(Decode(opCode, il, offset, module));
            offset += operandSize;
        }

        return instructions;
    }

    private static IlInstruction Decode(OpCode opCode, byte[] il, int offset, Module module)
    {
        if (opCode == OpCodes.Ldstr)
        {
            int token = BitConverter.ToInt32(il, offset);

            try
            {
                return new IlInstruction(opCode, null, module.ResolveString(token), token);
            }
            catch
            {
                return new IlInstruction(opCode, null, null, token);
            }
        }

        if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineField)
            return new IlInstruction(opCode, null, null, BitConverter.ToInt32(il, offset));

        return new IlInstruction(opCode, ReadInt32Constant(opCode, il, offset), null, null);
    }

    /// <summary>載入整數常數的指令有九種寫法（<c>ldc.i4.0</c> … <c>ldc.i4</c>），全部歸一。</summary>
    private static int? ReadInt32Constant(OpCode opCode, byte[] il, int offset)
    {
        if (opCode == OpCodes.Ldc_I4) return BitConverter.ToInt32(il, offset);
        if (opCode == OpCodes.Ldc_I4_S) return (sbyte)il[offset];
        if (opCode == OpCodes.Ldc_I4_M1) return -1;
        if (opCode == OpCodes.Ldc_I4_0) return 0;
        if (opCode == OpCodes.Ldc_I4_1) return 1;
        if (opCode == OpCodes.Ldc_I4_2) return 2;
        if (opCode == OpCodes.Ldc_I4_3) return 3;
        if (opCode == OpCodes.Ldc_I4_4) return 4;
        if (opCode == OpCodes.Ldc_I4_5) return 5;
        if (opCode == OpCodes.Ldc_I4_6) return 6;
        if (opCode == OpCodes.Ldc_I4_7) return 7;
        if (opCode == OpCodes.Ldc_I4_8) return 8;

        return null;
    }

    private static int OperandSize(OpCode opCode, byte[] il, int offset) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
            or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,

        // switch：4 byte 的分支數，後面接該數量個 4 byte 位移。
        OperandType.InlineSwitch => offset + 4 <= il.Length
            ? 4 + (BitConverter.ToInt32(il, offset) * 4)
            : -1,

        _ => -1,
    };

    private static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<short, OpCode>();

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
                table[opCode.Value] = opCode;
        }

        return table;
    }
}
