using LeFauxMods.Common.Integrations.GenericModConfigMenu;
using LeFauxMods.Common.Services;

namespace LeFauxMods.GarbageDay.Services;

/// <summary>Responsible for handling the mod configuration menu.</summary>
internal sealed class ConfigMenu
{
    private readonly IGenericModConfigMenuApi api = null!;
    private readonly GenericModConfigMenuIntegration gmcm;
    private readonly IModHelper helper;
    private readonly IManifest manifest;

    public ConfigMenu(IModHelper helper, IManifest manifest)
    {
        this.helper = helper;
        this.manifest = manifest;
        this.gmcm = new GenericModConfigMenuIntegration(manifest, helper.ModRegistry);
        if (!this.gmcm.IsLoaded)
        {
            return;
        }

        this.api = this.gmcm.Api;
        this.SetupMenu();
    }

    private static ModConfig Config => ModState.ConfigHelper.Temp;

    private static ConfigHelper<ModConfig> ConfigHelper => ModState.ConfigHelper;

    private void SetupMenu()
    {
#if DEBUG
        this.gmcm.Register(() =>
        {
            this.SetupMenu();
            ConfigHelper.Reset();
        }, ConfigHelper.Save);
#else
        this.gmcm.Register(ConfigHelper.Reset, ConfigHelper.Save);
#endif

        this.api.AddBoolOption(
            this.manifest,
            static () => Config.EnablePrismatic,
            static value => Config.EnablePrismatic = value,
            I18n.ConfigOption_EnablePrismatic_Name,
            I18n.ConfigOption_EnablePrismatic_Description);

        this.api.AddBoolOption(
            this.manifest,
            static () => Config.SkipFestival,
            static value => Config.SkipFestival = value,
            I18n.ConfigOption_SkipFestival_Name,
            I18n.ConfigOption_SkipFestival_Description);

        this.api.AddTextOption(
            this.manifest,
            static () => string.Join(',', Config.ExcludedGarbage),
            static value => Config.ExcludedGarbage = [..value.Split(',')],
            I18n.ConfigOption_ExcludedGarbage_Name,
            I18n.ConfigOption_ExcludedGarbage_Descriptions);

        this.gmcm.AddComplexOption(new CalendarConfigOption(this.helper, Config.GarbageDays));
    }
}