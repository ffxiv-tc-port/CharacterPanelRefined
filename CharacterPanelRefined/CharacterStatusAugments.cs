using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CharacterPanelRefined;

public sealed unsafe class CharacterStatusAugments(CharacterPanelRefinedPlugin plugin) : IDisposable {
    private readonly Tooltips tooltips = new();
    private readonly GearStats gearStats = new();

    private JobId lastJob;

    /// <summary>「裝備屬性合計」那一列在「裝備等級同步」列有顯示時該待的 Y。</summary>
    private float gearTotalBaseY;

    /// <summary>上一次組裝備 tooltip 時的同步狀態,用來決定要不要重組。</summary>
    private bool gearTooltipSynced;

    private AtkUnitBase* characterStatusPtr;
    private AtkTextNode* dhChancePtr;
    private AtkTextNode* dhDamagePtr;
    private AtkTextNode* detDmgIncreasePtr;
    private AtkTextNode* critDmgPtr;
    private AtkTextNode* critChancePtr;
    private AtkTextNode* critDmgIncreasePtr;
    private AtkTextNode* physMitPtr;
    private AtkTextNode* magicMitPtr;
    private AtkTextNode* sksSpeedIncreasePtr;
    private AtkTextNode* sksGcdPtr;
    private AtkTextNode* spsSpeedIncreasePtr;
    private AtkTextNode* spsGcdPtr;
    private AtkTextNode* spsAltGcdPtr;
    private AtkTextNode* tenMitPtr;
    private AtkTextNode* tenDmgPtr;
    private AtkTextNode* pieManaPtr;
    private AtkTextNode* expectedDamagePtr;
    private AtkTextNode* expectedHealPtr;
    private AtkTextNode* ilvlSyncPtr;
    private AtkResNode* attributesPtr;
    private AtkResNode* offensivePtr;
    private AtkResNode* defensivePtr;
    private AtkResNode* physPropertiesPtr;
    private AtkResNode* gearPtr;
    private AtkResNode* pietyPtr;
    private AtkResNode* tenacityPtr;
    private AtkResNode* spellSpeedPtr;
    private AtkResNode* skillSpeedPtr;
    private AtkTextNode* craftsmanshipBasePtr;
    private AtkTextNode* controlBasePtr;
    private AtkTextNode* cpPtr;
    private AtkTextNode* cpBasePtr;
    private AtkTextNode* gatheringBasePtr;
    private AtkTextNode* perceptionBasePtr;
    private AtkTextNode* gpPtr;
    private AtkTextNode* gpBasePtr;
    private AtkTextNode* gearTotalPtr;
    private AtkCollisionNode* gearTotalCollPtr;

    internal void OnSetup(AddonEvent type, AddonArgs args) {
        // 🔴 進場先把 39 個節點指標全部清空,不可省略。
        //    CharacterStatus 關閉時節點樹會被銷毀,而本外掛只註冊了 PostSetup 與 PreRequestedUpdate、
        //    **沒有任何 Finalize 監聽器**,所以關閉的那一刻沒有地方會清指標。
        //    下面有 7 組指標是「對應 Show* 設定開啟時才重新賦值」:
        //    expectedHealPtr / expectedDamagePtr / dhDamagePtr / critDmgIncreasePtr / ilvlSyncPtr /
        //    gearTotalPtr·gearTotalCollPtr / DoH·DoL 那 8 個。
        //    使用者把某個 Show* 從開改關後再重開面板,走到這裡時那組指標不會被覆寫,
        //    於是**殘留指向已銷毀的節點**;而本函式結尾的 characterStatusPtr = atkUnitBase 會讓
        //    RequestedUpdate 開頭那道「位址不符就 ClearPointers」的防線失效(位址是相符的)。
        //    寫入懸空節點是 AVE,屬 corrupted-state exception,try/catch 與例外隔離都攔不到。
        //    ⚠️ 設定視窗自己寫著「套用任何設定變更後必須重新開啟角色面板」——
        //    觸發路徑正是我們要求使用者做的標準操作,不是邊角案例。
        //    在這裡清空是安全的:無條件賦值的那些,都在結尾呼叫 UpdateCharacterPanelForJob 之前補回;
        //    條件式的那些保持 null,由各自的使用點判空。
        ClearPointers();

        var atkUnitBase = (AtkUnitBase*)args.Addon.Address;
        var uiState = UIState.Instance();
        var job = (JobId)uiState->PlayerState.CurrentClassJobId;
        var lvl = uiState->PlayerState.CurrentLevel;

        attributesPtr = atkUnitBase->UldManager.SearchNodeById(26);
        var mndNode = attributesPtr->ChildNode;
        mndNode->X = 10;
        SetTooltip((AtkComponentNode*)mndNode, Tooltips.Entry.MainStat);
        mndNode->Y = 40;
        var intNode = mndNode->PrevSiblingNode;
        intNode->X = 10;
        intNode->Y = 40;
        SetTooltip((AtkComponentNode*)intNode, Tooltips.Entry.MainStat);

        var vitalityNode = intNode->PrevSiblingNode;
        vitalityNode->Y = 20;
        SetTooltip((AtkComponentNode*)vitalityNode, Tooltips.Entry.Vitality);

        var attributesHeight = 150;

        var mentProperties = atkUnitBase->UldManager.SearchNodeById(58);
        var magAtkPotency = mentProperties->ChildNode->PrevSiblingNode;
        var healMagPotency = magAtkPotency->PrevSiblingNode;
        if (plugin.Configuration.ShowAvgHealing) {
            healMagPotency->Y = -attributesHeight - 10;
            expectedHealPtr = AddStatRow((AtkComponentNode*)healMagPotency, Localization.Panel_Heal_per_100_Potency, true);
            SetTooltip(expectedHealPtr, Tooltips.Entry.ExpectedHeal);
        } else {
            healMagPotency->ToggleVisibility(false);
        }
        if (plugin.Configuration.ShowAvgDamage) {
            if (plugin.Configuration.ShowAvgHealing)
                attributesHeight += 20;
            magAtkPotency->Y = -attributesHeight - 10;
            magAtkPotency->PrevSiblingNode->ToggleVisibility(false); // header
            expectedDamagePtr = AddStatRow((AtkComponentNode*)magAtkPotency, Localization.Panel_Damage_per_100_Potency, true);
            SetTooltip(expectedDamagePtr, Tooltips.Entry.ExpectedDamage);
        } else {
            magAtkPotency->ToggleVisibility(false);
        }

        var dexNode = vitalityNode->PrevSiblingNode;
        dexNode->Y = 40;
        SetTooltip((AtkComponentNode*)dexNode, Tooltips.Entry.MainStat);
        var strNode = dexNode->PrevSiblingNode;
        strNode->Y = 40;
        SetTooltip((AtkComponentNode*)strNode, Tooltips.Entry.MainStat);

        var offensiveHeight = 130;

        offensivePtr = atkUnitBase->UldManager.SearchNodeById(36);
        offensivePtr->Y = attributesHeight;
        var dh = offensivePtr->ChildNode;
        dh->Y = 120;
        dhChancePtr = AddStatRow((AtkComponentNode*)dh, Localization.Panel_Direct_Hit_Chance);
        if (plugin.Configuration.ShowDhDamageIncrease) {
            offensiveHeight += 20;
            magAtkPotency->Y -= 20;
            healMagPotency->Y -= 20;
            dhDamagePtr = AddStatRow((AtkComponentNode*)dh, Localization.Panel_Damage_Increase);
        }

        SetTooltip(dhChancePtr, Tooltips.Entry.DirectHit);
        var det = dh->PrevSiblingNode;
        det->Y = 80;
        detDmgIncreasePtr = AddStatRow((AtkComponentNode*)det, Localization.Panel_Damage_Increase);
        SetTooltip(detDmgIncreasePtr, Tooltips.Entry.Determination);
        var crit = det->PrevSiblingNode;
        critChancePtr = AddStatRow((AtkComponentNode*)crit, Localization.Panel_Crit_Chance);
        critDmgPtr = AddStatRow((AtkComponentNode*)crit, Localization.Panel_Crit_Damage);
        if (plugin.Configuration.ShowCritDamageIncrease) {
            offensiveHeight += 20;
            dh->Y += 20;
            det->Y += 20;
            magAtkPotency->Y -= 20;
            healMagPotency->Y -= 20;
            critDmgIncreasePtr = AddStatRow((AtkComponentNode*)crit, Localization.Panel_Damage_Increase);
        }

        SetTooltip(critChancePtr, Tooltips.Entry.Crit);

        defensivePtr = atkUnitBase->UldManager.SearchNodeById(44);
        defensivePtr->Y = attributesHeight;
        var magicDef = defensivePtr->ChildNode;
        magicDef->Y = 60;
        magicMitPtr = AddStatRow((AtkComponentNode*)magicDef, Localization.Panel_Magic_Mitigation);
        SetTooltip(magicMitPtr, Tooltips.Entry.MagicDefense);
        var def = magicDef->PrevSiblingNode;
        physMitPtr = AddStatRow((AtkComponentNode*)def, Localization.Panel_Physical_Mitigation);
        SetTooltip(physMitPtr, Tooltips.Entry.Defense);

        mentProperties->X = 0;
        mentProperties->Y = attributesHeight + offensiveHeight;
        spellSpeedPtr = mentProperties->ChildNode;
        magAtkPotency->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false); // Header
        spsSpeedIncreasePtr = AddStatRow((AtkComponentNode*)spellSpeedPtr, Localization.Panel_Skill_Speed_Increase);
        spsGcdPtr = AddStatRow((AtkComponentNode*)spellSpeedPtr, Localization.Panel_GCD);
        spsAltGcdPtr = AddStatRow((AtkComponentNode*)spellSpeedPtr, "");
        SetTooltip(spsSpeedIncreasePtr, Tooltips.Entry.Speed);

        physPropertiesPtr = atkUnitBase->UldManager.SearchNodeById(51);
        physPropertiesPtr->Y = attributesHeight + offensiveHeight + 40;
        skillSpeedPtr = physPropertiesPtr->ChildNode;
        skillSpeedPtr->Y = 20;
        skillSpeedPtr->PrevSiblingNode->ToggleVisibility(false); // Attack Power
        ((AtkTextNode*)skillSpeedPtr->PrevSiblingNode->PrevSiblingNode->ChildNode->PrevSiblingNode)->SetText(Localization.Panel_Speed_Properties);
        sksSpeedIncreasePtr = AddStatRow((AtkComponentNode*)skillSpeedPtr, Localization.Panel_Skill_Speed_Increase);
        sksGcdPtr = AddStatRow((AtkComponentNode*)skillSpeedPtr, Localization.Panel_GCD);
        SetTooltip(sksSpeedIncreasePtr, Tooltips.Entry.Speed);

        gearPtr = atkUnitBase->UldManager.SearchNodeById(80);
        if (plugin.Configuration.ShowGearProperties) {
            gearPtr->Y = attributesHeight + offensiveHeight + 40;
            gearPtr->X = 183;
            var avgItemLevelPtr = (AtkComponentNode*)gearPtr->ChildNode;
            ilvlSyncPtr = AddStatRow(avgItemLevelPtr, Localization.Panel_Item_level_Sync, copyColor: true, expandCollisionNode: false);
            CreateNewTooltip(atkUnitBase, ilvlSyncPtr, Tooltips.Entry.ItemLevelSync);
            if (plugin.Configuration.ShowGearContribution) {
                // ⚠️ 這一列**必須加在裝備等級同步那一列之後**。CreateNewTooltip 會把
                //    「label 的 PrevSiblingNode」整個覆寫掉,前面若還接著別人就會被從兄弟鏈上砍掉
                //    (那一列於是永遠不會被畫出來)。順序反過來就會踩到。
                gearTotalPtr = AddStatRow(avgItemLevelPtr, Localization.Panel_Gear_Total, expandCollisionNode: false);
                gearTotalBaseY = gearTotalPtr->AtkResNode.Y;
                gearTotalCollPtr = AttachRowTooltip(atkUnitBase, gearTotalPtr, Tooltips.Entry.GearContribution);
                // 節點是全新的,舊的計算結果對不上 —— 逼下一次 RequestedUpdate 重算並重寫文字。
                gearStats.Invalidate();
            }
        } else {
            gearPtr->ToggleVisibility(false);
        }

        var roleProp = atkUnitBase->UldManager.SearchNodeById(86);
        roleProp->Y = 60;
        pietyPtr = roleProp->ChildNode;
        pietyPtr->Y = 20;
        pieManaPtr = AddStatRow((AtkComponentNode*)pietyPtr, Localization.Panel_Mana_per_Tick);
        SetTooltip(pieManaPtr, Tooltips.Entry.Piety);
        tenacityPtr = pietyPtr->PrevSiblingNode;
        tenMitPtr = AddStatRow((AtkComponentNode*)tenacityPtr, Localization.Panel_Damage_Mitigation);
        tenDmgPtr = AddStatRow((AtkComponentNode*)tenacityPtr, Localization.Panel_Damage_Increase);
        tenacityPtr->PrevSiblingNode->ToggleVisibility(false); // header
        SetTooltip(tenMitPtr, Tooltips.Entry.Tenacity);

        var craftingPtr = atkUnitBase->UldManager.SearchNodeById(73);
        craftingPtr->X = 0;
        craftingPtr->Y = 80;
        var control = craftingPtr->ChildNode;
        var craftsmanship = control->PrevSiblingNode;
        craftsmanship->PrevSiblingNode->ToggleVisibility(false); // header
        if (plugin.Configuration.ShowDoHDoLStatsWithoutFood) {
            control->Y += 20;
            controlBasePtr = AddStatRow((AtkComponentNode*)control, Localization.Panel_excluding_Consumables);
            cpPtr = AddStatRow((AtkComponentNode*)control, Localization.Panel_CP, copyColor: true, expandCollisionNode: false);
            cpBasePtr = AddStatRow((AtkComponentNode*)control, Localization.Panel_excluding_Consumables, expandCollisionNode: false);
            craftsmanshipBasePtr = AddStatRow((AtkComponentNode*)craftsmanship, Localization.Panel_excluding_Consumables);
        }

        var gatheringPtr = atkUnitBase->UldManager.SearchNodeById(66);
        gatheringPtr->X = 0;
        gatheringPtr->Y = 80;
        var perception = gatheringPtr->ChildNode;
        var gathering = perception->PrevSiblingNode;
        gathering->PrevSiblingNode->ToggleVisibility(false); // header
        if (plugin.Configuration.ShowDoHDoLStatsWithoutFood) {
            perception->Y += 20;
            perceptionBasePtr = AddStatRow((AtkComponentNode*)perception, Localization.Panel_excluding_Consumables);
            gpPtr = AddStatRow((AtkComponentNode*)perception, Localization.Panel_GP, copyColor: true, expandCollisionNode: false);
            gpBasePtr = AddStatRow((AtkComponentNode*)perception, Localization.Panel_excluding_Consumables, expandCollisionNode: false);
            gatheringBasePtr = AddStatRow((AtkComponentNode*)gathering, Localization.Panel_excluding_Consumables);
        }

        characterStatusPtr = atkUnitBase;

        UpdateCharacterPanelForJob(job, lvl);
    }

    private void ToggleCustomTooltipNode(AtkTextNode* node, bool enable) {
        node->AtkResNode.ToggleVisibility(enable);
        node->AtkResNode.PrevSiblingNode->ToggleVisibility(enable);
        if (enable)
            node->AtkResNode.PrevSiblingNode->PrevSiblingNode->NodeFlags |= NodeFlags.EmitsEvents;
        else
            node->AtkResNode.PrevSiblingNode->PrevSiblingNode->NodeFlags &= ~NodeFlags.EmitsEvents;
    }

    private void CreateNewTooltip(AtkUnitBase* parent, AtkTextNode* forTextNode, Tooltips.Entry tooltip) {
        var component = (AtkComponentNode*)forTextNode->AtkResNode.ParentNode;
        var newCollNode = Util.CloneNode((AtkCollisionNode*)component->Component->UldManager.RootNode);
        forTextNode->AtkResNode.PrevSiblingNode->PrevSiblingNode = (AtkResNode*)newCollNode;
        newCollNode->AtkResNode.NextSiblingNode = forTextNode->AtkResNode.PrevSiblingNode->PrevSiblingNode;
        newCollNode->AtkResNode.Y = forTextNode->AtkResNode.Y;
        newCollNode->AtkResNode.AtkEventManager.Event = null;
        component->Component->UldManager.UpdateDrawNodeList();

        // 🔴 AtkStage.Instance() 是 isPointer:true 的靜態位址，會合法回 null；裸解參考是攔不到的 AVE。
        //    ⚠️ 這一處不在原掃描清單裡，是修 Update() 那處時同檔同形一併掃到的。
        //    判空刻意放在 GetUISpace()->Create 之前：先配置再放棄會漏掉那塊 UI 記憶體。
        //    取不到就不掛 tooltip —— 上面的節點都已建好，差別只是滑過去沒有說明文字。
        var stage = AtkStage.Instance();
        if (stage == null) return;

        var tooltipArgs = IMemorySpace.GetUISpace()->Create<AtkTooltipManager.AtkTooltipArgs>();
        tooltipArgs->TextArgs.Text = (byte*)tooltips[tooltip];
        stage->TooltipManager.AttachTooltip(AtkTooltipManager.AtkTooltipType.Text, parent->Id, (AtkResNode*)newCollNode, tooltipArgs);
    }

    /// <summary>
    /// 幫某一列掛上自己的 tooltip 碰撞節點。與上面的 CreateNewTooltip 的差別是:
    /// **它不會把後面的兄弟節點砍掉**。CreateNewTooltip 直接對
    /// <c>label-&gt;PrevSiblingNode</c> 賦值,原本掛在那裡的整段鏈就此消失,
    /// 只有在該列是這個元件裡最後加上去的一列時才剛好沒事。
    /// 這裡把新節點**插進**鏈裡(前後都接好),所以加幾列都不會互相吃掉。
    /// </summary>
    /// <returns>新建的碰撞節點;任何一步取不到東西就回 null(代表這一列沒有 tooltip,其餘照常)。</returns>
    private AtkCollisionNode* AttachRowTooltip(AtkUnitBase* parent, AtkTextNode* forTextNode, Tooltips.Entry tooltip) {
        if (parent == null || forTextNode == null)
            return null;
        var component = (AtkComponentNode*)forTextNode->AtkResNode.ParentNode;
        if (component == null || component->Component == null)
            return null;
        var rootNode = component->Component->UldManager.RootNode;
        if (rootNode == null)
            return null;
        var labelNode = forTextNode->AtkResNode.PrevSiblingNode;
        if (labelNode == null)
            return null;

        // 🔴 AtkStage.Instance() 是 isPointer:true 的靜態位址,會合法回 null;裸解參考是攔不到的 AVE。
        //    判空放在配置 UI 記憶體之前 —— 先配置再放棄會漏掉那一塊。
        var stage = AtkStage.Instance();
        if (stage == null)
            return null;
        var uiSpace = IMemorySpace.GetUISpace();
        if (uiSpace == null)
            return null;

        var newCollNode = Util.CloneNode((AtkCollisionNode*)rootNode);
        var tail = labelNode->PrevSiblingNode;
        newCollNode->AtkResNode.PrevSiblingNode = tail;
        if (tail != null)
            tail->NextSiblingNode = (AtkResNode*)newCollNode;
        labelNode->PrevSiblingNode = (AtkResNode*)newCollNode;
        newCollNode->AtkResNode.NextSiblingNode = labelNode;
        newCollNode->AtkResNode.Y = forTextNode->AtkResNode.Y;
        newCollNode->AtkResNode.AtkEventManager.Event = null;
        component->Component->UldManager.UpdateDrawNodeList();

        var tooltipArgs = uiSpace->Create<AtkTooltipManager.AtkTooltipArgs>();
        if (tooltipArgs == null)
            return newCollNode;
        tooltipArgs->TextArgs.Text = (byte*)tooltips[tooltip];
        stage->TooltipManager.AttachTooltip(AtkTooltipManager.AtkTooltipType.Text, parent->Id, (AtkResNode*)newCollNode, tooltipArgs);
        return newCollNode;
    }

    /// <summary>
    /// 搬動節點的 Y。等同於 AtkResNode::SetYFloat(台服 0x14062D9A0,已離線反組譯逐條核對:
    /// 判空 → 值有變就把 DrawFlags 的 bit0 設起來 → 寫 +0x48 的 Y,總共 13 條指令)。
    /// 自己做是為了不為了搬一個 Y 就多欠一個特徵碼相依 —— 特徵碼在台服對不上是靜默的。
    /// </summary>
    private static void SetNodeY(AtkResNode* node, float y) {
        if (node == null || Math.Abs(node->Y - y) < 0.01f)
            return;
        node->DrawFlags |= 1;
        node->Y = y;
    }

    private AtkTextNode* AddStatRow(AtkComponentNode* parentNode, string label, bool hideOriginal = false, bool copyColor = false, bool expandCollisionNode = true) {
        var collisionNode = parentNode->Component->UldManager.RootNode;
        if (!hideOriginal) {
            parentNode->AtkResNode.Height += 20;
            if (expandCollisionNode)
                collisionNode->Height += 20;
        }

        var numberNode = (AtkTextNode*)collisionNode->PrevSiblingNode;
        var labelNode = (AtkTextNode*)numberNode->AtkResNode.PrevSiblingNode;
        var newNumberNode = Util.CloneNode(numberNode);
        var prevSiblingNode = labelNode->AtkResNode.PrevSiblingNode;
        labelNode->AtkResNode.PrevSiblingNode = (AtkResNode*)newNumberNode;
        newNumberNode->AtkResNode.NextSiblingNode = (AtkResNode*)labelNode;
        newNumberNode->AtkResNode.Y = parentNode->AtkResNode.Height - 24;
        if (!copyColor)
            newNumberNode->TextColor = new ByteColor { A = 0xFF, R = 0xA0, G = 0xA0, B = 0xA0 };
        newNumberNode->NodeText.StringPtr = (byte*)MemoryHelper.GameAllocateUi((ulong)newNumberNode->NodeText.BufSize);
        var newLabelNode = Util.CloneNode(labelNode);
        newNumberNode->AtkResNode.PrevSiblingNode = (AtkResNode*)newLabelNode;
        newLabelNode->AtkResNode.PrevSiblingNode = prevSiblingNode;
        newLabelNode->AtkResNode.NextSiblingNode = (AtkResNode*)newNumberNode;
        newLabelNode->AtkResNode.Y = parentNode->AtkResNode.Height - 24;
        if (!copyColor)
            newLabelNode->TextColor = new ByteColor { A = 0xFF, R = 0xA0, G = 0xA0, B = 0xA0 };
        newLabelNode->NodeText.StringPtr = (byte*)MemoryHelper.GameAllocateUi((ulong)newLabelNode->NodeText.BufSize);
        newLabelNode->SetText(label);
        if (hideOriginal) {
            labelNode->AtkResNode.ToggleVisibility(false);
            numberNode->TextColor.A = 0; // toggle visibility doesn't work since it's constantly updated by the game
        }

        parentNode->Component->UldManager.UpdateDrawNodeList();

        return newNumberNode;
    }

    private void SetTooltip(AtkComponentNode* parentNode, Tooltips.Entry entry) {
        if (!plugin.Configuration.ShowTooltips)
            return;
        if (parentNode == null)
            return;
        var collisionNode = parentNode->Component->UldManager.RootNode;
        if (collisionNode == null)
            return;

        // 🔴 同上：AtkStage.Instance() 會合法回 null，裸解參考是攔不到的 AVE。
        //    ⚠️ 這一處同樣不在原掃描清單裡，是同檔同形一併掃到的。
        //    取不到就不改 tooltip 文字（維持原文），與上面 collisionNode == null 同一條放棄路徑。
        var stage = AtkStage.Instance();
        if (stage == null)
            return;

        var ttMgr = stage->TooltipManager;
        var ttMsg = ttMgr.TooltipMap[collisionNode].Value;
        ttMsg->AtkTooltipArgs.TextArgs.Text = (byte*)tooltips[entry];
    }

    private void SetTooltip(AtkTextNode* node, Tooltips.Entry entry) {
        SetTooltip((AtkComponentNode*)node->AtkResNode.ParentNode, entry);
    }

    internal void RequestedUpdate(IntPtr addon) {
        if (addon != (IntPtr)characterStatusPtr) {
            ClearPointers();
            return;
        }

        var uiState = UIState.Instance();
        var lvl = uiState->PlayerState.CurrentLevel;
        var levelModifier = LevelModifiers.LevelTable[lvl];
        var statInfo = new StatInfo();

        var dh = Equations.CalcDh(uiState->PlayerState.Attributes[(int)Attributes.DirectHit], ref statInfo, levelModifier);
        dhChancePtr->SetText($"{statInfo.DisplayValue:P1}");
        if (dhDamagePtr != null)
            dhDamagePtr->SetText($"{statInfo.DisplayValue * 0.25:P1}");
        tooltips.Update(Tooltips.Entry.DirectHit, statInfo);

        var det = Equations.CalcDet(uiState->PlayerState.Attributes[(int)Attributes.Determination], ref statInfo, levelModifier);
        tooltips.Update(Tooltips.Entry.Determination, statInfo);
        detDmgIncreasePtr->SetText($"{statInfo.DisplayValue:P1}");

        var critRate = Equations.CalcCritRate(uiState->PlayerState.Attributes[(int)Attributes.CriticalHit], ref statInfo, levelModifier);
        tooltips.Update(Tooltips.Entry.Crit, statInfo);
        critChancePtr->SetText($"{statInfo.DisplayValue:P1}");

        var critDmg = Equations.CalcCritDmg(uiState->PlayerState.Attributes[(int)Attributes.CriticalHit], ref statInfo, levelModifier);
        critDmgPtr->SetText($"{statInfo.DisplayValue:P1}");
        if (critDmgIncreasePtr != null)
            critDmgIncreasePtr->SetText($"{critRate * (critDmg - 1):P1}");

        Equations.CalcMagicDef(uiState->PlayerState.Attributes[(int)Attributes.MagicDefense], ref statInfo, levelModifier);
        magicMitPtr->SetText($"{statInfo.DisplayValue:P0}");
        tooltips.Update(Tooltips.Entry.MagicDefense, statInfo);

        Equations.CalcDef(uiState->PlayerState.Attributes[(int)Attributes.Defense], ref statInfo, levelModifier);
        physMitPtr->SetText($"{statInfo.DisplayValue:P0}");
        tooltips.Update(Tooltips.Entry.Defense, statInfo);

        var (ilvlSync, ilvlSyncType) = IlvlSync.GetCurrentIlvlSync();
        if (ilvlSyncPtr != null) {
            ToggleCustomTooltipNode(ilvlSyncPtr, ilvlSync != null);
            if (ilvlSync != null) {
                ilvlSyncPtr->SetText($"{ilvlSync}");
            }
        }

        var jobId = (JobId)uiState->PlayerState.CurrentClassJobId;

        if (gearTotalPtr != null) {
            // 沒有裝等同步時「裝備等級同步」那一列是隱藏的,會在上面留一個 20px 的空行。
            // 把本列往上補回去,不要讓面板中間開一個洞。
            var gearY = ilvlSync == null ? gearTotalBaseY - 20 : gearTotalBaseY;
            SetNodeY((AtkResNode*)gearTotalPtr, gearY);
            SetNodeY(gearTotalPtr->AtkResNode.PrevSiblingNode, gearY);
            if (gearTotalCollPtr != null)
                SetNodeY((AtkResNode*)gearTotalCollPtr, gearY);

            var allStats = plugin.Configuration.GearTotalAllStats;
            var synced = ilvlSync != null;
            // 重算要對每個部位的每個屬性各呼叫兩次遊戲函式,只在裝備/職業/設定變動時做。
            if (gearStats.Update(jobId, allStats) || gearTooltipSynced != synced) {
                gearTooltipSynced = synced;
                // 🔴 讀不到就要在列上看得見。把未知寫成 0 會直接誤導使用者,
                //    所以完全讀不到是「?」,只讀到一部分是「數字?」。
                gearTotalPtr->SetText(!gearStats.Available
                    ? "?"
                    : gearStats.Complete
                        ? gearStats.Total.ToString("N0")
                        : $"{gearStats.Total:N0}?");
                tooltips.UpdateGearContribution(gearStats, jobId, allStats, synced);
            }
        }

        StatInfo gcdMain = new(), gcdAlt = new();
        var altGcd = jobId.AltGcd(lvl);
        var gcdMod = jobId.GcdMod(lvl);
        var withMod = gcdMod != null && plugin.CtrlHeld != gcdMod.Passive;
        Equations.CalcSpeed(uiState->PlayerState.Attributes[jobId.IsCaster() ? (int)Attributes.SpellSpeed : (int)Attributes.SkillSpeed], ref statInfo,
            ref gcdMain, ref gcdAlt, levelModifier, altGcd, withMod ? gcdMod : null, out var baseGcd, out var altBaseGcd);
        tooltips.UpdateSpeed(statInfo, gcdMain, gcdAlt, baseGcd, altBaseGcd, !plugin.CtrlHeld ? gcdMod : null);
        if (jobId.IsCaster()) {
            spsSpeedIncreasePtr->SetText($"{statInfo.DisplayValue:P1}");
            ((AtkTextNode*)spsGcdPtr->AtkResNode.PrevSiblingNode)->SetText($"{Localization.Panel_GCD}{(withMod ? $" ({gcdMod!.Abbrev})" : "")}");
            spsGcdPtr->SetText($"{gcdMain.DisplayValue:N2}s");
            if (altGcd != null) {
                ((AtkTextNode*)spsAltGcdPtr->AtkResNode.PrevSiblingNode)->SetText($"{altGcd.Name}{(withMod ? $" ({gcdMod!.Abbrev})" : "")}");
                spsAltGcdPtr->SetText($"{gcdAlt.DisplayValue:N2}s");
            }
        } else {
            sksSpeedIncreasePtr->SetText($"{statInfo.DisplayValue:P1}");
            ((AtkTextNode*)sksGcdPtr->AtkResNode.PrevSiblingNode)->SetText($"{Localization.Panel_GCD}{(withMod ? $" ({gcdMod!.Abbrev})" : "")}");
            sksGcdPtr->SetText($"{gcdMain.DisplayValue:N2}s");
        }

        Equations.CalcPiety(uiState->PlayerState.Attributes[(int)Attributes.Piety], ref statInfo, levelModifier);
        pieManaPtr->SetText($"{statInfo.DisplayValue:N0}");
        tooltips.Update(Tooltips.Entry.Piety, statInfo);

        Equations.CalcTenacityMit(uiState->PlayerState.Attributes[(int)Attributes.Tenacity], ref statInfo, levelModifier);
        tenMitPtr->SetText($"{statInfo.DisplayValue:P1}");
        var tenDmg = new StatInfo();
        var ten = Equations.CalcTenacityDmg(uiState->PlayerState.Attributes[(int)Attributes.Tenacity], ref tenDmg, levelModifier);
        tenDmgPtr->SetText($"{tenDmg.DisplayValue:P1}");
        tooltips.UpdateTenacity(Tooltips.Entry.Tenacity, statInfo, tenDmg);

        Equations.CalcHp(uiState, jobId, out var hpPerVitality, out var hpModifier);
        tooltips.UpdateVitality(jobId.ToString(), hpPerVitality, hpModifier);

        if (expectedDamagePtr != null || expectedHealPtr != null) {
            var (avgDamage, normalDamage, critDamage, avgHeal, normalHeal, critHeal) =
                Equations.CalcExpectedOutput(uiState, jobId, det, critDmg, critRate, dh, ten, levelModifier, ilvlSync, ilvlSyncType);
            if (expectedDamagePtr != null) {
                expectedDamagePtr->SetText($"{avgDamage:N0}");
                tooltips.UpdateExpectedOutput(Tooltips.Entry.ExpectedDamage, normalDamage, critDamage);
            }

            if (expectedHealPtr != null) {
                expectedHealPtr->SetText($"{avgHeal:N0}");
                tooltips.UpdateExpectedOutput(Tooltips.Entry.ExpectedHeal, normalHeal, critHeal);
            }
        }

        // ⚠️ 這一段原本只有「設定開著嗎」這道閘門,那是設定值檢查、不是判空——
        //    另外 5 組條件式指標的使用點都有 `!= null`,只有這 8 個沒有,是半套邊界檢查。
        //    真實可達路徑(不需要重開面板):OnSetup 當下這個設定是關的(8 個指標＝null),
        //    使用者接著在 /cprconfig 勾開 → 本函式的設定閘門立刻變 true(設定是即時讀的),
        //    但節點還沒建 → 對 null 解參考。面板開著時按一次 Ctrl 就會走
        //    Update() → RequestedUpdate() 撞上去。
        //    保留設定閘門(不回退既有行為),只補判空。
        if (plugin.Configuration.ShowDoHDoLStatsWithoutFood) {
            if (jobId.IsCrafter()) {
                if (craftsmanshipBasePtr != null && controlBasePtr != null && cpPtr != null && cpBasePtr != null) {
                    var fd = Equations.EstimateBaseStats(uiState);
                    craftsmanshipBasePtr->SetText(fd.GetValueOrDefault(Attributes.Craftsmanship, uiState->PlayerState.Attributes[(int)Attributes.Craftsmanship])
                        .ToString());
                    controlBasePtr->SetText(fd.GetValueOrDefault(Attributes.Control, uiState->PlayerState.Attributes[(int)Attributes.Control]).ToString());
                    cpPtr->SetText(uiState->PlayerState.Attributes[(int)Attributes.MaxCp].ToString());
                    cpBasePtr->SetText(fd.GetValueOrDefault(Attributes.MaxCp, uiState->PlayerState.Attributes[(int)Attributes.MaxCp]).ToString());
                }
            } else if (jobId.IsGatherer()) {
                if (gatheringBasePtr != null && perceptionBasePtr != null && gpPtr != null && gpBasePtr != null) {
                    var fd = Equations.EstimateBaseStats(uiState);
                    gatheringBasePtr->SetText(fd.GetValueOrDefault(Attributes.Gathering, uiState->PlayerState.Attributes[(int)Attributes.Gathering])
                        .ToString());
                    perceptionBasePtr->SetText(fd.GetValueOrDefault(Attributes.Perception, uiState->PlayerState.Attributes[(int)Attributes.Perception])
                        .ToString());
                    gpPtr->SetText(uiState->PlayerState.Attributes[(int)Attributes.MaxGp].ToString());
                    gpBasePtr->SetText(fd.GetValueOrDefault(Attributes.MaxGp, uiState->PlayerState.Attributes[(int)Attributes.MaxGp]).ToString());
                }
            }
        }

        if (jobId != lastJob) {
            UpdateCharacterPanelForJob(jobId, lvl);
        }
    }

    private void UpdateCharacterPanelForJob(JobId job, int lvl) {
        if (job.IsCrafter() || job.IsGatherer()) {
            attributesPtr->ChildNode->ToggleVisibility(false);
            attributesPtr->ChildNode->PrevSiblingNode->ToggleVisibility(false);
            attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
            attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
            offensivePtr->ToggleVisibility(false);
            defensivePtr->ToggleVisibility(false);
            physPropertiesPtr->ToggleVisibility(false);
            gearPtr->ToggleVisibility(false);
            if (expectedDamagePtr != null)
                expectedDamagePtr->AtkResNode.ParentNode->ToggleVisibility(false);
            if (expectedHealPtr != null)
                expectedHealPtr->AtkResNode.ParentNode->ToggleVisibility(false);
        } else {
            offensivePtr->ToggleVisibility(true);
            defensivePtr->ToggleVisibility(true);
            physPropertiesPtr->ToggleVisibility(true);
            if (plugin.Configuration.ShowGearProperties)
                gearPtr->ToggleVisibility(true);
            if (expectedDamagePtr != null)
                expectedDamagePtr->AtkResNode.ParentNode->ToggleVisibility(true);
            if (expectedHealPtr != null)
                expectedHealPtr->AtkResNode.ParentNode->ToggleVisibility(true);
            if (job.IsCaster()) {
                skillSpeedPtr->ToggleVisibility(false);
                spellSpeedPtr->ToggleVisibility(true);
                skillSpeedPtr->ToggleVisibility(false);
                spellSpeedPtr->ToggleVisibility(true);
                spsAltGcdPtr->AtkResNode.ToggleVisibility(job.AltGcd(lvl) != null);
                spsAltGcdPtr->AtkResNode.PrevSiblingNode->ToggleVisibility(job.AltGcd(lvl) != null);
                if (job.UsesMind()) {
                    attributesPtr->ChildNode->ToggleVisibility(true);
                    attributesPtr->ChildNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    pietyPtr->ToggleVisibility(true);
                    tenacityPtr->ToggleVisibility(false);
                } else {
                    attributesPtr->ChildNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->ToggleVisibility(true);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    pietyPtr->ToggleVisibility(false);
                    tenacityPtr->ToggleVisibility(false);
                }
            } else {
                skillSpeedPtr->ToggleVisibility(true);
                spellSpeedPtr->ToggleVisibility(false);
                if (job.UsesDexterity()) {
                    attributesPtr->ChildNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(true);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    pietyPtr->ToggleVisibility(false);
                    tenacityPtr->ToggleVisibility(false);
                } else {
                    attributesPtr->ChildNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(false);
                    attributesPtr->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode->ToggleVisibility(true);
                    pietyPtr->ToggleVisibility(false);
                    tenacityPtr->ToggleVisibility(job.UsesTenacity());
                }
            }
        }

        lastJob = job;
    }

    internal void Update() {
        // 🔴 原本是兩層裸鏈。AtkStage.Instance() 是 [StaticAddress(..., isPointer: true)]：
        //    產生器讀「指標的位址」再解參考一層，遊戲尚未建立單例時回 null
        //    （非 isPointer 的那種才保證不回 null，是擲 InvalidOperationException）。
        //    RaptureAtkUnitManager 又是 AtkStage +0x20 的裸欄位，同樣可能是 null。
        //    裸解參考 null 原生指標是 AVE，屬 corrupted-state exception，try/catch 攔不到。
        //    取不到就整段跳過 —— 與既有的 charStatus == null 同一條路徑（這一輪不更新面板）。
        var stage = AtkStage.Instance();
        if (stage == null) return;
        var raptureAtkUnitManager = stage->RaptureAtkUnitManager;
        if (raptureAtkUnitManager == null) return;

        var charStatus = raptureAtkUnitManager->GetAddonByName("CharacterStatus");
        if (charStatus != null && charStatus->IsVisible) {
            RequestedUpdate((IntPtr)charStatus);

            // ⚠️ 2026-07-31 移除「重新整理當前 tooltip」那段。原本的程式碼是:
            //     var tooltipManager  = &AtkStage.Instance()->TooltipManager;
            //     var currentTooltipNode = ((AtkResNode**)tooltipManager)[4];   // = +0x20
            //     if (currentTooltipNode == null) return;
            //     plugin.GameFunctions.AtkTooltipManagerShowNodeTooltip(tooltipManager, currentTooltipNode);
            //
            // 問題:現行 ClientStructs 的 AtkTooltipManager **在 +0x20 沒有任何具名欄位**
            // (+0x08 TooltipMap 佔到 +0x18、+0x18 AtkStage* 佔到 +0x20,之後到 +0x50 全是空白)。
            // 那 8 bytes 的內容未知,卻被當成 AtkResNode* 並交給簽名掃描取得的原生函式去解參考。
            // 這與「Tab 補完崩潰」是同一形狀:非指標值被當指標再交給遊戲。唯一的防護是 == null,
            // 擋不住計數器/旗標/其他物件指標。角色屬性視窗開著時每按一次 Ctrl 就走一次。
            //
            // 為什麼不是「加防護」而是整段移除:要驗證它是不是節點就得讀 node->Type,
            // 而那本身就是對可疑指標的解參考——範圍檢查無法讓「假設不成立也不會崩」成立。
            // 例外隔離也無效(懸空指標的 AVE 是 corrupted-state exception,try/catch 攔不到)。
            //
            // 代價:按 Ctrl 時已顯示的 tooltip 不會立即重繪,要移開滑鼠再移回來。核心的
            // RequestedUpdate 不受影響。
            // 要恢復此功能,必須先離線在 ffxiv_dx11.exe 找出寫入 AtkTooltipManager+0x20 的指令、
            // 確認它確實是當前 tooltip 的節點指標,或改用 CS 已具名的 TooltipMap 走訪。
        }
    }

    private void ClearPointers() {
        characterStatusPtr = null;
        dhChancePtr = null;
        dhDamagePtr = null;
        detDmgIncreasePtr = null;
        critDmgPtr = null;
        critChancePtr = null;
        critDmgIncreasePtr = null;
        physMitPtr = null;
        magicMitPtr = null;
        sksSpeedIncreasePtr = null;
        sksGcdPtr = null;
        spsSpeedIncreasePtr = null;
        spsGcdPtr = null;
        spsAltGcdPtr = null;
        tenMitPtr = null;
        tenDmgPtr = null;
        pieManaPtr = null;
        expectedDamagePtr = null;
        expectedHealPtr = null;
        ilvlSyncPtr = null;
        attributesPtr = null;
        offensivePtr = null;
        defensivePtr = null;
        physPropertiesPtr = null;
        gearPtr = null;
        pietyPtr = null;
        tenacityPtr = null;
        spellSpeedPtr = null;
        skillSpeedPtr = null;
        craftsmanshipBasePtr = null;
        controlBasePtr = null;
        cpPtr = null;
        cpBasePtr = null;
        gatheringBasePtr = null;
        perceptionBasePtr = null;
        gpPtr = null;
        gpBasePtr = null;
        gearTotalPtr = null;
        gearTotalCollPtr = null;
    }

    public void ReloadLocs() {
        tooltips.Reload();
    }

    public void Dispose() {
        ClearPointers();
        tooltips.Dispose();
    }
}
