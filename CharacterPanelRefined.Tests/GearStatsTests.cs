using System;
using System.Globalization;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace CharacterPanelRefined.Tests;

/// <summary>
/// 「裝備屬性合計」的純邏輯測試。這裡刻意只測不需要遊戲記憶體的部分:
/// 哪些屬性算進合計、以及 tooltip 在各語系下組得出來且塞得進緩衝區。
/// 真正的讀取(InventoryManager / GetParameterValue)必須有遊戲行程,測不了。
/// </summary>
public class GearStatsTests {
    private const uint Strength = (uint)Attributes.Strength;
    private const uint Dexterity = (uint)Attributes.Dexterity;
    private const uint Vitality = (uint)Attributes.Vitality;
    private const uint Intelligence = (uint)Attributes.Intelligence;
    private const uint Mind = (uint)Attributes.Mind;
    private const uint Piety = (uint)Attributes.Piety;
    private const uint Tenacity = (uint)Attributes.Tenacity;
    private const uint CriticalHit = (uint)Attributes.CriticalHit;
    private const uint DirectHit = (uint)Attributes.DirectHit;
    private const uint Determination = (uint)Attributes.Determination;
    private const uint SkillSpeed = (uint)Attributes.SkillSpeed;
    private const uint SpellSpeed = (uint)Attributes.SpellSpeed;
    private const uint Defense = (uint)Attributes.Defense;
    private const uint Craftsmanship = (uint)Attributes.Craftsmanship;
    private const uint Gathering = (uint)Attributes.Gathering;

    /// <summary>Item 表的「攻擊間隔」欄位,量級是幾千,絕對不可以混進任何合計裡。</summary>
    private const uint Delay = 14;

    private static bool Job(uint param, JobId job) => GearStats.CountsTowardsTotal(param, job, false);

    private static bool All(uint param, JobId job) => GearStats.CountsTowardsTotal(param, job, true);

    [Test]
    public void TankCountsStrengthAndTenacityButNotDexterity() {
        ClassicAssert.IsTrue(Job(Strength, JobId.PLD));
        ClassicAssert.IsTrue(Job(Tenacity, JobId.PLD));
        ClassicAssert.IsTrue(Job(SkillSpeed, JobId.PLD));
        ClassicAssert.IsFalse(Job(Dexterity, JobId.PLD));
        ClassicAssert.IsFalse(Job(SpellSpeed, JobId.PLD));
        ClassicAssert.IsFalse(Job(Piety, JobId.PLD));
        ClassicAssert.IsFalse(Job(Intelligence, JobId.PLD));
    }

    [Test]
    public void HealerCountsMindAndPiety() {
        ClassicAssert.IsTrue(Job(Mind, JobId.WHM));
        ClassicAssert.IsTrue(Job(Piety, JobId.WHM));
        ClassicAssert.IsTrue(Job(SpellSpeed, JobId.WHM));
        ClassicAssert.IsFalse(Job(SkillSpeed, JobId.WHM));
        ClassicAssert.IsFalse(Job(Intelligence, JobId.WHM));
        ClassicAssert.IsFalse(Job(Tenacity, JobId.WHM));
    }

    [Test]
    public void CasterCountsIntelligenceButNotPiety() {
        ClassicAssert.IsTrue(Job(Intelligence, JobId.BLM));
        ClassicAssert.IsTrue(Job(SpellSpeed, JobId.BLM));
        ClassicAssert.IsFalse(Job(Piety, JobId.BLM));
        ClassicAssert.IsFalse(Job(Mind, JobId.BLM));
        ClassicAssert.IsFalse(Job(SkillSpeed, JobId.BLM));
    }

    [Test]
    public void RangedCountsDexterity() {
        ClassicAssert.IsTrue(Job(Dexterity, JobId.BRD));
        ClassicAssert.IsTrue(Job(SkillSpeed, JobId.BRD));
        ClassicAssert.IsFalse(Job(Strength, JobId.BRD));
    }

    [Test]
    public void EveryCombatJobCountsTheSharedSubstats() {
        foreach (var job in new[] { JobId.PLD, JobId.WHM, JobId.BLM, JobId.BRD, JobId.MNK, JobId.PCT, JobId.VPR }) {
            ClassicAssert.IsTrue(Job(Vitality, job), "vitality for {0}", job);
            ClassicAssert.IsTrue(Job(CriticalHit, job), "crit for {0}", job);
            ClassicAssert.IsTrue(Job(DirectHit, job), "direct hit for {0}", job);
            ClassicAssert.IsTrue(Job(Determination, job), "determination for {0}", job);
        }
    }

    [Test]
    public void CraftersAndGatherersCountTheirOwnStats() {
        ClassicAssert.IsTrue(Job(Craftsmanship, JobId.CRP));
        ClassicAssert.IsFalse(Job(Vitality, JobId.CRP));
        ClassicAssert.IsFalse(Job(CriticalHit, JobId.CRP));

        ClassicAssert.IsTrue(Job(Gathering, JobId.MIN));
        ClassicAssert.IsFalse(Job(Craftsmanship, JobId.MIN));
        ClassicAssert.IsFalse(Job(Vitality, JobId.MIN));
    }

    [Test]
    public void AllStatsModeIncludesEverythingExceptItemPerformanceColumns() {
        // 開了「全部屬性」之後,連本職業用不到的都算進去
        ClassicAssert.IsTrue(All(Intelligence, JobId.PLD));
        ClassicAssert.IsTrue(All(Piety, JobId.PLD));
        ClassicAssert.IsTrue(All(Defense, JobId.PLD));
        // 但武器基本性能/攻擊間隔/格擋這幾欄永遠不算 —— 它們不是點數預算
        foreach (uint param in new uint[] { 12, 13, Delay, 15, 16, 17, 18 })
            ClassicAssert.IsFalse(All(param, JobId.PLD), "param {0} must never be summed", param);
    }

    [Test]
    public void AllStatsModeIsAlwaysAtLeastAsInclusiveAsJobMode() {
        foreach (var job in new[] { JobId.PLD, JobId.WHM, JobId.BLM, JobId.BRD, JobId.CRP, JobId.MIN, JobId.ADV }) {
            for (uint param = 1; param <= 73; param++) {
                if (Job(param, job))
                    ClassicAssert.IsTrue(All(param, job), "param {0} counted for {1} in job mode but not in all mode", param, job);
            }
        }
    }

    [Test]
    public void UnknownJobStillProducesASensibleSet() {
        // 職業 id 讀成 0(ADV)或未來新增的職業時不可以擲例外,也不可以整組變空
        ClassicAssert.IsTrue(Job(Vitality, JobId.ADV));
        ClassicAssert.IsTrue(Job(Strength, JobId.ADV));
        ClassicAssert.IsFalse(Job(Delay, JobId.ADV));
    }

    [Test]
    public void GearTooltipBuildsForEveryLocale([Values("en", "de", "fr", "ja", "zh-Hant")] string locale) {
        Localization.Culture = new CultureInfo(locale);
        using var tooltips = new Tooltips();
        tooltips.Reload();

        // 讀不到的情況:四種失敗原因都要組得出文字
        foreach (var failure in Enum.GetValues<GearStatsFailure>()) {
            if (failure == GearStatsFailure.None)
                continue;
            var broken = new GearStats { Failure = failure };
            tooltips.UpdateGearContribution(broken, JobId.PLD, false, false);
            AssertFits(tooltips, locale, "failure " + failure);
        }

        // 有資料的情況:納入合計與未納入合計兩組都要有東西,而且要塞得進 4096 bytes
        var stats = new GearStats { Failure = GearStatsFailure.None, SlotsRead = 13, Total = 12345 };
        for (uint param = 1; param <= 73; param++)
            stats.Entries.Add(new GearStatEntry { ParamId = param, Name = "Attribute " + param, FromItem = 100 + (int)param, FromMateria = (int)param % 3 });

        tooltips.UpdateGearContribution(stats, JobId.PLD, false, false);
        AssertFits(tooltips, locale, "job total");
        tooltips.UpdateGearContribution(stats, JobId.PLD, true, true);
        AssertFits(tooltips, locale, "all stats, synced");

        // 不完整時要走得到那條紅字路徑
        stats.SlotsUnreadable = 2;
        ClassicAssert.IsFalse(stats.Complete);
        tooltips.UpdateGearContribution(stats, JobId.WHM, false, true);
        AssertFits(tooltips, locale, "incomplete");
    }

    /// <summary>
    /// tooltip 的緩衝區是固定 4096 bytes 的 AllocHGlobal。超過就會整段被丟掉(使用者看到的是
    /// 上一次的內容),所以每一條路徑都要驗長度,不能只驗「有沒有擲例外」。
    /// </summary>
    private static void AssertFits(Tooltips tooltips, string locale, string what) {
        var length = tooltips.EncodedLength(Tooltips.Entry.GearContribution);
        ClassicAssert.Greater(length, 0, "{0} / {1}: tooltip is empty", locale, what);
        ClassicAssert.Less(length + 1, Tooltips.BufferSize, "{0} / {1}: tooltip does not fit the buffer", locale, what);
    }
}
