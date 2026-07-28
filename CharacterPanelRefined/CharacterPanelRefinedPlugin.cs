using System.Globalization;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CharacterPanelRefined;

public class CharacterPanelRefinedPlugin : IDalamudPlugin {
    internal Configuration Configuration { get; }
    internal GameFunctions GameFunctions { get; }
    internal bool CtrlHeld { get; private set; }

    private readonly CharacterStatusAugments characterStatusAugments;
    private readonly ItemTooltipAugments itemTooltipAugments;
    private readonly ConfigWindow configWindow;

    public CharacterPanelRefinedPlugin(IDalamudPluginInterface pluginInterface) {
        Service.Initialize(pluginInterface);

        Configuration = Configuration.Get(pluginInterface);
        configWindow = new ConfigWindow(this, pluginInterface);
        GameFunctions = new GameFunctions();
        characterStatusAugments = new CharacterStatusAugments(this);
        itemTooltipAugments = new ItemTooltipAugments(this);

        UpdateLanguage();

        Service.Framework.Update += FrameworkOnUpdate;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CharacterStatus", characterStatusAugments.OnSetup);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "CharacterStatus", (_, a) => characterStatusAugments.RequestedUpdate(a.Addon));
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "ItemDetail", itemTooltipAugments.RequestedUpdate);
    }

    internal void UpdateLanguage() {
        var lang = "";

        if (Configuration.UseGameLanguage) {
            lang = Service.ClientState.ClientLanguage switch {
                ClientLanguage.English => "",
                ClientLanguage.French => "fr",
                ClientLanguage.German => "de",
                ClientLanguage.Japanese => "ja",
                // TC(台服)客戶端在 Dalamud 13.0.0.16 之後回報 ClientLanguage 7(TraditionalChinese),
                // 舊版回報 4(ChineseSimplified)。用數值比較才能同時相容 CI 釘的 13.0.0.6(列舉沒有 7 這個名字)與執行期新版。
                (ClientLanguage)4 or (ClientLanguage)5 or (ClientLanguage)7 => "zh-Hant",
                _ => ""
            };
        }

        Localization.Culture = new CultureInfo(lang);

        characterStatusAugments.ReloadLocs();
    }

    private void FrameworkOnUpdate(IFramework framework) {
        var ctrlState = Service.KeyState[VirtualKey.CONTROL];
        if (ctrlState == CtrlHeld)
            return;
        CtrlHeld = ctrlState;
        characterStatusAugments.Update();
    }

    public void Dispose() {
        configWindow.Dispose();
        characterStatusAugments.Dispose();
    }
}
