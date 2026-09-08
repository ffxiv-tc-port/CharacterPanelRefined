using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace CharacterPanelRefined;

public class Tooltips : IDisposable {
    private const ushort TitleColor = 8;
    private const ushort HighlightColor = 33;
    private const ushort GreenColor = 43;
    private const ushort OrangeColor = 31;
    private const ushort RedColor = 0x1f4;

    private readonly Dictionary<Entry, IntPtr> allocations = new();

    private readonly Dictionary<Entry, SeString> tooltips = new();

    private readonly Dictionary<(Entry, string), int> keywordIndices = new();

    public enum Entry {
        Crit,
        DirectHit,
        Determination,
        Speed,
        ExpectedDamage,
        ExpectedHeal,
        Tenacity,
        Piety,
        Defense,
        MagicDefense,
        Vitality,
        MainStat,
        ItemLevelSync,
        GearContribution,
    }

    /// <summary>每個 tooltip 的緩衝區大小。<see cref="WriteString"/> 一定要照這個值做邊界檢查。</summary>
    private const int AllocationSize = 4096;

    /// <summary>裝備明細最多列這麼多列,免得 tooltip 撐爆緩衝區。</summary>
    private const int MaxGearEntries = 24;

    public Tooltips() {
        foreach (var entry in Enum.GetValues<Entry>()) {
            var allocation = Marshal.AllocHGlobal(AllocationSize);
            // AllocHGlobal 不歸零。還沒寫過內容就被 hover 到的話,遊戲會從這裡讀一串未初始化的
            // 位元組直到碰上 0 為止 —— 先寫一個終止符,讓「還沒算出來」變成空字串而不是亂碼。
            Marshal.WriteByte(allocation, 0);
            allocations.Add(entry, allocation);
        }
    }

    private void LoadLocString(Entry entry, params string[] localization) {
        var str = new SeString();
        tooltips[entry] = str;
        var sb = new StringBuilder();
        var keywords = new HashSet<string>();

        void AddKeyword(string keyword) {
            while (!keywords.Add(keyword))
                keyword += '_';
            keywordIndices[(entry, keyword)] = str.Payloads.Count;
        }

        foreach (var s in localization) {
            int start;
            var end = -1;
            while ((start = s.IndexOf('{', end + 1)) >= 0) {
                if (start - end > 1) {
                    sb.Append(s, end + 1, start - end - 1);
                    str.Append(sb.ToString());
                    sb.Clear();
                }

                end = s.IndexOf('}', start);
                var len = end - start - 1;
                var keyword = s.Substring(start + 1, len);
                if (keyword[0] == '@') {
                    ushort col = 0;
                    switch (keyword) {
                        case "@Title":
                            col = TitleColor;
                            break;
                        case "@Highlight":
                            col = HighlightColor;
                            break;
                        case "@Red":
                            col = RedColor;
                            break;
                        case "@Wasting":
                            AddKeyword(keyword);
                            break;
                    }

                    str.Append(new UIForegroundPayload(col));
                } else {
                    AddKeyword(keyword);
                    str.Append(new TextPayload(""));
                }
            }

            if (end < s.Length - 1)
                sb.Append(s, end + 1, s.Length - end - 1);
        }

        if (sb.Length > 0)
            str.Append(sb.ToString());
    }

    public void Reload() {
        LoadLocString(Entry.Crit, Localization.Tooltips_Crit_Tooltip, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.Determination, Localization.Tooltips_Determination_Tooltip, "\n", Localization.Tooltips_Wasting, "\n",
            Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.DirectHit, Localization.Tooltips_Direct_Hit_Tooltip, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.Tenacity, Localization.Tooltips_Tenacity_Tooltip, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier, "\n\n", Localization.Tooltips_Tenacity_Damage_Increase, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.Piety, Localization.Tooltips_Piety_Tooltip, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.Defense, Localization.Tooltips_Defense_Tooltip, "\n", Localization.Tooltips_Wasting, "\n", Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.MagicDefense, Localization.Tooltips_Magic_Defense_Tooltip, "\n", Localization.Tooltips_Wasting, "\n",
            Localization.Tooltips_Next_Tier);
        LoadLocString(Entry.Vitality, Localization.Tooltips_Vitality_Tooltip);
        LoadLocString(Entry.ExpectedDamage, Localization.Tooltips_Expected_Damage);
        LoadLocString(Entry.ExpectedHeal, Localization.Tooltips_Expected_Heal);
        LoadLocString(Entry.MainStat, Localization.Tooltips_Main_Stat_Tooltip);
        WriteString(Entry.MainStat);
        LoadLocString(Entry.ItemLevelSync, Localization.Tooltips_Item_Level_Sync);
        WriteString(Entry.ItemLevelSync);
        // 裝備明細是每次更新才組出來的,但先放一份標題進去 —— 面板還沒更新過就被 hover 到時
        // 至少是個看得懂的標題,不是空白。
        tooltips[Entry.GearContribution] = new SeString(new TextPayload(Localization.Tooltips_Gear_Title));
        WriteString(Entry.GearContribution);
    }

    /// <summary>
    /// 組出「裝備提供的屬性」明細。列數會隨裝備變動,所以不走 LoadLocString 的樣板機制,
    /// 直接組 SeString。
    /// </summary>
    public void UpdateGearContribution(GearStats stats, JobId job, bool allStats, bool synced) {
        var str = new SeString();

        void Text(string text) => str.Append(new TextPayload(text));

        void Colored(ushort color, string text) {
            str.Append(new UIForegroundPayload(color));
            str.Append(new TextPayload(text));
            str.Append(new UIForegroundPayload(0));
        }

        Colored(TitleColor, Localization.Tooltips_Gear_Title);
        Text("\n");

        if (!stats.Available) {
            Colored(OrangeColor, FailureText(stats.Failure));
            tooltips[Entry.GearContribution] = str;
            WriteString(Entry.GearContribution);
            return;
        }

        var counted = new List<GearStatEntry>();
        var others = new List<GearStatEntry>();
        foreach (var entry in stats.Entries) {
            if (GearStats.CountsTowardsTotal(entry.ParamId, job, allStats))
                counted.Add(entry);
            else
                others.Add(entry);
        }

        var written = 0;

        void WriteGroup(string header, List<GearStatEntry> group) {
            if (group.Count == 0)
                return;
            Text("\n");
            Colored(HighlightColor, header);
            foreach (var entry in group) {
                if (written >= MaxGearEntries) {
                    Text("\n...");
                    return;
                }

                written++;
                Text($"\n{entry.Name}  {entry.Total:N0}");
                if (entry.FromMateria != 0)
                    Text("  " + string.Format(Localization.Tooltips_Gear_Split, entry.FromItem.ToString("N0"), entry.FromMateria.ToString("N0")));
            }
        }

        WriteGroup(Localization.Tooltips_Gear_Counted, counted);
        WriteGroup(Localization.Tooltips_Gear_Not_Counted, others);

        Text("\n\n");
        Colored(HighlightColor, allStats ? Localization.Tooltips_Gear_Total_All : Localization.Tooltips_Gear_Total_Job);
        Text($"  {stats.Total:N0}");

        if (!stats.Complete) {
            Text("\n");
            Colored(RedColor, string.Format(Localization.Tooltips_Gear_Incomplete, stats.SlotsUnreadable));
        }

        Text("\n\n" + Localization.Tooltips_Gear_Raw_Note);
        if (synced) {
            Text("\n");
            Colored(OrangeColor, Localization.Tooltips_Gear_Synced_Note);
        }

        tooltips[Entry.GearContribution] = str;
        WriteString(Entry.GearContribution);
    }

    private static string FailureText(GearStatsFailure failure) =>
        failure switch {
            GearStatsFailure.GameFunction => Localization.Tooltips_Gear_Unavailable_Function,
            GearStatsFailure.Sheet => Localization.Tooltips_Gear_Unavailable_Sheet,
            GearStatsFailure.Exception => Localization.Tooltips_Gear_Unavailable_Error,
            _ => Localization.Tooltips_Gear_Unavailable_Inventory
        };

    public IntPtr this[Entry entry] => allocations[entry];

    /// <summary>編碼後的長度,給測試用來確認 tooltip 沒有超出 4096 bytes 的緩衝區。</summary>
    internal int EncodedLength(Entry entry) => tooltips.TryGetValue(entry, out var str) ? str.Encode().Length : 0;

    /// <summary>緩衝區大小,給測試對照用。</summary>
    internal static int BufferSize => AllocationSize;

    public void UpdateExpectedOutput(Entry entry, double normalOutput, double critOutput) {
        var tooltip = tooltips[entry];
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, "NormalValue")]]).Text = normalOutput.ToString("N0");
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, "CritValue")]]).Text = critOutput.ToString("N0");
        WriteString(entry);
    }

    public void UpdateVitality(string job, double hpPerVitality, double jobModifer) {
        var tooltip = tooltips[Entry.Vitality];
        ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Vitality, "HpPerPoint")]]).Text = hpPerVitality.ToString("N0");
        ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Vitality, "Job")]]).Text = job;
        ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Vitality, "BaseHp")]]).Text = jobModifer.ToString("P0");
        WriteString(Entry.Vitality);
    }

    public void UpdateSpeed(in StatInfo statInfo, in StatInfo gcdMain, in StatInfo gcdAlt, int baseGcd, int? altBaseGcd, in GcdModifier? mod) {
        var sb = new StringBuilder();
        sb.Append(Localization.Tooltips_Skill_Spell_Speed_Tooltip)
            .Append("\n\n");
        if (mod != null) {
            sb.Append(mod.Passive ? Localization.Tooltips_Skill_Spell_Speed_With_Mod : Localization.Tooltips_Skill_Spell_Speed_Without_Mod)
                .Append("\n\n");
        }

        sb.Append(Localization.Tooltips_Skill_Spell_Speed_GCD)
            .Append('\n')
            .Append(Localization.Tooltips_Wasting)
            .Append('\n')
            .Append(Localization.Tooltips_Next_Tier)
            .Append("\n\n");
        if (altBaseGcd != null) {
            sb.Append(Localization.Tooltips_Skill_Spell_Speed_GCD)
                .Append('\n')
                .Append(Localization.Tooltips_Wasting)
                .Append('\n')
                .Append(Localization.Tooltips_Next_Tier)
                .Append("\n\n");
        }
        sb.Append(Localization.Tooltips_Skill_Spell_Speed_DoT_Increase)
            .Append('\n')
            .Append(Localization.Tooltips_Wasting)
            .Append('\n')
            .Append(Localization.Tooltips_Next_Tier);

        LoadLocString(Entry.Speed, sb.ToString());

        var tooltip = tooltips[Entry.Speed];

        ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Speed, "GCD")]]).Text = (baseGcd / 100.0).ToString("N2");
        if (mod != null) {
            ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Speed, "ModName")]]).Text = mod.Name;
            if (mod.Passive)
                ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Speed, "ModName_")]]).Text = mod.Name;
        }
        Update(Entry.Speed, gcdMain);
        if (altBaseGcd != null) {
            ((TextPayload)tooltip.Payloads[keywordIndices[(Entry.Speed, "GCD_")]]).Text = (altBaseGcd.Value / 100.0).ToString("N2");
            Update(Entry.Speed, gcdAlt, "_");
            Update(Entry.Speed, statInfo, "__");
        } else {
            Update(Entry.Speed, statInfo, "_");
        }

        WriteString(Entry.Speed);
    }

    public void UpdateTenacity(Entry entry, in StatInfo tenMit, in StatInfo tenDmg, string suffix = "") {
        Update(Entry.Tenacity, tenMit);
        Update(Entry.Tenacity, tenDmg, "_");
        WriteString(entry);
    }

    public void Update(Entry entry, in StatInfo statInfo, string suffix = "") {
        var tooltip = tooltips[entry];
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, "PointsPerTier" + suffix)]]).Text = statInfo.PointsPerTier.ToString("N1");
        var wasting = statInfo.CurrentValue - statInfo.PrevTier;
        ((UIForegroundPayload)tooltip.Payloads[keywordIndices[(entry, "@Wasting" + suffix)]]).ColorKey = wasting == 0 ? GreenColor : OrangeColor;
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, "Wasting" + suffix)]]).Text = wasting.ToString();
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, $"Points{suffix}{suffix}")]]).Text =
            wasting == 1 ? Localization.Tooltips_Wasting_Points_Singular : Localization.Tooltips_Wasting_Points_Plural;
        var nextTier = statInfo.NextTier - statInfo.CurrentValue;
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, "NextTier" + suffix)]]).Text = nextTier.ToString();
        ((TextPayload)tooltip.Payloads[keywordIndices[(entry, $"Points{suffix}{suffix}_")]]).Text =
            nextTier == 1 ? Localization.Tooltips_Wasting_Points_Singular : Localization.Tooltips_Wasting_Points_Plural;
        WriteString(entry);
    }

    private void WriteString(Entry entry) {
        var target = allocations[entry];
        var encoded = tooltips[entry].Encode();

        // 緩衝區是固定 4096 bytes 的 AllocHGlobal,原本沒有任何邊界檢查。
        // 超長就整段放棄(保留上一次的內容),不要寫出去把堆積踩壞。
        if (encoded.Length + 1 > AllocationSize) {
            Service.PluginLog?.Warning($"Tooltip {entry} is {encoded.Length} bytes, exceeds the {AllocationSize} byte buffer - not written");
            return;
        }

        Marshal.Copy(encoded, 0, target, encoded.Length);
        Marshal.WriteByte(target, encoded.Length, 0);
    }

    private void ReleaseUnmanagedResources() {
        foreach (var (_, alloc) in allocations) {
            Marshal.FreeHGlobal(alloc);
        }

        allocations.Clear();
    }

    public void Dispose() {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~Tooltips() {
        ReleaseUnmanagedResources();
    }
}
