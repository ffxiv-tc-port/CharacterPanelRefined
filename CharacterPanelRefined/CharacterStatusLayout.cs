using System;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CharacterPanelRefined;

/// <summary>
/// CharacterStatus 節點樹的型別指紋。OnSetup 沿這些節點 ID 與兄弟鏈直接寫座標、把節點當元件節點解參考；
/// 鏈上任何一格不是預期的節點，就是把別的結構當指標用（AVE，攔不到）。
/// 所以在動任何節點之前整組比對：節點 ID、節點型別、子鏈長度、元件型別與元件內部的列結構全部相符才放行。
/// 期望值取自台服 7.25 的 ui/uld/CharacterStatus.uld（ChildNode＝ULD ChildNodeId，PrevSiblingNode＝ULD 第 3 欄）。
/// </summary>
/// <remarks>只讀節點欄位、不呼叫遊戲函式（容器查找除外）；只在框架執行緒、拿到節點的同一幀呼叫。</remarks>
internal static unsafe class CharacterStatusLayout {
    private readonly record struct Link(uint Id, NodeType Type, Link[]? Children = null);

    private const NodeType AttributeRow = (NodeType)1001;
    private const NodeType MindIntelligenceRow = (NodeType)1002;
    private const NodeType StatRow = (NodeType)1004;

    // 容器（根 widget，型別一律 Res）→ 從 ChildNode 起沿 PrevSiblingNode 走到 null 的完整子鏈。
    private static readonly (uint Id, Link[] Children)[] Containers = [
        (26, [new(35, MindIntelligenceRow), new(34, MindIntelligenceRow), new(33, AttributeRow), new(32, AttributeRow), new(31, AttributeRow), new(27, NodeType.Res)]),
        (58, [new(65, StatRow), new(64, StatRow), new(63, StatRow), new(59, NodeType.Res)]),
        (36, [new(43, StatRow), new(42, StatRow), new(41, StatRow), new(37, NodeType.Res)]),
        (44, [new(50, StatRow), new(49, StatRow), new(45, NodeType.Res)]),
        (51, [new(57, StatRow), new(56, StatRow), new(52, NodeType.Res, [new(55, NodeType.NineGrid), new(54, NodeType.Text), new(53, NodeType.Image)])]),
        (80, [new(85, StatRow), new(81, NodeType.Res)]),
        (86, [new(92, StatRow), new(91, StatRow), new(87, NodeType.Res)]),
        (73, [new(79, StatRow), new(78, StatRow), new(74, NodeType.Res)]),
        (66, [new(72, StatRow), new(71, StatRow), new(67, NodeType.Res)]),
    ];

    // 每一列元件的內部：RootNode 起沿 PrevSiblingNode＝碰撞 → 數值文字 → 標籤文字（AddStatRow／SetTooltip 靠這個順序）。
    private static readonly Link[] RowComponent = [new(4, NodeType.Collision), new(3, NodeType.Text), new(2, NodeType.Text)];

    /// <returns>全部相符回 null；否則回第一個不符處的說明（給 log 用）。</returns>
    internal static string? FindMismatch(AtkUnitBase* addon) {
        if (addon == null)
            return "addon 是 null";
        var address = (nint)addon;
        return FindMismatch(id => (nint)((AtkUnitBase*)address)->UldManager.SearchNodeById(id));
    }

    /// <param name="searchNodeById">依根 widget 節點 ID 查節點位址，查不到回 0。</param>
    internal static string? FindMismatch(Func<uint, nint> searchNodeById) {
        foreach (var (id, children) in Containers) {
            var node = (AtkResNode*)searchNodeById(id);
            if (node == null)
                return $"容器 #{id}：找不到節點";
            if (node->Type != NodeType.Res)
                return $"容器 #{id}：型別期望 {(ushort)NodeType.Res}，實際 {(ushort)node->Type}";
            var bad = CheckChain(node->ChildNode, children, $"#{id}");
            if (bad != null)
                return bad;
        }
        return null;
    }

    private static string? CheckChain(AtkResNode* node, Link[] expected, string where) {
        for (var i = 0; i < expected.Length; i++) {
            var want = expected[i];
            if (node == null)
                return $"{where} 子鏈第 {i} 格：期望 Node {want.Id}（型別 {(ushort)want.Type}），實際是 null";
            if (node->NodeId != want.Id || node->Type != want.Type)
                return $"{where} 子鏈第 {i} 格：期望 Node {want.Id}（型別 {(ushort)want.Type}），實際 Node {node->NodeId}（型別 {(ushort)node->Type}）";

            var bad = (ushort)want.Type >= 1000
                ? CheckRowComponent((AtkComponentNode*)node, $"{where}/{want.Id}")
                : want.Children != null
                    ? CheckChain(node->ChildNode, want.Children, $"{where}/{want.Id}")
                    : null;
            if (bad != null)
                return bad;
            node = node->PrevSiblingNode;
        }
        return node == null ? null : $"{where} 子鏈比預期多出 Node {node->NodeId}（型別 {(ushort)node->Type}）";
    }

    private static string? CheckRowComponent(AtkComponentNode* node, string where) {
        var component = node->Component;
        if (component == null)
            return $"{where}：元件是 null";
        var info = (AtkUldComponentInfo*)component->UldManager.Objects;
        if (info == null)
            return $"{where}：元件資訊是 null";
        if (info->ComponentType != ComponentType.Base)
            return $"{where}：元件型別期望 {ComponentType.Base}，實際 {info->ComponentType}";
        return CheckChain(component->UldManager.RootNode, RowComponent, $"{where} 元件內");
    }
}
