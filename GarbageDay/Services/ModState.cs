using LeFauxMods.Common.Services;
using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley.Inventories;
using xTile;

namespace LeFauxMods.GarbageDay.Services;

/// <summary>Responsible for managing state.</summary>
internal sealed class ModState
{
    private static ModState? Instance;

    private readonly ConfigHelper<ModConfig> configHelper;
    private readonly PerScreen<NPC?> currentNpc = new();
    private readonly Dictionary<IAssetName, Action<Map>> mapEdits = [];
    private readonly IReflectedField<Multiplayer> multiplayer;
    private readonly Dictionary<IAssetName, Dictionary<Vector2, string>?> processedLocations = [];

    private Inventory? allCans;

    private ModState(IModHelper helper)
    {
        this.configHelper = new ConfigHelper<ModConfig>(helper);
        this.multiplayer = helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer");
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
    }

    public static Inventory AllCans => Instance!.allCans ??=
        Game1.player.team.GetOrCreateGlobalInventory(ModConstants.GlobalInventoryId);

    public static ModConfig Config => Instance!.configHelper.Config;

    public static ConfigHelper<ModConfig> ConfigHelper => Instance!.configHelper;

    public static Dictionary<IAssetName, Action<Map>> MapEdits => Instance!.mapEdits;

    public static Multiplayer Multiplayer => Instance!.multiplayer.GetValue();

    public static Dictionary<IAssetName, Dictionary<Vector2, string>?> ProcessedLocations =>
        Instance!.processedLocations;

    public static NPC? CurrentNpc
    {
        get => Instance!.currentNpc.Value;
        set => Instance!.currentNpc.Value = value;
    }

    public static void Init(IModHelper helper) => Instance ??= new ModState(helper);

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.allCans = null;
        this.processedLocations.Clear();
    }
}