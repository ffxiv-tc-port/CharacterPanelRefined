using System;
using System.Buffers.Binary;
using System.Globalization;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace CharacterPanelRefined;

public enum IlvlSyncType {
    Strict,
    // a lvl 80 535 weapon will not be synced in a level 80 duty
    // a higher level weapon will be synced to 530 however
    LevelBased
}

public static class IlvlSync {
    // 副本導演宣告品級同步那段碼：movzx eax,byte [rbx+旗標]; test al,al; js 最低品級分支;
    // test al,40h; jne; cmp word [rbx+同步品級],r12w; je。0x80／0x40 兩個位元也由這串位元組釘住。
    internal const string InstanceDirectorSig = "0F B6 83 ?? ?? ?? ?? 84 C0 0F 88 ?? ?? ?? ?? A8 40 0F 85 ?? ?? ?? ?? 66 44 39 A3 ?? ?? ?? ?? 0F 84";
    // 公共內容導演宣告品級同步：cmp word [rcx+同步品級],0; mov rdi,rcx; je。
    internal const string PublicDirectorSig = "66 83 B9 ?? ?? ?? ?? 00 48 8B F9 0F 84";

    // js 跳到的最低品級分支：cmp word [rbx+最低品級],r12w（66 44 39 A3 disp32）後接 je（0F 84）。
    // 刻意不寫成特徵碼字串：它只在跳躍目標上比對，全 .text 本來就不唯一，寫成字串會被改版日的特徵碼普查誤報。
    private static ReadOnlySpan<byte> CmpWordRbxR12 => [0x66, 0x44, 0x39, 0xA3];
    private static ReadOnlySpan<byte> JeRel32 => [0x0F, 0x84];

    // 只在插件建構子寫一次；0＝沒有初始化，不讀導演。
    private static int flagsOffset;
    private static int minIlvlOffset;
    private static int syncIlvlOffset;

    /// <summary>
    /// 從遊戲碼讀出三個位移，任何一步對不上就擲例外讓插件載入失敗，不帶著猜的位移跑。
    /// 只能從插件建構子（同步載入路徑）呼叫。推導見 commit 訊息。
    /// </summary>
    public static unsafe void Initialize(ISigScanner scanner) {
        // 讀掃描器自己的 .text 視圖（Dalamud 的服務實例是檔案複本），與特徵碼比對的是同一份位元組。
        var text = new ReadOnlySpan<byte>((void*)scanner.TextSectionBase, scanner.TextSectionSize);
        var (flags, min, sync) = Derive(text, TextIndex(scanner, InstanceDirectorSig), TextIndex(scanner, PublicDirectorSig));
        flagsOffset = flags;
        minIlvlOffset = min;
        syncIlvlOffset = sync;
        Service.PluginLog.Information($"品級同步位移：旗標 +0x{flags:X}、最低品級 +0x{min:X}、同步品級 +0x{sync:X}（從遊戲碼讀出）");
    }

    private static int TextIndex(ISigScanner scanner, string sig) {
        if (!scanner.TryScanText(sig, out var address))
            throw new InvalidOperationException($"品級同步：遊戲碼裡找不到特徵碼「{sig}」（多半是遊戲改版），為避免讀錯位移，插件不載入。");
        return checked((int)((long)address - (long)scanner.Module.BaseAddress - scanner.TextSectionOffset));
    }

    /// <summary>純函式：text＝整段 .text，兩個 site＝特徵碼命中點在 text 內的索引；回傳（旗標, 最低品級, 同步品級）位移。</summary>
    internal static unsafe (int Flags, int MinIlvl, int SyncIlvl) Derive(ReadOnlySpan<byte> text, int icdSite, int pcdSite) {
        RequirePattern(text, icdSite, InstanceDirectorSig, "副本導演");
        RequirePattern(text, pcdSite, PublicDirectorSig, "公共內容導演");
        var flags = ReadInt32(text, icdSite + 3);
        var sync = ReadInt32(text, icdSite + 27);
        var minSite = (long)icdSite + 15 + ReadInt32(text, icdSite + 11);
        if (minSite < 0 || minSite > text.Length - 10)
            throw Fail($"最低品級分支跳出 .text（索引 {minSite}）");
        if (!text.Slice((int)minSite, 4).SequenceEqual(CmpWordRbxR12) || !text.Slice((int)minSite + 8, 2).SequenceEqual(JeRel32))
            throw Fail("最低品級分支不是預期的 cmp word／je 指令");
        var min = ReadInt32(text, (int)minSite + 4);
        var pcdSync = ReadInt32(text, pcdSite + 3);

        // 同步品級是兩種導演共用的 ContentDirector 欄位：兩處碼讀到同一個位移才算數。
        if (pcdSync != sync)
            throw Fail($"副本導演（+0x{sync:X}）與公共內容導演（+0x{pcdSync:X}）讀到的同步品級位移不同");
        RequireInside(flags, sizeof(byte), sizeof(InstanceContentDirector), "旗標");
        RequireInside(min, sizeof(ushort), sizeof(ContentDirector), "最低品級");
        RequireInside(sync, sizeof(ushort), sizeof(ContentDirector), "同步品級");
        return (flags, min, sync);
    }

    private static int ReadInt32(ReadOnlySpan<byte> text, int at) => BinaryPrimitives.ReadInt32LittleEndian(text.Slice(at, 4));

    private static void RequirePattern(ReadOnlySpan<byte> text, int at, string sig, string what) {
        var tokens = sig.Split(' ');
        if (at < 0 || at > text.Length - tokens.Length)
            throw Fail($"{what}的位置超出 .text（索引 {at}）");
        for (var i = 0; i < tokens.Length; i++) {
            if (tokens[i] != "??" && text[at + i] != byte.Parse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                throw Fail($"{what}的指令位元組與預期不符（第 {i} 個位元組）");
        }
    }

    private static void RequireInside(int offset, int fieldSize, int structSize, string what) {
        if (offset <= 0 || offset > structSize - fieldSize)
            throw Fail($"{what}位移 +0x{offset:X} 不在結構範圍內（大小 0x{structSize:X}）");
    }

    private static InvalidOperationException Fail(string why) =>
        new($"品級同步：{why}。遊戲碼與插件的假設不一致（多半是遊戲改版），為避免讀錯位移，插件不載入。");

    public static unsafe (uint?, IlvlSyncType) GetCurrentIlvlSync() {
        var icDirector = syncIlvlOffset != 0 && EventFramework.Instance() != null ? EventFramework.Instance()->GetInstanceContentDirector() : null;
        if (icDirector != null) {
            if (icDirector->InstanceContentType != InstanceContentType.BeginnerTraining && (icDirector->ContentDirector.Director.ContentFlags & 1) == 0) {
                var icd = (IntPtr)icDirector;
                // min ilvl
                if (*(byte*)(icd + flagsOffset) >= 0x80 && *(ushort*)(icd + minIlvlOffset) > 0) {
                    Service.PluginLog.Debug($"Using min ilvl {*(ushort*)(icd + minIlvlOffset)}");
                    return (*(ushort*)(icd + minIlvlOffset), IlvlSyncType.Strict);
                }

                // duty is synced
                if (((*(byte*)(icd + flagsOffset) & 0x40) == 0 || UIState.Instance()->PlayerState.IsLevelSynced) && *(ushort*)(icd + syncIlvlOffset) > 0) {
                    Service.PluginLog.Debug($"Using duty ilvl sync {*(ushort*)(icd + syncIlvlOffset)}");
                    return (*(ushort*)(icd + syncIlvlOffset), IlvlSyncType.Strict);
                }
            }
        }

        var pcDirector = syncIlvlOffset != 0 && EventFramework.Instance() != null ? EventFramework.Instance()->GetPublicContentDirector() : null;
        if (pcDirector != null) {
            var pcd = (IntPtr)pcDirector;
            // eureka / bozja is synced
            if (*(ushort*)(pcd + syncIlvlOffset) > 0) {
                Service.PluginLog.Debug($"Using public content ilvl sync {*(ushort*)(pcd + syncIlvlOffset)}");
                return (*(ushort*)(pcd + syncIlvlOffset), IlvlSyncType.Strict);
            }
        }

        if (UIState.Instance()->PlayerState.IsLevelSynced) {
            var syncedLevel = UIState.Instance()->PlayerState.CurrentLevel;
            var ilvl = (uint)(syncedLevel switch {
                100 => 790,
                >=93 => 660 + (syncedLevel - 93) * 3,
                >=91 => 650 + (syncedLevel - 91) * 5,
                90 => 660,
                >=83 => 530 + (syncedLevel - 83) * 3,
                >=81 => 520 + (syncedLevel - 81) * 5,
                80 => 530,
                >=73 => 400 + (syncedLevel - 73) * 3,
                >=71 => 390 + (syncedLevel - 71) * 5,
                70 => 400,
                >=63 => 270 + (syncedLevel - 63) * 3,
                >=61 => 260 + (syncedLevel - 61) * 5,
                60 => 270,
                >=53 => 130 + (syncedLevel - 53) * 3,
                >=51 => 120 + (syncedLevel - 51) * 5,
                50 => 130,
                _ => syncedLevel
            });
            Service.PluginLog.Debug($"Using level based ilvl {ilvl}");
            return (ilvl, IlvlSyncType.LevelBased);
        }

        return (null, IlvlSyncType.Strict);
    }
}
