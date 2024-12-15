namespace LeFauxMods.GarbageDay;

using Common.Integrations.GenericModConfigMenu;
using Common.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley.Characters;
using StardewValley.Extensions;
using StardewValley.GameData.GarbageCans;
using StardewValley.Menus;
using StardewValley.Objects;
using xTile;
using xTile.Dimensions;

/// <inheritdoc />
internal sealed class ModEntry : Mod
{
    private readonly Dictionary<IAssetName, Dictionary<Vector2, string>> allCans = new();
    private readonly PerScreen<NPC?> currentNpc = new();
    private readonly HashSet<string> lootAdded = new(StringComparer.OrdinalIgnoreCase);

    private ModConfig config = null!;
    private IReflectedField<Multiplayer> multiplayer = null!;

    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        // Init
        I18n.Init(this.Helper.Translation);
        this.config = this.Helper.ReadConfig<ModConfig>();
        this.multiplayer = this.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer");

        // Events
        this.Helper.Events.Content.AssetRequested += this.OnAssetRequested;
        this.Helper.Events.Content.AssetsInvalidated += this.OnAssetsInvalidated;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;
        this.Helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;

        if (!Context.IsMainPlayer)
        {
            return;
        }

        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(Constants.IconPath))
        {
            e.LoadFromModFile<Texture2D>("assets/icons.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.DataType != typeof(Map))
        {
            return;
        }

        e.Edit(asset =>
        {
            var map = asset.AsMap().Data;
            var layer = map.GetLayer("Buildings");
            var frontLayer = map.GetLayer("Front");
            var backLayer = map.GetLayer("Back");

            for (var x = 0; x < layer.LayerWidth; x++)
            {
                for (var y = 0; y < layer.LayerHeight; y++)
                {
                    var tileLocation = new Location(x, y) * Game1.tileSize;
                    var tile = layer.PickTile(tileLocation, Game1.viewport.Size);
                    if (tile is null
                        || !tile.Properties.TryGetValue("Action", out var property)
                        || string.IsNullOrWhiteSpace(property))
                    {
                        continue;
                    }

                    var parts = ArgUtility.SplitBySpace(property);
                    if (parts.Length < 2
                        || !parts[0].Equals("Garbage", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        continue;
                    }

                    Log.Trace("Garbage can {0} found on map {1} at ({2}, {3}).", parts[1], asset.NameWithoutLocale, x,
                        y);

                    // Remove base tile
                    layer.Tiles[x, y] = null;

                    // Remove lid tile
                    frontLayer.Tiles[x, y - 1] = null;

                    // Add NoPath to tile
                    backLayer.PickTile(tileLocation, Game1.viewport.Size)?.Properties.Add("NoPath", string.Empty);

                    // Add garbage can
                    if (!this.allCans.TryGetValue(asset.NameWithoutLocale, out var cansInLocation))
                    {
                        cansInLocation = new Dictionary<Vector2, string>();
                        this.allCans[asset.NameWithoutLocale] = cansInLocation;
                    }

                    cansInLocation[new Vector2(x, y)] = parts[1];
                }
            }
        }, (AssetEditPriority)int.MaxValue);
    }

    private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e) =>
        this.allCans.RemoveWhere(kvp => e.NamesWithoutLocale.Any(assetName => assetName.IsEquivalentTo(kvp.Key)));

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsPlayerFree
            || !e.Button.IsActionButton()
            || !Game1.currentLocation.Objects.TryGetValue(e.Cursor.GrabTile, out var obj)
            || obj is not Chest chest
            || chest.GlobalInventoryId?.StartsWith(Constants.GlobalInventoryPrefix,
                StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        this.Helper.Input.Suppress(e.Button);
        var character = Utility.isThereAFarmerOrCharacterWithinDistance(e.Cursor.GrabTile, 7, Game1.currentLocation);
        if (character is NPC npc and not Horse)
        {
            // Queue up NPC response
            this.currentNpc.Value = npc;
            this.multiplayer.GetValue()
                .globalChatInfoMessage("TrashCan", Game1.player.Name, npc.GetTokenizedDisplayName());
            if (npc.Name.Equals("Linus", StringComparison.OrdinalIgnoreCase))
            {
                npc.doEmote(32);
                npc.setNewDialogue("Data\\ExtraDialogue:Town_DumpsterDiveComment_Linus", true, true);

                Game1.player.changeFriendship(5, npc);
                this.multiplayer.GetValue().globalChatInfoMessage("LinusTrashCan");
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

        if (!chest.modData.ContainsKey(Constants.ModDataChecked))
        {
            chest.modData[Constants.ModDataChecked] = "true";
            Game1.stats.Increment("trashCansChecked");
        }

        var items = chest.GetItemsForPlayer();
        var specialItem = items.FirstOrDefault(item => item.modData.ContainsKey(Constants.ModDataSpecialItem));

        // Drop Item
        switch (specialItem?.QualifiedItemId)
        {
            case "(O)890":
                var origin = Game1.tileSize * (chest.TileLocation + new Vector2(0.5f, -1));
                Game1.createItemDebris(specialItem, origin, 2, chest.Location, (int)origin.Y + Game1.tileSize);
                items.Remove(specialItem);
                chest.playerChoiceColor.Value = Color.DarkGray;
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
                        Game1.playSound(Constants.TrashCanSound);
                        chest.ShowMenu();
                    });
                return;
            default:
                chest.playerChoiceColor.Value = Color.DarkGray;
                break;
        }

        // Play sound
        if (chest.modData.TryGetValue(Constants.ModDataPlaySound, out var sound))
        {
            chest.Location.playSound(sound);
            chest.modData.Remove(Constants.ModDataPlaySound);
        }

        // Add special item to player inventory
        Game1.player.addItemByMenuIfNecessary(specialItem);
        items.Remove(specialItem);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.lootAdded.Clear();

        // Clear all garbage cans
        Utility.ForEachLocation(location =>
        {
            var assetName = this.Helper.GameContent.ParseAssetName(location.mapPath.Value);
            if (!this.allCans.TryGetValue(assetName, out var cansInLocation))
            {
                return true;
            }

            foreach (var (pos, whichCan) in cansInLocation)
            {
                if (!location.Objects.TryGetValue(pos, out var obj) || obj is not Chest)
                {
                    continue;
                }

                Log.Trace("Removing garbage can {0} at {1} ({2})", whichCan, location.Name, pos);
                location.Objects.Remove(pos);
            }

            return true;
        });

        // Clear all items
        foreach (var whichCan in DataLoader.GarbageCans(Game1.content).GarbageCans.Keys)
        {
            var items = Game1.player.team.GetOrCreateGlobalInventory(Constants.GlobalInventoryPrefix + whichCan);

            // If garbage day clear all items
            if (Game1.dayOfMonth % 7 == (int)this.config.GarbageDay % 7)
            {
                items.Clear();
                continue;
            }

            // Always clear the special items
            items.RemoveWhere(item => item.modData.ContainsKey(Constants.ModDataSpecialItem));
            items.RemoveEmptySlots();
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e) =>
        Utility.ForEachLocation(location =>
        {
            var assetName = this.Helper.GameContent.ParseAssetName(location.mapPath.Value);
            if (!this.allCans.TryGetValue(assetName, out var cansInLocation))
            {
                return true;
            }

            foreach (var (pos, whichCan) in cansInLocation)
            {
                if (!location.Objects.TryGetValue(pos, out var obj))
                {
                    Log.Trace("Placing garbage can {0} at {1} ({2})", whichCan, location.Name, pos);

                    var garbageCanItem = (SObject)ItemRegistry.Create($"(BC){Constants.ItemId}");
                    if (!garbageCanItem.placementAction(
                            location,
                            (int)pos.X * Game1.tileSize,
                            (int)pos.Y * Game1.tileSize,
                            Game1.player)
                        || !location.Objects.TryGetValue(pos, out obj))
                    {
                        Log.Trace("Failed to place garbage can {0} at {1} ({2})", whichCan, location.Name, pos);
                        continue;
                    }
                }

                if (obj is not Chest chest ||
                    !chest.ItemId.Equals(Constants.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Trace("Unrecognized object at garbage can location {0} ({1})", location.Name, pos);
                    continue;
                }

                chest.GlobalInventoryId = Constants.GlobalInventoryPrefix + whichCan;
                chest.playerChoiceColor.Value = Color.DarkGray;

                if (!this.lootAdded.Add(whichCan))
                {
                    continue;
                }

                Log.Trace("Adding loot to garbage can at {0} ({1})", location.Name, pos);
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
                    item.modData[Constants.ModDataSpecialItem] = "true";
                    chest.addItem(item);
                    if (this.config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                    }

                    continue;
                }

                if (selected.IsDoubleMegaSuccess)
                {
                    chest.modData[Constants.ModDataPlaySound] = Constants.DoubleMegaSound;
                    if (this.config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                    }
                }
                else if (selected.IsMegaSuccess)
                {
                    chest.modData[Constants.ModDataPlaySound] = Constants.MegaSound;
                    if (this.config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                    }
                }

                if (selected.AddToInventoryDirectly)
                {
                    Log.Trace("Special loot was selected {0}", item.Name);
                    item.modData[Constants.ModDataSpecialItem] = "true";
                    chest.addItem(item);
                    if (this.config.EnablePrismatic)
                    {
                        chest.playerChoiceColor.Value = DiscreteColorPicker.getColorFromSelection(1);
                    }

                    continue;
                }

                Log.Trace("Normal loot was selected {0}", item.Name);
                chest.addItem(item);

                // Update color
                var colors = chest.GetItemsForPlayer().Select(ItemContextTagManager.GetColorFromTags).OfType<Color>()
                    .ToList();
                if (!colors.Any())
                {
                    continue;
                }

                var index = garbageRandom.Next(colors.Count);
                chest.playerChoiceColor.Value = colors[index];
            }

            return true;
        });

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var gmcm = new GenericModConfigMenuIntegration(this.ModManifest, this.Helper.ModRegistry);
        if (!gmcm.IsLoaded)
        {
            return;
        }

        var tempConfig = new ModConfig();
        gmcm.Register(Reset, Save);

        gmcm.Api.AddBoolOption(
            this.ModManifest,
            () => tempConfig.EnablePrismatic,
            value => tempConfig.EnablePrismatic = value,
            I18n.Config_EnablePrismatic_Name,
            I18n.Config_EnablePrismatic_Description);

        gmcm.Api.AddNumberOption(
            this.ModManifest,
            () => (int)tempConfig.GarbageDay,
            value => tempConfig.GarbageDay = (DayOfWeek)value,
            I18n.Config_GarbageDay_Name,
            I18n.Config_GarbageDay_Description,
            0,
            6,
            1,
            day => day switch
            {
                0 => I18n.Config_GarbageDay_Sunday(),
                1 => I18n.Config_GarbageDay_Monday(),
                2 => I18n.Config_GarbageDay_Tuesday(),
                3 => I18n.Config_GarbageDay_Wednesday(),
                4 => I18n.Config_GarbageDay_Thursday(),
                5 => I18n.Config_GarbageDay_Friday(),
                6 => I18n.Config_GarbageDay_Saturday(),
                _ => string.Empty
            });

        void Reset()
        {
            tempConfig = this.Helper.ReadConfig<ModConfig>();
        }

        void Save()
        {
            this.Helper.WriteConfig(tempConfig);
            this.config = this.Helper.ReadConfig<ModConfig>();
        }
    }

    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (this.currentNpc.Value is null || e.OldMenu is not ItemGrabMenu { context: Chest chest } ||
            !chest.GlobalInventoryId.StartsWith(Constants.GlobalInventoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Game1.drawDialogue(this.currentNpc.Value);
        this.currentNpc.Value = null;
    }
}
