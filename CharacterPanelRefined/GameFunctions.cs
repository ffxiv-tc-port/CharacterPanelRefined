using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CharacterPanelRefined;

public sealed class GameFunctions {

    // 刻意不宣告上游的 AtkTooltipManagerShowNodeTooltip:唯一的使用處(CharacterStatusAugments.Update 的 tooltip 重繪)
    // 已移除,沒有呼叫端就不必每次載入掃那條特徵碼。要恢復時連同特徵碼從上游取回。

    public GameFunctions() {
        Service.GameInteropProvider.InitializeFromAttributes(this);
    }
}
