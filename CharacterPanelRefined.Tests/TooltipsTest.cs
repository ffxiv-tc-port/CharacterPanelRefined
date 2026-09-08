using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace CharacterPanelRefined.Tests;

public class TooltipsTest {
    private static string[] locales = { "en", "de", "fr", "ja" };
    // MainStat / ItemLevelSync 是純靜態文字,GearContribution 走自己的組字流程(測試在 GearStatsTests),
    // 三者都沒有 {PointsPerTier} 這類佔位符,不能丟給下面那條 tooltips.Update() 的預設路徑。
    private static Tooltips.Entry[] entries = Enum.GetValues<Tooltips.Entry>()
        .Except(new [] { Tooltips.Entry.MainStat, Tooltips.Entry.ItemLevelSync, Tooltips.Entry.GearContribution }).ToArray();

    private Tooltips tooltips = null!;

    [OneTimeSetUp]
    public void Setup() {
        tooltips = new Tooltips();
    }

    [Test]
    public void AllLocalizationsCompile([ValueSource(nameof(locales))] string locale, [ValueSource(nameof(entries))] Tooltips.Entry entry) {
        Localization.Culture = new CultureInfo(locale);

        tooltips.Reload();

        var stat = new StatInfo { CurrentValue = 1000, DisplayValue = 0.5, NextTier = 1010, PrevTier = 999 };
        var gcdMod = GcdModifier.LeyLines;

        switch (entry) {
            case Tooltips.Entry.ExpectedDamage:
                tooltips.UpdateExpectedOutput(Tooltips.Entry.ExpectedDamage, 1, 1.5);
                break;
            case Tooltips.Entry.ExpectedHeal:
                tooltips.UpdateExpectedOutput(Tooltips.Entry.ExpectedHeal, 1, 1.5);
                break;
            case Tooltips.Entry.Speed:
                tooltips.UpdateSpeed(stat, stat, stat, 250, null, null);
                tooltips.UpdateSpeed(stat, stat, stat, 250, 280, null);
                tooltips.UpdateSpeed(stat, stat, stat, 250, null, gcdMod);
                tooltips.UpdateSpeed(stat, stat, stat, 250, 280, gcdMod);
                break;
            case Tooltips.Entry.Vitality:
                tooltips.UpdateVitality("ADV", 1, 1);
                break;
            default:
                tooltips.Update(entry, stat);
                break;
        }
    }
}
