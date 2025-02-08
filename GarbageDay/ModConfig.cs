using System.Globalization;
using System.Text;
using LeFauxMods.Common.Interface;
using LeFauxMods.Common.Models;

namespace LeFauxMods.GarbageDay;

/// <inheritdoc cref="IModConfig{TConfig}" />
internal sealed class ModConfig : IModConfig<ModConfig>, IConfigWithLogAmount
{
    /// <summary>Gets or sets a value indicating whether to enable prismatic colors when a special item is added.</summary>
    public bool EnablePrismatic { get; set; } = true;

    /// <summary>Gets or sets garbage can ids to exclude.</summary>
    public HashSet<string> ExcludedGarbage { get; set; } = ["DesertFestival"];

    /// <summary>Gets or sets the days that garbage is collected.</summary>
    public HashSet<int> GarbageDays { get; set; } = [7, 14, 21, 28];

    /// <summary>Gets or sets a value indicating whether festival days will be skipped.</summary>
    public bool SkipFestival { get; set; } = true;

    /// <inheritdoc />
    public LogAmount LogAmount { get; set; } = LogAmount.Less;

    /// <inheritdoc />
    public void CopyTo(ModConfig other)
    {
        other.EnablePrismatic = this.EnablePrismatic;
        other.GarbageDays.Clear();
        other.GarbageDays.UnionWith(this.GarbageDays);
        other.LogAmount = this.LogAmount;
    }

    /// <inheritdoc />
    public string GetSummary() =>
        new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"{nameof(this.EnablePrismatic),25}: {this.EnablePrismatic}")
            .AppendLine(CultureInfo.InvariantCulture,
                $"{nameof(this.ExcludedGarbage),25}: {string.Join(',', this.ExcludedGarbage)}")
            .AppendLine(CultureInfo.InvariantCulture,
                $"{nameof(this.GarbageDays),25}: {string.Join(',', this.GarbageDays)}")
            .AppendLine(CultureInfo.InvariantCulture, $"{nameof(this.SkipFestival),25}: {this.SkipFestival}")
            .ToString();
}