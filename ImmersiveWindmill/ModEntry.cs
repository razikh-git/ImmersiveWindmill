using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using xTile;
using xTile.Dimensions;

using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Objects;

using TMXLoader;

namespace ImmersiveWindmill
{
	public class ModEntry : Mod
	{
		internal static ModEntry Instance;
		internal Config Config;
		internal ITranslationHelper i18n => Helper.Translation;

		private const string CmdPrefix = "mill";
		internal const string TmxBuildablePrefix = "BuildableIndoors-";
		internal const string AssetPrefix = "blueberry.ImmersiveWindmill.";
		internal static readonly string MillInteriorMapFile = "MillInterior";

		private const string ActionLadder = AssetPrefix + "MillLadder";
		private const string ActionGreatWheel = AssetPrefix + "MillGreatWheel";
		private const string ActionHopper = AssetPrefix + "MillHopper";
		private const string ActionLever = AssetPrefix + "MillLever";
		private const string ActionMillstone = AssetPrefix + "MillStone";
		private const string ActionConveyor = AssetPrefix + "MillConveyor";
		private const string ActionFlourBin = AssetPrefix + "MillFlourBin";
		private const string InspectAction = "InspectAction";

		internal static readonly Point MillEntryOffset = new Point(1, 1);
		internal static readonly string MillBuildableId = AssetPrefix + "MillBuildable";
		internal const int RoomHeight = 14;
		internal static int RoomSpacing = 28;

		public override void Entry(IModHelper helper)
		{
			Instance = this;
			Config = helper.ReadConfig<Config>();
			HarmonyPatches.Patch();

			Helper.Events.GameLoop.SaveLoaded += GameLoopOnSaveLoaded;
			Helper.Events.Input.ButtonPressed += InputOnButtonPressed;
			Helper.Events.Display.MenuChanged += DisplayOnMenuChanged;
			if (Config.DebugMode)
				Helper.ConsoleCommands.Add(
					$"{CmdPrefix}warp",
					"Warp outside a mill on the farm, or inside the MillInterior location if no mills are found.",
					(s, args) =>
					{
						var mill = GetMillsForFarm().FirstOrDefault();
						var buildables = (IList<SaveBuildable>) GetTmxlBuildablesBuilt().GetValue(null);
						var millIndoors = buildables.FirstOrDefault(b =>
							b.Position[0] == mill.tileX.Value && b.Position[1] == mill.tileY.Value);
						var warp = ((string)Game1.getLocationFromName(millIndoors.Indoors.Name).Map
							.Properties["Warp"]).Split(' ');
						Game1.player.warpFarmer(new Warp(Game1.player.getTileX(), Game1.player.getTileY(),
							millIndoors.Location,
							mill.tileX.Value + 1,
							mill.tileY.Value + 2,
							false));
					});
		}

		private void GameLoopOnSaveLoaded(object sender, SaveLoadedEventArgs e)
		{
			//asdf();
		}

		private void InputOnButtonPressed(object sender, ButtonPressedEventArgs e)
		{
			if (Game1.eventUp && !Game1.currentLocation.currentEvent.playerControlSequence // No event cutscenes
			    || Game1.currentBillboard != 0 || Game1.activeClickableMenu != null || Game1.menuUp // No menus
			    || Game1.nameSelectUp || Game1.IsChatting || Game1.dialogueTyping || Game1.dialogueUp // No text inputs
			    || Game1.player.UsingTool || Game1.pickingTool || Game1.numberOfSelectedItems != -1 // No tools in use
			    || Game1.fadeToBlack
			    || !Game1.player.CanMove)
				return;

			// TODO: DEBUG: Remove debug hotkey
			if (e.Button == SButton.F8)
				asdf();

			// Actions:
			if (!e.Button.IsActionButton())
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

			var buildable = ((IList<SaveBuildable>) GetTmxlBuildablesBuilt().GetValue(null))
				.FirstOrDefault(tmxb => tmxb.Position[0] == mill.tileX.Value && tmxb.Position[1] == mill.tileY.Value);
			if (buildable == null)
			{
				Log.E("Failed to find a suitable mill buildable/interior for mill at"
				      + $" (X:{mill.tileX.Value}, Y:{mill.tileY.Value}).");
				return;
			}

			var millIndoors = Game1.getLocationFromName(buildable.Indoors.Name);
			UpdateMillInteriorWarpOutLocation(new Location(Game1.player.getTileX(), Game1.player.getTileY()),
				millIndoors.Map);
			var warp = ((string)millIndoors.Map.Properties["Warp"]).Split(' ');

			if (warp.Length == 0)
			{
				Log.E($"Failed to patch in warp properties for {buildable.Indoors.Name} in setup:"
				      + $"\nWarp: {(warp.Length <= 0 ? "null" : warp.Aggregate((a,b) => $"{a} {b}"))}");
			}
			else
			{
				Log.W($"\nWarp: {(warp.Length <= 0 ? "null" : warp.Aggregate((a,b) => $"{a} {b}"))}");
				var targetPosition = new Location(int.Parse(warp[0]), int.Parse(warp[1]));
				Game1.player.warpFarmer(new Warp(Game1.player.getTileX(), Game1.player.getTileY(),
					buildable.Indoors.Name, targetPosition.X, targetPosition.Y - 1, false));
				Helper.Input.Suppress(e.Button);
			}
		}
		
		private void DisplayOnMenuChanged(object sender, MenuChangedEventArgs e)
		{
			// Update positions of all mill interior entry 'fake' buildables when using Robin's blueprints menu
			if (e.NewMenu is BlueprintsMenu || e.OldMenu is BlueprintsMenu)
				asdf();
		}

		public static List<Mill> GetMillsForFarm()
		{
			return Game1.getFarm().buildings.OfType<Mill>().ToList();
		}

		public static Type GetTmx()
		{
			var tmx = Type.GetType("TMXLoader.TMXLoaderMod, TMXLoader");
			if (tmx != null)
			{
				return tmx;
			}
			
			Log.E("Unable to save mill interior data: TMXLoader not found, or ImmersiveWindmill out of date."
			      + "\nAll objects placed within mills will be lost upon saving.");
			return null;
		}

		public static FieldInfo GetTmxlBuildablesDefined()
		{
			return GetTmx().GetField("buildables", 
					BindingFlags.Public | BindingFlags.Static);
		}

		public static FieldInfo GetTmxlBuildablesBuilt()
		{
			return GetTmx().GetField("buildablesBuild", 
					BindingFlags.NonPublic | BindingFlags.Static);

		}

		public static FieldInfo GetTmxlBuildableExits()
		{
			return GetTmx().GetField("buildablesExits", 
					BindingFlags.NonPublic | BindingFlags.Static);
		}

		public SaveBuildable GetNewMillBuildable(Point position)
		{
			var uniqueId = ((ulong)Helper.Multiplayer.GetNewID()).ToString();
			return new SaveBuildable(
				MillBuildableId,
				"Farm",
				position,
				uniqueId,
				Game1.player.Name,
				Game1.player.UniqueMultiplayerID,
				new Dictionary<string, string>())
			{
				Indoors = new SaveLocation(TmxBuildablePrefix + uniqueId, "")
			};
		}

		private void asdf()
		{
			if (GetTmx() == null)
				return;

			Log.W("Updating TMXL buildables list");
			Log.D($"Current mills: {GetMillsForFarm().Count}");
			var tmxBuildablesBuiltField = GetTmxlBuildablesBuilt();
			var tmxBuildablesBuilt = (IList<SaveBuildable>) tmxBuildablesBuiltField.GetValue(null);
			var tmxBuildableExitsField = GetTmxlBuildableExits();
			var tmxBuildableExits = (IDictionary<string, Warp>) tmxBuildableExitsField.GetValue(null);
			var tmxBuildablesDefinedField = GetTmxlBuildablesDefined();
			var tmxBuildablesDefined = (IList<BuildableEdit>) GetTmxlBuildablesDefined().GetValue(null);
			var buildableDefinition = tmxBuildablesDefined.FirstOrDefault(b => b.id == MillBuildableId);
			if (buildableDefinition == null)
			{
				// Add buildable definition
				var iconPath = Path.Combine("assets", "icon.png");
				tmxBuildablesDefined.Add(new BuildableEdit(
					MillBuildableId,
					Path.Combine("assets", MillInteriorMapFile),
					iconPath,
					-1,
					Helper.Content.Load<Texture2D>(iconPath),
					MillInteriorMapFile,
					Game1.getFarm().Name,
					null));
				tmxBuildablesDefinedField.SetValue(null, tmxBuildablesDefined);
			}

			Log.W("Old TMXL Buildables:");
			var msg = tmxBuildablesBuilt.Aggregate(
				$"Count: {tmxBuildablesBuilt.Count}",
				(current, buildable) => current + $"\n{buildable.Id} : {buildable.UniqueId}");
			Log.D(msg);

			var mills = GetMillsForFarm().ToDictionary(mill => mill, mill => false);
			for (var i = tmxBuildablesBuilt.Count - 1; i > -1; --i)
			{
				var buildable = tmxBuildablesBuilt[i];

				Log.D($"Checking buildable {buildable.Id} : {buildable.UniqueId}"
				      + $" at (X:{buildable.Position[0]}, Y:{buildable.Position[1]})");

				if (buildable.Id != MillBuildableId)
					// Ignore buildables other than our 'fake' buildables
					continue;

				var mill = mills.FirstOrDefault(pair => // Check whether buildable matches position of any mill on the farm
					pair.Key.tileX.Value == buildable.Position[0]
					&& pair.Key.tileY.Value == buildable.Position[1]).Key;

				if (mill != null) // Mill matching this buildable exists on the farm, mark this one as OK
				{
					Log.D($"Confirmed buildable for mill at (X:{mill.tileX.Value}, Y:{mill.tileY.Value})");
					mills[mill] = true;
				}
				else // There are no mills on the farm that match the buildable, remove the buildable entry
				{
					Log.D($"Removing buildable for mill at (X:{buildable.Position[0]}, Y:{buildable.Position[1]})");
					tmxBuildablesBuilt.RemoveAt(i);
					tmxBuildableExits.Remove(buildable.Indoors.Name);
				}
			}

			// Update TMXL buildables lists to match new mill positions
			// Updating buildable exits should give us our exit warps back to the farm
			foreach (var pair in mills.Where(pair => !pair.Value))
			{
				Log.D($"Adding buildable for mill at (X:{pair.Key.tileX.Value}, Y:{pair.Key.tileY.Value})");
				var buildable = GetNewMillBuildable(new Point(pair.Key.tileX.Value, pair.Key.tileY.Value));
				tmxBuildablesBuilt.Add(buildable);
				tmxBuildableExits.Add(buildable.Indoors.Name,
					new Warp(0, 0, 
						buildable.Location,
						buildable.Position[0], buildable.Position[1], false));
			}

			// Apply our changes to TMXL's fields
			tmxBuildablesBuiltField.SetValue(null, tmxBuildablesBuilt);
			tmxBuildableExitsField.SetValue(null, tmxBuildableExits);

			Log.W("New TMXL Buildables:");
			msg = tmxBuildablesBuilt.Aggregate(
				$"Count: {tmxBuildablesBuilt.Count}",
				(current, buildable) => current + $"\n{buildable.Id} : {buildable.UniqueId}");
			Log.D(msg);
		}
		
		public void CheckTileAction(Vector2 position, GameLocation location, string[] forceAction = null)
		{
			var property = location.doesTileHaveProperty(
				(int) position.X, (int) position.Y, "Action", "Buildings");
			if (property == null && forceAction == null)
				return;
			var action = property?.Split(' ') ?? forceAction;
			var millInterior = Game1.currentLocation is MillInterior ? (MillInterior) Game1.currentLocation : null;
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

		internal static void UpdateMillInteriorWarpOutLocation(Location enterFrom, Map map)
		{
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

			Log.D("Updated mill interior warps:"
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
				((Chest)((MillInterior)Game1.currentLocation).objects[MillInterior.HopperPosition]).items,
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