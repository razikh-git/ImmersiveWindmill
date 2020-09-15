using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;

using Netcode;
using StardewValley.Network;
using StardewValley.Objects;

namespace ImmersiveWindmill
{
	public class MillInterior : GameLocation
	{
		public Mill Mill;
		public NetStringDictionary<Chest, NetRef<Chest>> MillHopperInputs = new NetStringDictionary<Chest, NetRef<Chest>>();

		public MillInterior() {}

		public MillInterior(string map, string name)
			: base(map, name)
		{
			NetFields.AddFields(MillHopperInputs);
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			base.UpdateWhenCurrentLocation(time);
		}

		public override void hostSetup()
		{
			Mill ??= (Mill) ((Farm) Game1.getLocationFromName("Farm")).getBuildingAt(ModEntry.LastMillUsed);
			Mill ??= ((Farm) Game1.getLocationFromName("Farm")).buildings.OfType<Mill>().FirstOrDefault();
			if (Mill == null)
				Log.D($"Warped to {Name}: No mill was found on the farm.",
					ModEntry.Instance.Config.DebugMode);
			Mill ??= new Mill();
			ModEntry.Instance.Helper.Events.GameLoop.UpdateTicked += GameLoopOnUpdateTicked;

			// Set contextual map tiles:
			MillHopperInputs[Mill.nameOfIndoors] ??= new Chest();

			// Hopper has items waiting to be milled
			if (!MillHopperInputs[Mill.nameOfIndoors].items.Any())
				for (var x = 5; x < 7; ++x)
				for (var y = 5; y < 7; ++y)
					Game1.currentLocation.Map.GetLayer("AboveBuildings")
						.Tiles[x, y].TileIndex = 1;

			// Mill building is currently milling items
			if (Mill != null && Mill.input.Value.items.Any())
				return;
			var contextualTiles = new Dictionary<Rectangle, string>
			{
				// Millstones
				{new Rectangle(7, 35, 3, 2), "Front"},
				// Flour chute
				{new Rectangle(7, 60, 2, 1), "AlwaysFront"},
				{new Rectangle(7, 61, 2, 2), "Front"},
				{new Rectangle(7, 63, 1, 1), "AboveBuildings"},
			};
			foreach (var tiles in contextualTiles)
				for (var x = tiles.Key.X; x < tiles.Key.X + tiles.Key.Width; ++x)
				for (var y = tiles.Key.Y; y < tiles.Key.Y + tiles.Key.Height; ++y)
					Game1.currentLocation.Map.GetLayer(tiles.Value)
						.Tiles[x, y].TileIndex = 1;
		}
		
		public override void cleanupBeforePlayerExit()
		{
			ModEntry.Instance.Helper.Events.GameLoop.UpdateTicked -= GameLoopOnUpdateTicked;
			base.cleanupBeforePlayerExit();
		}

		private void GameLoopOnUpdateTicked(object sender, UpdateTickedEventArgs e)
		{
			if (!ModEntry.Instance.Config.MillsMakeSounds || !e.IsMultipleOf(145) || !Game1.game1.IsActive || Game1.fadeIn)
				return;

			Game1.playSound(ModEntry.AssetPrefix + "windmill_ambient_"
				+ (Game1.player.getTileY() < ModEntry.RoomHeight
					? "loud"
					: Game1.player.getTileY() < ModEntry.RoomHeight * 2 + ModEntry.RoomSpacing
						? "normal"
						: "quiet"));
		}

		public override void draw(SpriteBatch b)
		{
			base.draw(b);

			// Draw the usual Mill produce bubble when there's items in the output collection bin
			if (Mill == null || !Mill.output.Value.items.Any())
				return;

			var position = new Vector2(Map.DisplayWidth - 7, Map.DisplayHeight - 7);
			var yOffset = 4f * (float)Math.Round(Math.Sin(DateTime.Now.TimeOfDay.TotalMilliseconds / 250.0), 2);

			// Produce-completed bubble
			b.Draw(
				Game1.mouseCursors,
				Game1.GlobalToLocal(Game1.viewport, new Vector2(
					position.X * 64 + 192,
					position.Y * 64 - 96 + yOffset)),
				new Rectangle(141, 465, 20, 24),
				Color.White * 0.75f,
				0f,
				Vector2.Zero,
				4f,
				SpriteEffects.None,
				((Mill.tileY.Value + 1) * 64) / 10000f + 1f / 10000f + Mill.tileX.Value / 10000f);

			// Produce icon
			b.Draw(
				Game1.objectSpriteSheet,
				Game1.GlobalToLocal(Game1.viewport, new Vector2(
					position.X * 64 + 192 + 32 + 4,
					position.Y * 64 - 64 + 8 + yOffset)),
				Game1.getSourceRectForStandardTileSheet(
					Game1.objectSpriteSheet,
					Mill.output.Value.items[0].ParentSheetIndex,
					16, 16),
				Color.White * 0.75f,
				0f,
				new Vector2(8f, 8f),
				4f,
				SpriteEffects.None,
				((Mill.tileY.Value + 1) * 64) / 10000f + 1f / 10000f + Mill.tileX.Value / 10000f);
		}
	}
}
