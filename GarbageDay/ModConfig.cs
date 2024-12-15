namespace LeFauxMods.GarbageDay;

/// <summary>Mod config data for Garbage Day.</summary>
internal sealed class ModConfig
{
    /// <summary>Gets or sets a value indicating whether to enable prismatic colors when a special item is added.</summary>
    public bool EnablePrismatic { get; set; } = true;

    /// <summary>Gets or sets the day of the week that garbage is collected.</summary>
    public DayOfWeek GarbageDay { get; set; }
}
