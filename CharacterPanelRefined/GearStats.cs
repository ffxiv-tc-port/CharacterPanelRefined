using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace CharacterPanelRefined;

/// <summary>單一屬性的「這套裝備給了多少」明細。</summary>
public struct GearStatEntry {
    /// <summary>BaseParam 的 row id(與 <see cref="Attributes"/> 同一套編號)。</summary>
    public uint ParamId;

    /// <summary>屬性名稱,一律取自遊戲的 BaseParam 資料表,不寫死任何語言。</summary>
    public string Name;

    /// <summary>裝備本體提供的點數,已含高品質(HQ)加成。</summary>
    public int FromItem;

    /// <summary>魔晶石提供的點數,已套用遊戲自己的單件上限裁切。</summary>
    public int FromMateria;

    public int Total => FromItem + FromMateria;
}

public enum GearStatsFailure {
    None,

    /// <summary>InventoryItem::GetParameterValue 的特徵碼在這個客戶端沒解出來。</summary>
    GameFunction,

    /// <summary>InventoryManager / 裝備欄容器還沒準備好。</summary>
    Inventory,

    /// <summary>BaseParam 資料表讀不到。</summary>
    Sheet,

    /// <summary>計算途中擲了例外(細節寫在 log)。</summary>
    Exception
}

/// <summary>
/// 「目前裝備中的部位總共提供了多少屬性」的計算。純讀取,不寫回任何遊戲狀態。
///
/// 資料來源:<c>InventoryManager::GetInventoryContainer(InventoryType.EquippedItems)</c>,
/// 每個欄位的點數交給遊戲自己的 <c>InventoryItem::GetParameterValue</c> 算 ——
/// 那正是遊戲計算裝備屬性時走的同一個函式,所以高品質加成、魔晶石、以及
/// 「單件裝備某屬性的上限」全部與遊戲一致,不需要我們自己重算一遍公式。
///
/// 🔴 這個函式**不套用等級同步/裝備等級同步**(同步是在別的地方套的),
///    所以這裡算出來的是裝備原始值。介面上必須講清楚。
///
/// 🔴 讀不到道具資料時遊戲回的是 <c>0xFFFFFFFF</c> 而不是 0
///    (離線反組譯 0x140845FA0:道具列查不到就 <c>lea eax,[rsi-1]</c> 帶著 rsi=0 回傳)。
///    照著加下去合計會直接爆掉,所以每個部位先用一次探測呼叫判斷。
/// </summary>
public sealed unsafe class GearStats {
    /// <summary>裝備欄容器的合理大小上限;超出就當成讀到壞資料,整段放棄。</summary>
    private const int MaxSlots = 32;

    /// <summary>一個部位最多五顆魔晶石。</summary>
    private const int MateriaSlots = 5;

    /// <summary>
    /// 不納入「全部屬性合計」的欄位:武器的物理/魔法基本性能、攻擊間隔、附加效果、
    /// 攻擊次數、格擋發動力、格擋性能。這些不是點數預算,加進總和只會變成沒有意義的數字
    /// (攻擊間隔本身就是幾千的量級)。它們仍然會出現在明細裡。
    /// </summary>
    private static readonly HashSet<uint> NonBudgetParams = [12, 13, 14, 15, 16, 17, 18];

    private static (uint Id, string Name)[]? paramCache;

    public readonly List<GearStatEntry> Entries = [];

    public GearStatsFailure Failure { get; internal set; } = GearStatsFailure.Inventory;

    /// <summary>有沒有算出任何東西。false 代表介面上要顯示「不知道」而不是 0。</summary>
    public bool Available => Failure == GearStatsFailure.None;

    /// <summary>成功讀完的部位數。</summary>
    public int SlotsRead { get; internal set; }

    /// <summary>讀不到道具資料的部位數。大於 0 時合計是不完整的。</summary>
    public int SlotsUnreadable { get; internal set; }

    /// <summary>合計(依設定為「本職業常用屬性」或「全部屬性」)。</summary>
    public int Total { get; internal set; }

    /// <summary>合計是不是完整的。不完整時介面要標出來,不可以裝作沒事。</summary>
    public bool Complete => Available && SlotsUnreadable == 0;

    private ulong lastFingerprint;
    private JobId lastJob = (JobId)byte.MaxValue;
    private bool lastAllStats;
    private bool hasResult;

    /// <summary>強制下一次 <see cref="Update"/> 重算。</summary>
    public void Invalidate() => hasResult = false;

    /// <summary>
    /// 只有在裝備內容/職業/設定變動時才重算。角色面板的 RequestedUpdate 是每幀都會來的,
    /// 而重算一次要對每個部位的每個屬性各呼叫兩次遊戲函式,不能每幀做。
    /// </summary>
    /// <returns>這次有沒有真的重算(呼叫端據此決定要不要重組 tooltip 與重寫節點文字)。</returns>
    public bool Update(JobId job, bool allStats) {
        var fingerprint = Fingerprint();
        if (hasResult && fingerprint == lastFingerprint && job == lastJob && allStats == lastAllStats)
            return false;

        lastFingerprint = fingerprint;
        lastJob = job;
        lastAllStats = allStats;
        hasResult = true;
        Recompute(job, allStats);
        return true;
    }

    /// <summary>
    /// 取裝備欄容器。任何一步取不到就回 null —— 呼叫端一律判空,
    /// 不靠 try/catch(解參考 null 原生指標是 AVE,屬 corrupted-state exception,catch 不到)。
    /// </summary>
    private static InventoryContainer* GetContainer() {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
            return null;
        var container = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null || container->Items == null || !container->IsLoaded)
            return null;
        if (container->Size <= 0 || container->Size > MaxSlots)
            return null;
        return container;
    }

    /// <summary>裝備內容的指紋。容器讀不到時回 0,於是容器一旦可讀就會自動觸發重算。</summary>
    private static ulong Fingerprint() {
        var container = GetContainer();
        if (container == null)
            return 0;

        var hash = 14695981039346656037UL;
        for (var i = 0; i < container->Size; i++) {
            var item = container->Items + i;
            hash = Mix(hash, item->ItemId);
            hash = Mix(hash, (byte)item->Flags);
            for (var m = 0; m < MateriaSlots; m++) {
                hash = Mix(hash, item->Materia[m]);
                hash = Mix(hash, item->MateriaGrades[m]);
            }
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value) {
        hash ^= value;
        return hash * 1099511628211UL;
    }

    /// <summary>
    /// BaseParam 資料表的 (row id, 名稱) 快取。
    /// ⚠️ 一律以 row.RowId 為準,不用「第 n 列就是 id n」——那個前提在別的表上已經被推翻過。
    /// </summary>
    private static (uint Id, string Name)[]? GetParams() {
        if (paramCache != null)
            return paramCache;

        var sheet = Service.DataManager.GetExcelSheet<BaseParam>();
        if (sheet.Count == 0)
            return null;

        var list = new List<(uint, string)>();
        foreach (var row in sheet) {
            if (row.RowId == 0)
                continue;
            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            list.Add((row.RowId, name));
        }

        if (list.Count == 0)
            return null;

        paramCache = list.ToArray();
        return paramCache;
    }

    private void Recompute(JobId job, bool allStats) {
        Entries.Clear();
        SlotsRead = 0;
        SlotsUnreadable = 0;
        Total = 0;
        Failure = GearStatsFailure.None;

        // 特徵碼沒解出來時,產生器產的包裝會擲 InvalidOperationException;
        // 這裡先判指標,讓「這個客戶端沒有這個函式」變成介面上的「不知道」而不是例外。
        if (InventoryItem.MemberFunctionPointers.GetParameterValue == null) {
            Failure = GearStatsFailure.GameFunction;
            return;
        }

        var container = GetContainer();
        if (container == null) {
            Failure = GearStatsFailure.Inventory;
            return;
        }

        var pars = GetParams();
        if (pars == null) {
            Failure = GearStatsFailure.Sheet;
            return;
        }

        var fromItem = new int[pars.Length];
        var fromMateria = new int[pars.Length];

        try {
            for (var i = 0; i < container->Size; i++) {
                var item = container->Items + i;
                if (item->ItemId == 0)
                    continue;

                // 探測:道具列查不到時遊戲回 0xFFFFFFFF,而那個判斷發生在任何屬性相關邏輯之前,
                // 所以問一個屬性就能決定整個部位讀不讀得到。
                if (InventoryItem.GetParameterValue(1, item, false, false, false, false) == uint.MaxValue) {
                    SlotsUnreadable++;
                    continue;
                }

                for (var p = 0; p < pars.Length; p++) {
                    var id = pars[p].Id;
                    var withMateria = InventoryItem.GetParameterValue(id, item, true, true, true, true);
                    var withoutMateria = InventoryItem.GetParameterValue(id, item, false, true, true, true);
                    if (withMateria > int.MaxValue || withoutMateria > int.MaxValue)
                        continue;

                    fromItem[p] += (int)withoutMateria;
                    fromMateria[p] += (int)withMateria - (int)withoutMateria;
                }

                SlotsRead++;
            }
        } catch (Exception e) {
            Service.PluginLog.Warning(e, "Failed to read equipped gear stats");
            Failure = GearStatsFailure.Exception;
            Entries.Clear();
            return;
        }

        for (var p = 0; p < pars.Length; p++) {
            if (fromItem[p] == 0 && fromMateria[p] == 0)
                continue;

            var entry = new GearStatEntry {
                ParamId = pars[p].Id,
                Name = pars[p].Name,
                FromItem = fromItem[p],
                FromMateria = fromMateria[p]
            };
            Entries.Add(entry);
            if (CountsTowardsTotal(entry.ParamId, job, allStats))
                Total += entry.Total;
        }
    }

    /// <summary>這個屬性算不算進列上那個合計數字。</summary>
    public static bool CountsTowardsTotal(uint paramId, JobId job, bool allStats) =>
        allStats ? !NonBudgetParams.Contains(paramId) : IsJobRelevant(paramId, job);

    /// <summary>
    /// 「本職業常用屬性」的定義,刻意與 UpdateCharacterPanelForJob 決定要顯示哪幾列的規則一致 ——
    /// 也就是角色面板上這個職業真的看得到的那幾項。主屬性與副屬性的量級不同,
    /// 把用不到的項目(例如騎士的智力)一起加進去只會讓數字失去比較的意義。
    /// </summary>
    private static bool IsJobRelevant(uint paramId, JobId job) {
        var attribute = (Attributes)paramId;

        if (job.IsCrafter())
            return attribute is Attributes.Craftsmanship or Attributes.Control or Attributes.MaxCp;
        if (job.IsGatherer())
            return attribute is Attributes.Gathering or Attributes.Perception or Attributes.MaxGp;

        return attribute switch {
            Attributes.Vitality => true,
            Attributes.CriticalHit => true,
            Attributes.DirectHit => true,
            Attributes.Determination => true,
            Attributes.Piety => job.UsesMind(),
            Attributes.Tenacity => job.UsesTenacity(),
            Attributes.SpellSpeed => job.IsCaster(),
            Attributes.SkillSpeed => !job.IsCaster(),
            Attributes.Mind => job.UsesMind(),
            Attributes.Intelligence => job.IsCaster() && !job.UsesMind(),
            Attributes.Dexterity => !job.IsCaster() && job.UsesDexterity(),
            Attributes.Strength => !job.IsCaster() && !job.UsesDexterity(),
            _ => false
        };
    }
}
