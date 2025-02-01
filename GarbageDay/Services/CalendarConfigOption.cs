using System.Globalization;
using LeFauxMods.Common.Integrations.GenericModConfigMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Menus;

namespace LeFauxMods.GarbageDay.Services;

internal sealed class CalendarConfigOption : ComplexOption
{
    private const int Scale = Game1.pixelZoom;

    private static readonly Rectangle SourceRect = new(37, 231, 225, 145);

    private readonly HashSet<int> days;
    private readonly IModHelper helper;
    private readonly List<ClickableComponent> slots = [];
    private readonly Texture2D texture;

    private bool? change;

    public CalendarConfigOption(IModHelper helper, HashSet<int> days)
    {
        this.helper = helper;
        this.days = days;
        this.texture = helper.GameContent.Load<Texture2D>("LooseSprites/Billboard");

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                var slot = new ClickableComponent(
                    new Rectangle(
                        Scale * (1 + (32 * x)),
                        Scale * (17 + (32 * y)),
                        31 * Scale,
                        31 * Scale),
                    (this.slots.Count + 1).ToString(CultureInfo.InvariantCulture));

                this.slots.Add(slot);
            }
        }
    }

    /// <inheritdoc />
    public override int Height => 145 * Scale;

    /// <inheritdoc />
    public override string Name => string.Empty;

    /// <inheritdoc />
    public override string Tooltip => string.Empty;

    public override void Draw(SpriteBatch spriteBatch, Vector2 pos)
    {
        pos.X -= SourceRect.Width * Scale / 2f;
        var (originX, originY) = pos.ToPoint();
        var (mouseX, mouseY) = this.helper.Input.GetCursorPosition().GetScaledScreenPixels().ToPoint();

        mouseX -= originX;
        mouseY -= originY;

        var mouseLeft = this.helper.Input.GetState(SButton.MouseLeft);
        var controllerA = this.helper.Input.GetState(SButton.ControllerA);
        var held = mouseLeft is SButtonState.Held || controllerA is SButtonState.Held;
        var pressed = mouseLeft is SButtonState.Pressed || controllerA is SButtonState.Pressed;

        if (!held && !pressed)
        {
            this.change = null;
        }

        spriteBatch.Draw(
            this.texture,
            pos,
            SourceRect,
            Color.White,
            0f,
            Vector2.Zero,
            Vector2.One * Scale,
            SpriteEffects.None,
            1f);

        for (var i = 0; i < this.slots.Count; i++)
        {
            var slot = this.slots[i];
            if (!int.TryParse(slot.name, out var day))
            {
                continue;
            }

            if ((held || pressed) && slot.bounds.Contains(mouseX, mouseY))
            {
                this.change ??= this.days.Add(day);
                switch (this.change)
                {
                    case true:
                        this.days.Add(day);
                        break;
                    case false:
                        this.days.Remove(day);
                        break;
                }
            }

            if (!this.days.Contains(day))
            {
                continue;
            }

            spriteBatch.Draw(
                Game1.mouseCursors,
                pos + slot.bounds.Center.ToVector2(),
                new Rectangle(323, 433, 9, 10),
                Color.White,
                0f,
                new Vector2(4.5f, 5f),
                Vector2.One * Scale,
                SpriteEffects.None,
                1f);
        }
    }
}