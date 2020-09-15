using System.Linq;

using Microsoft.Xna.Framework;
using xTile;
using xTile.Dimensions;

using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;

namespace ImmersiveWindmill
{
	public class ModEntry : Mod
	{
		internal static ModEntry Instance;
		internal Config Config;
		internal ITranslationHelper i18n => Helper.Translation;

		private const string CmdPrefix = "mill";
		internal const string AssetPrefix = "blueberry.ImmersiveWindmill.";
		private const string ActionLadder = AssetPrefix + "MillLadder";
		private const string ActionGreatWheel = AssetPrefix + "MillGreatWheel";
		private const string ActionHopper = AssetPrefix + "MillHopper";
		private const string ActionLever = AssetPrefix + "MillLever";
		private const string ActionMillstone = AssetPrefix + "MillStone";
		private const string ActionConveyor = AssetPrefix + "MillConveyor";
		private const string ActionFlourBin = AssetPrefix + "MillFlourBin";
		private const string InspectAction = "InspectAction";

		internal static readonly string MillInteriorMapName = AssetPrefix + "MillInterior";
		internal const int RoomHeight = 14;
		internal static int RoomSpacing = 28;

		internal static Vector2 LastMillUsed;

		public override void Entry(IModHelper helper)
		{
			Instance = this;
			Config = helper.ReadConfig<Config>();
			HarmonyPatches.Patch();

			Helper.Events.GameLoop.SaveLoaded += GameLoopOnSaveLoaded;
			Helper.Events.Input.ButtonPressed += InputOnButtonPressed;
			if (Config.DebugMode)
				Helper.ConsoleCommands.Add(
					$"{CmdPrefix}warp",
					"Warp outside a mill on the farm, or inside the MillInterior location if no mills are found.",
					(s, args) =>
					{
						var warp = ((string)Game1.getLocationFromName(MillInteriorMapName).Map
							.Properties["Warp"]).Split(' ');
						var mill = (Mill) Game1.getFarm().buildings.FirstOrDefault(b => b is Mill);
						Game1.player.warpFarmer(new Warp(Game1.player.getTileX(), Game1.player.getTileY(),
							mill == null ? MillInteriorMapName : Game1.getFarm().Name,
							mill == null ? int.Parse(warp[0]) : mill.tileX.Value + 1,
							mill == null ? int.Parse(warp[1]) - 1 : mill.tileY.Value + 2,
							false));
					});
		}

		private void GameLoopOnSaveLoaded(object sender, SaveLoadedEventArgs e)
		{
			PopulateMillsWithInteriors(Config.CheckAllLocationsForMills);
		}

		private void InputOnButtonPressed(object sender, ButtonPressedEventArgs e)
		{
			if (Game1.eventUp && !Game1.currentLocation.currentEvent.playerControlSequence // No event cutscenes
			    || Game1.currentBillboard != 0 || Game1.activeClickableMenu != null || Game1.menuUp // No menus
			    || Game1.nameSelectUp || Game1.IsChatting || Game1.dialogueTyping || Game1.dialogueUp // No text inputs
			    || Game1.player.UsingTool || Game1.pickingTool || Game1.numberOfSelectedItems != -1 // No tools in use
			    || Game1.fadeToBlack
			    || !Game1.player.CanMove
			    || !e.Button.IsActionButton())
				return;

			// Use tile actions in maps
			CheckTileAction(e.Cursor.GrabTile, Game1.currentLocation);

			// Enter mills built on the farm
			var position = e.Cursor.GrabTile;
			if (!Config.MillsHaveInteriors
			    || !(Game1.currentLocation is Farm farm)
			    || !(farm.getBuildingAt(position) is Mill mill)
			    || mill.daysOfConstructionLeft.Value > 0
				//|| mill.output.Value.items.Any()
			)
				return;

			LastMillUsed = new Vector2(mill.tileX.Value, mill.tileY.Value);
			UpdateMillInteriorWarpOutLocation(new Location(Game1.player.getTileX(), Game1.player.getTileY()));
			var warp = ((string)Game1.getLocationFromName(MillInteriorMapName).Map
					.Properties["Warp"]).Split(' ');

			if (warp.Length == 0)
			{
				Log.E($"Failed to patch in warp properties for {MillInteriorMapName} in setup:"
				      + $"\nWarp: {(warp.Length <= 0 ? "null" : warp.Aggregate((a,b) => $"{a} {b}"))}");
			}
			else
			{
				Log.W($"\nWarp: {(warp.Length <= 0 ? "null" : warp.Aggregate((a,b) => $"{a} {b}"))}");
				var targetPosition = new Location(int.Parse(warp[0]), int.Parse(warp[1]));
				Game1.player.warpFarmer(new Warp(Game1.player.getTileX(), Game1.player.getTileY(),
					MillInteriorMapName, targetPosition.X, targetPosition.Y - 1, false));
				Helper.Input.Suppress(e.Button);
			}
		}
		
		private void PopulateMillsWithInteriors(bool isAppliedWorldWide)
		{
			
		}

		public void CheckTileAction(Vector2 position, GameLocation location, string[] forceAction = null)
		{
			var property = location.doesTileHaveProperty(
				(int) position.X, (int) position.Y, "Action", "Buildings");
			if (property == null && forceAction == null)
				return;
			var action = property?.Split(' ') ?? forceAction;
			var millInterior = Game1.currentLocation.Name == MillInteriorMapName ? (MillInterior) Game1.currentLocation : null;
			switch (action[0])
			{
				case ActionHopper:
					Log.W("ActionHopper!");
					// Use the interior mill action as a stage for mill drop-in items
					MillInteriorHopperAction(millInterior.Mill, Game1.player);
					break;

				case ActionLever:
					Log.W("ActionLever!");
					// Add all items from the hopper drop-in as inputs to the mill building

					var oldFacingDirection = Game1.player.FacingDirection;
					var oldPosition = Game1.player.Position;

					// Block player agency
					Helper.Events.Input.ButtonPressed += Input_ButtonPressed_BlockPlayerAgency;
					Game1.player.Position = position * 64f;
					Game1.player.FacingDirection = 1;
					Game1.player.completelyStopAnimatingOrDoingAction();

					Game1.player.FarmerSprite.animateOnce(new[]
					{
						new FarmerSprite.AnimationFrame(6, 500),
						new FarmerSprite.AnimationFrame(32, 500, false, false, who =>
						{
							// Pull lever
							Game1.currentLocation.Map.GetLayer("Front")
								.Tiles[(int) position.X, (int) position.Y - 1].TileIndex += 1;
							Game1.currentLocation.Map.GetLayer("Buildings")
								.Tiles[(int) position.X, (int) position.Y].TileIndex += 1;

							Game1.currentLocation.playSound("openBox");

							//DelayedAction.playSoundAfterDelay("pullItemFromWater", 1000);
						}) //{xOffset = 32}
						,
						new FarmerSprite.AnimationFrame(33, 1000, false, false, who =>
						{
							// Open hopper to grain chute
							Game1.currentLocation.playSound("pullItemFromWater");
							for (var x = 5; x < 7; ++x)
							for (var y = 5; y < 7; ++y)
								Game1.currentLocation.Map.GetLayer("AboveBuildings")
									.Tiles[x, y].TileIndex = 1;

							// Release lever
							Game1.currentLocation.Map.GetLayer("Front")
								.Tiles[(int) position.X, (int) position.Y - 1].TileIndex -= 1;
							Game1.currentLocation.Map.GetLayer("Buildings")
								.Tiles[(int) position.X, (int) position.Y].TileIndex -= 1;

							// Return player agency
							Helper.Events.Input.ButtonPressed -= Input_ButtonPressed_BlockPlayerAgency;
							Game1.player.FacingDirection = oldFacingDirection;
							Game1.player.Position = oldPosition;
						}) //{xOffset = 26}
						,
						new FarmerSprite.AnimationFrame(32, 350, false, false) //{xOffset = 32}
						,
					}, who => {
						// Absolutely return player agency
						Helper.Events.Input.ButtonPressed -= Input_ButtonPressed_BlockPlayerAgency;
						Game1.player.FacingDirection = oldFacingDirection;
						Game1.player.Position = oldPosition;
					});
					break;

				case ActionLadder:
					Log.W("ActionLadder!");
					// Climb up each level of the mill via the ladder, then climb all the way back down
					var targetTile = new Location((int) position.X,
						((int) position.Y - RoomSpacing) % (Game1.currentLocation.Map.DisplayHeight / 64) + 1);
					Log.W($"TargetTile: {targetTile.ToString()}");
					Game1.playSound("stairsdown");
					Game1.player.warpFarmer(new Warp(
						Game1.player.getTileX(), Game1.player.getTileY(),
						Game1.currentLocation.Name,
						targetTile.X,
						targetTile.Y < 0
							? targetTile.Y + Game1.currentLocation.Map.DisplayHeight / 64
							: targetTile.Y,
						false));
					break;

				case ActionFlourBin:
					if (millInterior.Mill.output.Value.items.Any())
						Utility.CollectSingleItemOrShowChestMenu(millInterior.Mill.output.Value, millInterior.Mill);
					else
						goto case InspectAction;
					break;
					
				case ActionGreatWheel:
				case ActionMillstone:
				case ActionConveyor:
				case InspectAction:
					Log.W("InspectAction!");
					if (action[0] == InspectAction && forceAction != null)
						action[0] = forceAction[1];
					var message = "world.mill_" + action[0] switch
					{
						ActionGreatWheel => "greatwheel",
						ActionMillstone => "millstone",
						ActionFlourBin => "flourbin",
						ActionHopper => "hopper",
						ActionConveyor => "conveyor",
						ActionLever => "lever",
						_ => "???"
					} + $"{(millInterior.Mill.input.Any() ? "_active" : "")}.inspect";
					Game1.drawDialogueNoTyping(i18n.Get(message, new
					{
						produce = millInterior.Mill.input.Value.items.FirstOrDefault()?.DisplayName.ToLower() ?? "produce"
					}));
					break;
			}
		}

		private void Input_ButtonPressed_BlockPlayerAgency(object sender, ButtonPressedEventArgs e)
		{
			Helper.Input.Suppress(e.Button);
		}

		internal static void UpdateMillInteriorWarpOutLocation(Location enterFrom, Map map = null)
		{
			map ??= Game1.getLocationFromName(MillInteriorMapName).Map;

			RoomSpacing = (map.DisplayHeight / 64 - RoomHeight * 3);

			var warp = map.Properties["Warp"].ToString().Split(' ').ToList();
			var oldWarp = warp;

			if (enterFrom != Location.Origin)
			{
				warp[3] = enterFrom.X.ToString();
				warp[4] = enterFrom.Y.ToString();
			}

			// Mill Interior exit is 2 tiles wide, so it needs 2 defined warps
			warp = warp.Concat(new[] {(int.Parse(warp[0]) + 1).ToString(), warp[1], warp[2], warp[3], warp[4]}).ToList();
			map.Properties["Warp"] = warp.Aggregate("", (s, s1) => $"{s} {s1}").Remove(0, 1);

			Log.D($"Updated {MillInteriorMapName} warps:"
			      + $"\nFrom {oldWarp.Aggregate("", (s, s1) => $"{s} {s1}")}"
			      + $"\nTo {warp.Aggregate("", (s, s1) => $"{s} {s1}")}"
			      + $"\nMap spacing: {RoomSpacing}, Map Height: {RoomHeight}");
		}

		public bool MillInteriorHopperAction(Mill mill, Farmer who)
		{
			Log.D($"MillableItems: {Config.MillableItems.Keys.Aggregate("", (s, o) => $"{s}, {o}")}");

			// Empty-handed clicks show an inspect prompt
			if (who?.ActiveObject == null)
			{
				CheckTileAction(Vector2.Zero, Game1.currentLocation, new [] {InspectAction, ActionHopper});
				return false;
			}

			// Only add items marked as valid in the config file
			if (!Config.MillableItems.ContainsKey(who.ActiveObject.ParentSheetIndex.ToString())
			    && !Config.MillableItems.ContainsKey(who.ActiveObject.Name))
			{
				Log.D($"ActiveObject: {who.ActiveObject.ParentSheetIndex} ({who.ActiveObject.Name})");
				Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantMill"));
				return false;
			}

			// Try to add items to the hopper if there's available room
			var currentMillInput = (StardewValley.Object) Utility.addItemToThisInventoryList(
				who.ActiveObject,
				((MillInterior)Game1.currentLocation).MillHopperInputs[mill.nameOfIndoors].items,
				36);
			who.ActiveObject = null;
			if (currentMillInput != null)
				who.ActiveObject = currentMillInput;
			if (who.ActiveObject != null)
			{
				Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:MillFull"));
				return false;
			}
			
			// Change hopper appearance
			/*
			for (var x = 5; x < 7; ++x)
			for (var y = 5; y < 7; ++y)
				Game1.currentLocation.Map.GetLayer("AboveBuildings")
					.Tiles[x, y].TileIndex = 46 + x - 5 + 16 * (5 - y);
			*/
			
			Game1.playSound("Ship");
			return true;
		}
	}
}