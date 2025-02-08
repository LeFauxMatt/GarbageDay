using LeFauxMods.Common.Utilities;
using LeFauxMods.GarbageDay.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewValley.Characters;
using StardewValley.Extensions;
using StardewValley.GameData.GarbageCans;
using StardewValley.Menus;
using StardewValley.Objects;
using xTile;
using xTile.Dimensions;

namespace LeFauxMods.GarbageDay;

/// <inheritdoc />
internal sealed class ModEntry : Mod
{
    private bool wasFestival;

    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        // Init
        I18n.Init(helper.Translation);
        ModState.Init(helper);
        Log.Init(this.Monitor, ModState.Config);

        // Events
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;

        if (!Context.IsMainPlayer)
        {
            return;
        }

        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
    }

    private static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ModConstants.IconPath))
        {
            e.LoadFromModFile<Texture2D>("assets/icons.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (!ModState.MapEdits.TryGetValue(e.NameWithoutLocale, out var mapEdit) || e.DataType != typeof(Map))
        {
            return;
        }

        ModState.MapEdits.Remove(e.NameWithoutLocale);
        e.Edit(asset =>
        {
            var map = asset.AsMap().Data;
            mapEdit(map);
        }, (AssetEditPriority)int.MaxValue);
    }

    private static void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        foreach (var assetName in e.NamesWithoutLocale)
        {
            if (ModState.ProcessedLocations.ContainsKey(assetName) && !ModState.MapEdits.ContainsKey(assetName))
            {
                ModState.ProcessedLocations.Remove(assetName);
            }
        }
    }

    private static void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (ModState.CurrentNpc is null || e.OldMenu is not ItemGrabMenu { context: Chest chest } ||
            !chest.GlobalInventoryId.StartsWith(ModConstants.GlobalInventoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Game1.drawDialogue(ModState.CurrentNpc);
        ModState.CurrentNpc = null;
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Helper.Events.Display.MenuChanged += OnMenuChanged;
        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;

        if (!Context.IsMainPlayer)
        {
            return;
        }

        this.wasFestival = Utility.isFestivalDay(
            Game1.dayOfMonth == 1 ? 28 : Game1.dayOfMonth - 1,
            Game1.dayOfMonth == 1 && Game1.seasonIndex == 0 ? Season.Winter : (Season)(Game1.seasonIndex - 1));
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Helper.Events.Display.MenuChanged -= OnMenuChanged;
        this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsPlayerFree
            || !e.Button.IsActionButton()
            || !Game1.currentLocation.Objects.TryGetValue(e.Cursor.GrabTile, out var obj)
            || obj is not Chest chest
            || chest.GlobalInventoryId?.StartsWith(ModConstants.GlobalInventoryPrefix,
                StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        this.Helper.Input.Suppress(e.Button);
        var character = Utility.isThereAFarmerOrCharacterWithinDistance(e.Cursor.GrabTile, 7, Game1.currentLocation);
        if (character is NPC npc and not Horse)
        {
            // Queue up NPC response
            ModState.CurrentNpc = npc;
            ModState.Multiplayer.globalChatInfoMessage("TrashCan", Game1.player.Name, npc.GetTokenizedDisplayName());
            if (npc.Name.Equals("Linus", StringComparison.OrdinalIgnoreCase))
            {
                npc.doEmote(32);
                npc.setNewDialogue("Data\\ExtraDialogue:Town_DumpsterDiveComment_Linus", true, true);

                Game1.player.changeFriendship(5, npc);
                ModState.Multiplayer.globalChatInfoMessage("LinusTrashCan");
            }
            else
            {
                switch (npc.Age)
                {
                    case 2:
                        npc.doEmote(28);
                        npc.setNewDialogue("Data\\ExtraDialogue:Town_DumpsterDiveComment_Child", true, true);

                        break;

                    case 1:
                        npc.doEmote(8);
                        npc.setNewDialogue("Data\\ExtraDialogue:Town_DumpsterDiveComment_Teen", true, true);

                        break;

                    default:
                        npc.doEmote(12);
                        npc.setNewDialogue("Data\\ExtraDialogue:Town_DumpsterDiveComment_Adult", true, true);

                        break;
                }

                Game1.player.changeFriendship(-25, npc);
            }
        }

        if (!chest.modData.ContainsKey(ModConstants.ModDataChecked))
        {
            chest.modData[ModConstants.ModDataChecked] = "true";
            _ = Game1.stats.Increment("trashCansChecked");
        }

        var items = chest.GetItemsForPlayer();
        var specialItem =
            items.FirstOrDefault(static item => item.modData.ContainsKey(ModConstants.ModDataSpecialItem));

        // Drop Item
        switch (specialItem?.QualifiedItemId)
        {
            case "(O)890":
                var origin = Game1.tileSize * (chest.TileLocation + new Vector2(0.5f, -1));
                _ = Game1.createItemDebris(specialItem, origin, 2, chest.Location, (int)origin.Y + Game1.tileSize);
                _ = items.Remove(specialItem);
                chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                chest.shakeTimer = 0;
                return;
            case "(H)66":
                // Change texture to lidless sprite
                chest.playerChoiceColor.Value = Color.Black;
                break;
            case null:
                // Open regular chest
                chest.GetMutex()
                    .RequestLock(() =>
                    {
                        _ = Game1.playSound(ModConstants.TrashCanSound);
                        chest.ShowMenu();
                    });
                return;
            default:
                chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(20);
                break;
        }

        // Play sound
        if (chest.modData.TryGetValue(ModConstants.ModDataPlaySound, out var sound))
        {
            chest.Location.playSound(sound);
            _ = chest.modData.Remove(ModConstants.ModDataPlaySound);
        }

        // Add special item to player inventory
        Game1.player.addItemByMenuIfNecessary(specialItem);
        _ = items.Remove(specialItem);
        chest.shakeTimer = 0;
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        // Reset loot state
        foreach (var chest in ModState.AllCans.OfType<Chest>())
        {
            var items = chest.GetItemsForPlayer();
            chest.modData.Remove(ModConstants.ModDataChecked);

            // If garbage day clear all items
            if (this.wasFestival || ModState.Config.GarbageDays.Contains(Game1.dayOfMonth))
            {
                if (ModState.Config.SkipFestival && Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
                {
                    this.wasFestival = true;
                }
                else
                {
                    items.Clear();
                    this.wasFestival = false;
                    continue;
                }
            }

            // Always clear special items
            _ = items.RemoveWhere(static item => item.modData.ContainsKey(ModConstants.ModDataSpecialItem));
            items.RemoveEmptySlots();
        }

        // Clear all garbage cans
        Utility.ForEachLocation(location =>
        {
            var assetName = this.Helper.GameContent.ParseAssetName(location.mapPath.Value);
            if (!ModState.ProcessedLocations.TryGetValue(assetName, out var foundCans) || foundCans is null)
            {
                return true;
            }

            foreach (var (pos, whichCan) in foundCans)
            {
                if (!location.Objects.TryGetValue(pos, out var obj) || obj is not Chest)
                {
                    continue;
                }

                Log.Trace("Removing garbage can {0} at {1} ({2}, {3}).",
                    whichCan,
                    location.DisplayName,
                    (int)pos.X,
                    (int)pos.Y);

                _ = location.Objects.Remove(pos);
            }

            return true;
        });
    }

    private Dictionary<Vector2, string>? ProcessLocation(GameLocation location, IAssetName assetName)
    {
        if (ModState.ProcessedLocations.TryGetValue(assetName, out var foundCans))
        {
            return foundCans;
        }

        ModState.ProcessedLocations.Add(assetName, null);

        var layer = location.map.GetLayer("Buildings");
        if (layer is null)
        {
            return null;
        }

        for (var x = 0; x < layer.LayerWidth; x++)
        {
            for (var y = 0; y < layer.LayerHeight; y++)
            {
                var tile = new Location(x * Game1.tileSize, y * Game1.tileSize);
                if (layer.PickTile(tile, Game1.viewport.Size)?.Properties.TryGetValue("Action", out var property) !=
                    true ||
                    property is null ||
                    string.IsNullOrWhiteSpace(property))
                {
                    continue;
                }

                var action = ArgUtility.SplitBySpace(property);
                if (!ArgUtility.TryGet(action, 0, out var actionType, out _, true, "string actionType") ||
                    actionType != "Garbage" ||
                    !ArgUtility.TryGet(action, 1, out var id, out _, true, "string id") ||
                    string.IsNullOrWhiteSpace(id) ||
                    ModState.Config.ExcludedGarbage.Contains(id))
                {
                    continue;
                }

                Log.Trace("Garbage can {0} found at {1} ({2}, {3}).", id, location.DisplayName, x, y);
                foundCans ??= new Dictionary<Vector2, string>();
                ModState.ProcessedLocations[assetName] ??= foundCans;
                foundCans.Add(new Vector2(x, y), id);
            }
        }

        if (foundCans?.Any() != true)
        {
            return foundCans;
        }

        // Queue map edits
        ModState.MapEdits.Add(
            assetName,
            map =>
            {
                foreach (var pos in foundCans.Keys)
                {
                    // Remove base tile
                    try
                    {
                        map.GetLayer("Buildings").Tiles[(int)pos.X, (int)pos.Y] = null;
                    }
                    catch
                    {
                        // ignored
                    }

                    // Remove lid tile
                    try
                    {
                        map.GetLayer("Front").Tiles[(int)pos.X, (int)pos.Y - 1] = null;
                    }
                    catch
                    {
                        // ignored
                    }

                    // Add NoPath to tile
                    try
                    {
                        map.GetLayer("Back")
                            .PickTile(new Location((int)pos.X * Game1.tileSize, (int)pos.Y * Game1.tileSize),
                                Game1.viewport.Size)?.Properties
                            .Add("NoPath", string.Empty);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            });

        this.Helper.GameContent.InvalidateCache(assetName);
        return foundCans;
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        Utility.ForEachLocation(location =>
        {
            var assetName = this.Helper.GameContent.ParseAssetName(location.mapPath.Value);
            var foundCans = this.ProcessLocation(location, assetName);
            if (foundCans is null)
            {
                return true;
            }

            foreach (var (pos, whichCan) in foundCans)
            {
                if (!location.Objects.TryGetValue(pos, out var obj))
                {
                    Log.Trace("Placing garbage can {0} at {1} ({2})", whichCan, location.Name, pos);

                    var garbageCanItem = (SObject)ItemRegistry.Create($"(BC){ModConstants.ItemId}");
                    if (!garbageCanItem.placementAction(
                            location,
                            (int)pos.X * Game1.tileSize,
                            (int)pos.Y * Game1.tileSize,
                            Game1.player) ||
                        !location.Objects.TryGetValue(pos, out obj))
                    {
                        Log.Warn("Failed to place garbage can {0} at {1} ({2})", whichCan, location.Name, pos);
                        continue;
                    }
                }

                if (obj is not Chest chest ||
                    !chest.ItemId.Equals(ModConstants.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Unrecognized object {0} found at {1} ({2})", obj.ItemId, location.Name, pos);
                    continue;
                }

                chest.GlobalInventoryId = ModConstants.GlobalInventoryPrefix + whichCan;
                chest.playerChoiceColor.Value = Color.DarkGray;
                chest.modData[ModConstants.ModDataName] = whichCan;

                _ = ModState.AllCans.TryAddBackup(chest, ModConstants.GlobalInventoryPrefix);
                if (!ModState.AllCans.TryGetBackup(chest, out var lootChest) ||
                    lootChest.modData.ContainsKey(ModConstants.ModDataChecked))
                {
                    continue;
                }

                Log.Trace("Adding loot for id {0}", whichCan);
                location.TryGetGarbageItem(
                    whichCan,
                    Game1.player.DailyLuck,
                    out var item,
                    out var selected,
                    out var garbageRandom);

#if DEBUG
                if (whichCan == "Saloon")
                {
                    item = ItemRegistry.Create("(H)66");
                    selected = new GarbageCanItemData
                    {
                        ItemId = "(H)66", IsDoubleMegaSuccess = true, AddToInventoryDirectly = true
                    };
                }
#endif

                if (selected is null)
                {
                    Log.Trace("No loot item selected for today");
                    continue;
                }

                if (selected.ItemId == "(O)890")
                {
                    Log.Trace("Special loot was selected {0}", item.Name);
                    item.modData[ModConstants.ModDataSpecialItem] = "true";
                    chest.addItem(item);
                    if (ModState.Config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                        chest.shakeTimer = int.MaxValue;
                    }

                    continue;
                }

                if (selected.IsDoubleMegaSuccess)
                {
                    chest.modData[ModConstants.ModDataPlaySound] = ModConstants.DoubleMegaSound;
                    if (ModState.Config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                        chest.shakeTimer = int.MaxValue;
                    }
                }
                else if (selected.IsMegaSuccess)
                {
                    chest.modData[ModConstants.ModDataPlaySound] = ModConstants.MegaSound;
                    if (ModState.Config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                        chest.shakeTimer = int.MaxValue;
                    }
                }

                if (selected.AddToInventoryDirectly)
                {
                    Log.Trace("Special loot was selected {0}", item.Name);
                    item.modData[ModConstants.ModDataSpecialItem] = "true";
                    chest.addItem(item);
                    if (ModState.Config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                        chest.shakeTimer = int.MaxValue;
                    }

                    continue;
                }

                Log.Trace("Normal loot was selected {0}", item.Name);
                chest.addItem(item);

                // Update color
                chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(garbageRandom.Next(2, 20));
            }

            return true;
        });
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e) =>
        _ = new ConfigMenu(this.Helper, this.ModManifest);
}