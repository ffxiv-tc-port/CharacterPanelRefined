using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CharacterPanelRefined;

[Serializable]
public class Configuration : IPluginConfiguration {
    [NonSerialized] private IDalamudPluginInterface pluginInterface = null!;

    public bool ShowTooltips { get; set; } = true;
    public bool UseGameLanguage { get; set; } = true;
    public bool ShowAvgDamage { get; set; } = true;
    public bool ShowAvgHealing { get; set; } = true;
    public bool ShowGearProperties { get; set; } = true;
    public bool ShowSyncedStatsOnTooltip { get; set; } = true;
    public bool ShowCritDamageIncrease { get; set; } = false;
    public bool ShowDhDamageIncrease { get; set; } = false;
    public bool ShowDoHDoLStatsWithoutFood { get; set; } = true;

    /// <summary>在「裝備」區塊多加一列「裝備屬性合計」,並在該列的 tooltip 列出各屬性明細。</summary>
    public bool ShowGearContribution { get; set; } = true;

    /// <summary>合計改成納入全部屬性(不含武器基本性能與攻擊間隔),而不是只算本職業常用的那幾項。</summary>
    public bool GearTotalAllStats { get; set; } = false;

    /// <summary>製作職/採集職的角色面板原本一律隱藏整個「裝備」區塊,開啟後照一般職業的規則顯示。</summary>
    public bool ShowGearSectionForDoHDoL { get; set; } = false;

    public int Version { get; set; } = 0;

    public static Configuration Get(IDalamudPluginInterface pluginInterface) {
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.pluginInterface = pluginInterface;
        return config;
    }

    public void Save() {
        pluginInterface.SavePluginConfig(this);
    }
}
