using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.Xna.Framework;
using Netcode;
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
		internal static ITMXLAPI TmxLoader;

		private const string CmdPrefix = "mill";
		internal const string TmxBuildablePrefix = "BuildableIndoors-";
		internal const string AssetPrefix = "blueberry.ImmersiveWindmill.";

		private const string ActionLadder = AssetPrefix + "MillLadder";
		private const string ActionGreatWheel = AssetPrefix + "MillGreatWheel";
		private const string ActionHopper = AssetPrefix + "MillHopper";
		private const string ActionLever = AssetPrefix + "MillLever";
		private const string ActionMillstone = AssetPrefix + "MillStone";
		private const string ActionConveyor = AssetPrefix + "MillConveyor";
		private const string ActionFlourBin = AssetPrefix + "MillFlourBin";
		private const string InspectAction = "InspectAction";

		internal static readonly Point MillBuildableExitTile = new Point(0, 2);
		internal static readonly Point MillEntryOffset = new Point(1, 1);
		internal static readonly string MillBuildableId = AssetPrefix + "MillBuildable";
		internal const int RoomHeight = 14;
		internal static int RoomSpacing = 28;

		public override void Entry(IModHelper helper)
		{
			Instance = this;
			Config = helper.ReadConfig<Config>();
			HarmonyPatches.Patch();

			Helper.Events.GameLoop.GameLaunched += GameLoopOnGameLaunched;
			Helper.Events.GameLoop.SaveLoaded += GameLoopOnSaveLoaded;
			Helper.Events.Input.ButtonPressed += InputOnButtonPressed;
			Helper.Events.Display.MenuChanged += DisplayOnMenuChanged;
			if (Config.DebugMode)
				Helper.ConsoleCommands.Add(
					$"{CmdPrefix}warp",
					"Warp outside a mill on the farm, or inside the MillIndoors location if no mills are found.",
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
			Helper.ConsoleCommands.Add(
				$"{CmdPrefix}build",
				"Rebuild and check over buildables to confirm valid data.", (s, args) =>
				{
					AddOrUpdateBuildablesForMills();
				});
		}
		
		private void LoadApis()
		{
			var uniqueId = "Platonymous.TMXLoader";
			TmxLoader = Helper.ModRegistry.GetApi<ITMXLAPI>(uniqueId);
			if (TmxLoader == null)
			{
				Log.E($"Couldn't load TMXLoader API from \"{uniqueId}\". Is the mod installed?");
			}
		}

		private void GameLoopOnGameLaunched(object sender, GameLaunchedEventArgs e)
		{
			LoadApis();
		}

		private void GameLoopOnSaveLoaded(object sender, SaveLoadedEventArgs e)
		{
			//AddOrUpdateBuildablesForMills();
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
			
			// Use tile actions in maps
			if (!e.Button.IsActionButton())
				CheckTileAction(e.Cursor.GrabTile, Game1.currentLocation);
		}
		
		public void Input_ButtonPressed_BlockPlayerAgency(object sender, ButtonPressedEventArgs e)
		{
			Helper.Input.Suppress(e.Button);
		}

		private void DisplayOnMenuChanged(object sender, MenuChangedEventArgs e)
		{
			// Update positions of all mill indoors entry 'fake' buildables when using Robin's blueprints menu
			if (e.NewMenu is CarpenterMenu || e.OldMenu is CarpenterMenu)
				AddOrUpdateBuildablesForMills();
		}

		/// <summary>
		/// Fetch all Mill buildings on the farm.
		/// </summary>
		public static List<Mill> GetMillsForFarm()
		{
			return Game1.getFarm().buildings.OfType<Mill>().ToList();
		}
		
		/// <summary>
		/// Fetch the spawn position for a 1x2 tile buildable on some Mill building.
		/// </summary>
		public static Point GetBuildablePositionForMill(Mill mill)
		{
			return new Point(
				mill.tileX.Value + MillEntryOffset.X,
				mill.tileY.Value + MillEntryOffset.Y - 1);
		}

		/// <summary>
		/// Check whether some buildable matches expected tile coordinates for any Mill building.
		/// </summary>
		public static bool BuildableMatchesMillPosition(Mill mill, SaveBuildable buildable)
		{
			var position = GetBuildablePositionForMill(mill);
			return buildable.Position[0] == position.X && buildable.Position[1] == position.Y;
		}

		/// <summary>
		/// Fetch buildable matching name of indoors, or if mill has no specified indoors added yet, then match by position
		/// </summary>
		public static SaveBuildable GetBuildableForMill(Mill mill)
		{
			var buildablesBuilt = (IList<SaveBuildable>) GetTmxlBuildablesBuilt().GetValue(null);
			var buildable = buildablesBuilt.FirstOrDefault(tmxb => tmxb.Indoors?.Name == mill.nameOfIndoors);
			buildable ??= buildablesBuilt.FirstOrDefault(tmxb => BuildableMatchesMillPosition(mill, buildable));
			return buildable;
		}

		/// <summary>
		/// Fetch mill matching name of indoors, or if mill has no specified indoors added yet, then match by position
		/// </summary>
		public static Mill GetMillForBuildable(IList<Mill> mills, SaveBuildable buildable)
		{
			var mill = mills.FirstOrDefault(m => buildable.Indoors?.Name == m.nameOfIndoors);
			mill ??= mills.FirstOrDefault(m => BuildableMatchesMillPosition(m, buildable));
			return mill;
		}

		public static Type GetTmx()
		{
			var tmx = Type.GetType("TMXLoader.TMXLoaderMod, TMXLoader");
			if (tmx != null && TmxLoader != null)
				return tmx;
			
			Log.E("Unable to save mill indoors data: TMXLoader not found, or ImmersiveWindmill out of date."
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

		// TODO: TEST: Indoors object persistence when moving Mills with buildables and indoors via BlueprintsMenu
		public void AddOrUpdateBuildablesForMills()
		{
			if (GetTmx() == null)
				return;

			Log.W("Updating TMXL buildables list");
			Log.D($"Current mills: {GetMillsForFarm().Count}");

			var tmxBuildablesBuilt = (IList<SaveBuildable>) GetTmxlBuildablesBuilt().GetValue(null);
			var tmxBuildablesDefined = (IList<BuildableEdit>) GetTmxlBuildablesDefined().GetValue(null);

			var definedBuildable = tmxBuildablesDefined.FirstOrDefault(b => b.id == MillBuildableId);
			if (definedBuildable == null)
			{
				Log.E($"No mill buildable definition found for {MillBuildableId}");
				return;
			}
			Log.D($"Confirmed mill buildable definition for {definedBuildable.name}:"
			      + $"\nIndoors {definedBuildable.indoorsFile} - Exit (X:{definedBuildable.exitTile[0]}, Y:{definedBuildable.exitTile[1]})");

			Log.W("Current TMXL Buildables:");
			var msg = tmxBuildablesBuilt.Aggregate(
				$"Count: {tmxBuildablesBuilt.Count}",
				(current, buildable) => current + $"\n{buildable.Id} : {buildable.UniqueId}"
				                                + $"\nAt (X:{buildable.Position[0]}, Y:{buildable.Position[1]})"
				                                + $" (Indoors: {buildable.Indoors?.Name ?? "null"})");
			Log.D(msg);

			var mills = GetMillsForFarm().ToDictionary(mill => mill, mill => false);
			for (var i = tmxBuildablesBuilt.Count - 1; i > -1; --i)
			{
				var buildable = tmxBuildablesBuilt[i];

				Log.D($"Checking buildable {buildable.Id} : {buildable.UniqueId}"
				      + $"\nAt (X:{buildable.Position[0]}, Y:{buildable.Position[1]})"
				      + $" (Indoors: {buildable.Indoors?.Name ?? "null"})");
				
				// Ignore buildables other than our 'fake' buildables
				if (buildable.Id != MillBuildableId)
					continue;
				
				// Check whether any mills match either the buildable indoors location or buildable position
				var mill = GetMillForBuildable(mills.Keys.ToList(), buildable);
				if (mill != null)
				{
					// Update mill to use buildable indoors location for matching with buildable later on
					// TODO: TEST: Check for mill indoors change
					var millIndoors = typeof(Mill).GetField(nameof(Mill.indoors), BindingFlags.Instance | BindingFlags.Public);
					if (millIndoors?.GetValue(mill) != null && buildable.Indoors != null)
						millIndoors.SetValue(mill, new NetRef<GameLocation>(Game1.getLocationFromName(TmxBuildablePrefix + buildable.UniqueId)));
					// TODO: TEST: Doesn't work.
					
					Log.D($"Confirmed buildable for mill at (X:{mill.tileX.Value}, Y:{mill.tileY.Value}) ({mill.indoors?.Value?.Name})");
					mills[mill] = true; // Mill matching this buildable exists on the farm, mark this one as OK

					// Check whether buildable matches position of any mill on the farm
					if (BuildableMatchesMillPosition(mill, buildable))
						continue;

					Log.D($"Mill at (X:{mill.tileX.Value}, Y:{mill.tileY.Value}) ({mill.indoors?.Value?.Name}) doesn't match buildable"
					      + $" at (X:{buildable.Position[0]}, Y:{buildable.Position[1]}) ({buildable.Indoors?.Name}), moving...");
					TmxLoader.MoveBuildable( // Update buildable position if the mill position doesn't match
						buildable.UniqueId,
						Game1.getLocationFromName(buildable.Location),
						GetBuildablePositionForMill(mill));
				}
				else // There are no mills on the farm that match the buildable, remove the buildable entry
				{
					Log.D($"Removing mill-less buildable at (X:{buildable.Position[0]}, Y:{buildable.Position[1]}) ({buildable.Indoors?.Name})");
					TmxLoader.RemoveBuildable(buildable.UniqueId);
				}
			}

			// Add entries for buildables for any new mills
			foreach (var pair in mills.Where(pair => !pair.Value))
			{
				Log.D($"Adding buildable for mill at (X:{pair.Key.tileX.Value}, Y:{pair.Key.tileY.Value})");
				TmxLoader.BuildBuildable(
					MillBuildableId,
					Game1.getFarm(),
					GetBuildablePositionForMill(pair.Key));
			}

			Log.W("New TMXL Buildables:");
			tmxBuildablesBuilt = (IList<SaveBuildable>) GetTmxlBuildablesBuilt().GetValue(null);
			msg = tmxBuildablesBuilt.Aggregate(
				$"Count: {tmxBuildablesBuilt.Count}",
				(current, buildable) => current + $"\n{buildable.Id} : {buildable.UniqueId}"
				                                + $"\nAt (X:{buildable.Position[0]}, Y:{buildable.Position[1]})"
				                                + $" (Indoors: {buildable.Indoors?.Name ?? "null"})");
			Log.D(msg);
		}
		
		public void CheckTileAction(Vector2 position, GameLocation location, string[] forceAction = null)
		{
			var property = location.doesTileHaveProperty(
				(int) position.X, (int) position.Y, "Action", "Buildings");
			if (property == null && forceAction == null)
				return;
			var action = property?.Split(' ') ?? forceAction;
			var millIndoors = Game1.currentLocation is MillIndoors ? (MillIndoors) Game1.currentLocation : null;
			switch (action[0])
			{
				case ActionHopper:
					Log.W("ActionHopper!");
					// Use the indoors mill action as a stage for mill drop-in items
					MillIndoorsHopperAction(millIndoors.Mill, Game1.player);
					break;

				case ActionLever:
					Log.W("ActionLever!");
					// Add all items from the hopper drop-in as inputs to the mill building

					var oldFacingDirection = Game1.player.FacingDirection;
					var oldPosition = Game1.player.Position;

					// Block player agency
					Game1.player.Halt();
					Game1.player.completelyStopAnimatingOrDoingAction();
					Game1.player.Position = position * 64f;
					Game1.player.FacingDirection = 1;
					Helper.Events.Input.ButtonPressed += Input_ButtonPressed_BlockPlayerAgency;

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
						}) //{xOffset = 26}
						,
						new FarmerSprite.AnimationFrame(32, 350, false, false) //{xOffset = 32}
						,
					}, who => {
						// Return player agency
						Helper.Events.Input.ButtonPressed -= Input_ButtonPressed_BlockPlayerAgency;
						Game1.player.FacingDirection = oldFacingDirection;
						Game1.player.Position = oldPosition;
					});
					break;

				case ActionLadder:
					Log.W("ActionLadder!");
					// Climb up each level of the mill via the ladder, then climb all the way back down
					// Assume all ladders are placed on the same tile relative to their room, and are evenly spaced apart
					var targetTile = new Location((int) position.X,
						((int) position.Y - RoomSpacing) % (Game1.currentLocation.Map.DisplayHeight / 64) + 1);
					var warp = new Warp(
						Game1.player.getTileX(), Game1.player.getTileY(),
						Game1.currentLocation.Name,
						targetTile.X,
						targetTile.Y < 0
							? targetTile.Y + Game1.currentLocation.Map.DisplayHeight / 64 + RoomHeight
							: targetTile.Y,
						false);
					Game1.player.warpFarmer(warp);
					Game1.playSound("stairsdown");
					Log.W($"Warp to: {warp.TargetName}, (X:{warp.TargetX}, Y:{warp.TargetY})");
					break;

				case ActionFlourBin:
					if (millIndoors.Mill.output.Value.items.Any())
						Utility.CollectSingleItemOrShowChestMenu(millIndoors.Mill.output.Value, millIndoors.Mill);
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
					} + $"{(millIndoors.Mill.input.Any() ? "_active" : "")}.inspect";
					Game1.drawDialogueNoTyping(i18n.Get(message, new
					{
						produce = millIndoors.Mill.input.Value.items.FirstOrDefault()?.DisplayName.ToLower() ?? "produce"
					}));
					break;
			}
		}

		public bool MillIndoorsHopperAction(Mill mill, Farmer who)
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
				((Chest)((MillIndoors)Game1.currentLocation).objects[MillIndoors.HopperPosition]).items,
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