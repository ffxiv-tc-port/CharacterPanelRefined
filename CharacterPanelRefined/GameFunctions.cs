using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CharacterPanelRefined;

public sealed class GameFunctions {

    // ⚠️ 目前沒有呼叫端。唯一的使用處(CharacterStatusAugments.Update 裡的 tooltip 重繪)
    // 已於 2026-07-31 移除,因為它把未文件化的 AtkTooltipManager+0x20 當節點指標餵給這個函式。
    // 保留宣告是為了日後驗證過偏移之後好恢復;標成 Fallible 讓它掃不到時只是留 null,
    // 不會在外掛載入時拋 SignatureException——現在沒人用它,不值得為它冒載入失敗的風險。
    [Signature("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 41 83 FF 71", Fallibility = Fallibility.Fallible)]
    public readonly unsafe delegate*unmanaged[Thiscall]<AtkTooltipManager*, AtkResNode*, void> AtkTooltipManagerShowNodeTooltip = null!;

    public GameFunctions() {
        Service.GameInteropProvider.InitializeFromAttributes(this);
    }
}
