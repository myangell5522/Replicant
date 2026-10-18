using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Replicant.Content.Projectiles;
using Replicant.Content.Tiles;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace Replicant.Common.Dimension
{
	/// <summary>
	/// The shape of the oil fields: a gentle plain of long swells and soft mounds that LOOPS — walk off one
	/// end and you are back at the other. Everything is a pure function of x so the generator, the renderer
	/// and the loop all agree.
	/// </summary>
	public static class DimensionTerrain
	{
		public const int LoopStart = 320;          // first tile of the loop
		public const int LoopWidth = 720;          // tiles before the field repeats
		public const int Width = LoopStart * 2 + LoopWidth;
		public const int Height = 720;
		public const int Ground = 360;             // mean surface height (tiles): a tall sky, so the top edge is never in view
		public static int LoopCenter => LoopStart + LoopWidth / 2;

		private static readonly float[] moundX = new float[12], moundH = new float[12], moundW = new float[12];
		private static bool built;

		private static void Build()
		{
			var r = new UnifiedRandom(1337);
			for (int i = 0; i < moundX.Length; i++) {
				moundX[i] = r.NextFloat(LoopWidth);
				moundH[i] = r.NextFloat(1.2f, 4.2f);
				moundW[i] = r.NextFloat(14f, 34f);
			}
			built = true;
		}

		/// <summary>0..LoopWidth position of a world tile x inside the loop.</summary>
		public static float LoopX(float x)
		{
			float u = (x - LoopStart) % LoopWidth;
			return u < 0 ? u + LoopWidth : u;
		}

		/// <summary>Surface height in tiles (smaller = higher) at world tile x (fractional).</summary>
		public static float SurfaceAt(float x)
		{
			if (!built)
				Build();
			float u = LoopX(x);
			float a = u / LoopWidth * MathHelper.TwoPi;
			float h = MathF.Sin(a * 2f) * 2.2f + MathF.Sin(a * 5f + 1.3f) * 1.1f + MathF.Sin(a * 11f + 0.4f) * 0.4f;
			for (int i = 0; i < moundX.Length; i++) {
				float d = MathF.Abs(u - moundX[i]);
				d = MathF.Min(d, LoopWidth - d);                 // wrap
				h += moundH[i] * MathF.Exp(-(d / moundW[i]) * (d / moundW[i]));
			}
			// the place they first appear: a quiet, flat stage
			float stage = MathF.Abs(u - LoopWidth / 2f);
			h *= Utils.GetLerpValue(18f, 60f, stage, true);
			return Ground - h;
		}

		/// <summary>Where a lily grows (deterministic per loop column, so the loop repeats seamlessly).</summary>
		public static bool HasLily(int x)
		{
			int u = (int)LoopX(x);
			if (MathF.Abs(u - LoopWidth / 2f) < 4f)
				return false;
			return ReUtils.Hash01(u, 991) < 0.62f;
		}
	}

	/// <summary>
	/// Replicant's own dimension: a real, separate world (no other mod needed). At the peak of the explosion the
	/// home world is saved and unloaded, the oil fields are generated into a small world file of their own, and
	/// the player is loaded into it — all hidden behind the explosion, which keeps drawing over the loading
	/// screen. Nothing of the home world exists there and nothing there can touch the home world. Afterwards the
	/// home world is loaded again and the player returns to where they stood. The dimension itself is never saved
	/// (it is regenerated each time). Single player only.
	/// </summary>
	public class Subworld : ModSystem
	{
		private static string Folder => Path.Combine(Main.SavePath, "Replicant");
		private static string DimensionPath => Path.Combine(Folder, "TheSongsEnd.wld");
		private static readonly Guid DimensionId = new("6f1d3a52-7b0e-4c8e-9e2a-5d0c2f9b1a77");

		private static WorldFileData root;
		private static Vector2 homePosition;
		private static volatile bool traveling;
		private static bool enteringDimension;
		private static int idleTimer;

		public static bool InDimension { get; private set; }
		public static bool Traveling => traveling;
		/// <summary>The travel in progress is toward the dimension (for the loading overlay).</summary>
		public static bool EnteringDimension => enteringDimension;

		private static readonly FieldInfo archiveLock = typeof(Main).Assembly.GetType("Terraria.ModLoader.BackupIO")?.GetField("archiveLock", BindingFlags.Public | BindingFlags.Static);

		public override void Load()
		{
			if (Main.dedServ)
				return;
			On_Main.DrawMenu += DrawOverMenu;
			On_Main.UpdateAudio_DecideOnNewMusic += KeepMusic;
		}

		public override void Unload()
		{
			root = null;
		}

		// ------------------------------------------------------------------ requests
		public static string WhyNot()
		{
			if (Main.netMode != NetmodeID.SinglePlayer)
				return "the dimension only opens in single player";
			if (traveling)
				return "already travelling";
			if (Main.ActiveWorldFileData == null || Main.ActivePlayerFileData == null)
				return "no world is loaded";
			if (Main.LocalPlayer.dead)
				return "you are dead";
			return null;
		}

		public static bool Enter()
		{
			if (InDimension)
				return true;
			if (WhyNot() != null)
				return false;
			root = Main.ActiveWorldFileData;
			homePosition = Main.LocalPlayer.position;
			Begin(toDimension: true);
			return true;
		}

		public static void ReturnHome()
		{
			if (!InDimension || traveling || root == null)
				return;
			Begin(toDimension: false);
		}

		// ------------------------------------------------------------------ the journey
		private static void Begin(bool toDimension)
		{
			traveling = true;
			enteringDimension = toDimension;
			if (!toDimension)
				SubworldPlayer.returnTo = homePosition;
			try {
				SystemLoader.PreSaveAndQuit();
			}
			catch (Exception e) {
				Log("PreSaveAndQuit threw: " + e.Message);
			}
			Main.menuMode = 10;
			Main.gameMenu = true;
			Main.statusText = "";
			// start after this frame's update has finished, on a worker thread (like vanilla's save-and-quit)
			Main.QueueMainThreadAction(() => ThreadPool.QueueUserWorkItem(_ => Run(toDimension)));
		}

		private static void Run(bool toDimension)
		{
			bool leavingDimension = InDimension;
			try {
				Leave(saveWorld: !leavingDimension);
				WorldFileData target = toDimension ? Generate() : root;
				SetArchiveLock(toDimension);
				target.SetAsActive();
				RunOnMainThread(ReloadPlayer);
				WorldGen.playWorldCallBack(null);
				if (WorldGen.loadFailed || !WorldGen.loadSuccess)
					throw new Exception("the world failed to load");
				if (!toDimension)
					DeleteDimensionFiles();
			}
			catch (Exception e) {
				Log("Travel failed, returning home: " + e);
				try {
					if (root != null) {
						SetArchiveLock(false);
						root.SetAsActive();
						SubworldPlayer.returnTo = homePosition;
						WorldGen.playWorldCallBack(null);
					}
				}
				catch (Exception e2) {
					Log("Returning home failed too: " + e2);
					Main.menuMode = 0;
				}
			}
			finally {
				traveling = false;
			}
		}

		/// <summary>Vanilla's save-and-quit without going to the menu.</summary>
		private static void Leave(bool saveWorld)
		{
			SoundEngine.StopTrackedSounds();
			CaptureInterface.ResetFocus();
			Main.ActivePlayerFileData.StopPlayTimer();
			Player.SavePlayer(Main.ActivePlayerFileData);
			Player.ClearPlayerTempInfo();
			Rain.ClearRain();
			if (saveWorld) {
				WorldFile.CacheSaveTime();
				WorldFile.SaveWorld();
			}
			SystemLoader.OnWorldUnload();
			Main.fastForwardTimeToDawn = false;
			Main.fastForwardTimeToDusk = false;
			Main.UpdateTimeRate();
		}

		/// <summary>Writes the oil fields as a small world of their own.</summary>
		private static WorldFileData Generate()
		{
			Directory.CreateDirectory(Folder);
			DeleteDimensionFiles();
			WorldFileData data = new(DimensionPath, false) {
				Name = "The Song's End",
				GameMode = Main.GameMode,
				CreationTime = DateTime.Now,
				Metadata = FileMetadata.FromCurrentSettings(FileType.World),
				WorldGeneratorVersion = 1198295875585uL,
				UniqueId = DimensionId,
			};
			data.SetSeed("1337");
			data.SetWorldSize(DimensionTerrain.Width, DimensionTerrain.Height);
			data.SetAsActive();
			WorldGen.generatingWorld = true;
			WorldGen.gen = true;
			try {
				Main.maxTilesX = DimensionTerrain.Width;
				Main.maxTilesY = DimensionTerrain.Height;
				Main.worldName = data.Name;
				Main.worldID = 0x5EED_0D1E & int.MaxValue;
				WorldGen.clearWorld();
				SetLayers();
				Main.spawnTileX = DimensionTerrain.LoopCenter;
				Main.spawnTileY = (int)DimensionTerrain.SurfaceAt(Main.spawnTileX) - 1;
				WorldGen._genRand = new UnifiedRandom(1337);
				Main.rand = new UnifiedRandom(1337);
				GenVars.structures = new StructureMap();
				WorldGen.RandomizeBackgrounds(WorldGen.genRand);
				WorldGen.RandomizeTreeStyle();
				WorldGen.RandomizeCaveBackgrounds();
				FillField();
			}
			finally {
				WorldGen.gen = false;
				WorldGen.generatingWorld = false;
			}
			WorldFile.SaveWorld(false, resetTime: true);
			return data;
		}

		/// <summary>
		/// Everything the camera can see is "surface": the sky (not the cave or hell background) shows behind the field,
		/// and the sun lights it.
		/// </summary>
		private static void SetLayers()
		{
			// (the underworld layer is maxTilesY - 200 = 520: far below anything the camera can reach, so the
			// hell background never bleeds into the field)
			Main.worldSurface = DimensionTerrain.Ground + 90;
			Main.rockLayer = DimensionTerrain.Ground + 100;
		}

		private static void FillField()
		{
			int stone = ModContent.TileType<OilStone>();
			int lily = ModContent.TileType<GlyphLily>();
			for (int x = 0; x < Main.maxTilesX; x++) {
				// half-tile steps under a smooth drawn surface: the ground never shows hard corners
				float top = MathF.Ceiling(DimensionTerrain.SurfaceAt(x + 0.5f) * 2f) / 2f;
				int ty = (int)MathF.Floor(top);
				bool half = top - ty > 0.25f;
				for (int y = ty; y < Main.maxTilesY; y++) {
					Tile t = Main.tile[x, y];
					t.HasTile = true;
					t.TileType = (ushort)stone;
					if (y == ty && half)
						t.IsHalfBlock = true;
				}
				if (DimensionTerrain.HasLily(x)) {
					Tile t = Main.tile[x, ty - 1];
					t.HasTile = true;
					t.TileType = (ushort)lily;
					t.TileFrameX = 0;
					t.TileFrameY = 0;
				}
			}
		}

		private static void ReloadPlayer()
		{
			PlayerFileData current = Main.ActivePlayerFileData;
			try {
				PlayerFileData reloaded = Player.LoadPlayer(current.Path, current.IsCloudSave);
				if (reloaded?.Player != null && reloaded.Player.loadStatus == 0)
					reloaded.SetAsActive();
			}
			catch (Exception e) {
				Log("Reloading the player failed; keeping the one in memory: " + e.Message);
			}
		}

		private static void RunOnMainThread(Action action)
		{
			using ManualResetEventSlim done = new(false);
			Exception error = null;
			Main.QueueMainThreadAction(() => {
				try {
					action();
				}
				catch (Exception e) {
					error = e;
				}
				finally {
					done.Set();
				}
			});
			if (!done.Wait(TimeSpan.FromSeconds(30)))
				throw new TimeoutException("the main thread did not answer");
			if (error != null)
				throw new Exception("main thread step failed", error);
		}

		private static void DeleteDimensionFiles()
		{
			try {
				foreach (string p in new[] { DimensionPath, DimensionPath + ".bak", Path.ChangeExtension(DimensionPath, ".twld"), Path.ChangeExtension(DimensionPath, ".twld") + ".bak" }) {
					if (File.Exists(p))
						File.Delete(p);
				}
				// the map the player drew of it
				string playerPath = Main.ActivePlayerFileData?.Path;
				if (!string.IsNullOrEmpty(playerPath) && !Main.ActivePlayerFileData.IsCloudSave) {
					string map = Path.Combine(playerPath[..^4], DimensionId + ".map");
					foreach (string p in new[] { map, map + ".bak", Path.ChangeExtension(map, ".tmap"), Path.ChangeExtension(map, ".tmap") + ".bak" }) {
						if (File.Exists(p))
							File.Delete(p);
					}
				}
			}
			catch (Exception e) {
				Log("Could not delete old dimension files: " + e.Message);
			}
		}

		private static void SetArchiveLock(bool value)
		{
			try {
				archiveLock?.SetValue(null, value);
			}
			catch { }
		}

		private static void Log(string s) => ModContent.GetInstance<Replicant>().Logger.Warn("[Replicant] " + s);

		// ------------------------------------------------------------------ while loading
		/// <summary>The explosion (or the way home) is drawn over the vanilla loading screen.</summary>
		private static void DrawOverMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
		{
			orig(self, gameTime);
			if (!traveling)
				return;
			RiddleCutscene.DrawLoadingScreen(Main.spriteBatch);
		}

		private static void KeepMusic(On_Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self)
		{
			orig(self);
			if (traveling)
				Main.newMusic = MusicLoader.GetMusicSlot(ModContent.GetInstance<Replicant>(), enteringDimension ? "Assets/Music/RebirthSwell" : "Assets/Music/Silence");
		}

		// ------------------------------------------------------------------ in the dimension
		public override void OnWorldLoad()
		{
			InDimension = Main.ActiveWorldFileData != null && Main.ActiveWorldFileData.Path == DimensionPath;
			if (InDimension)
				SetLayers();
			idleTimer = 0;
		}

		public override void OnWorldUnload()
		{
			if (!traveling)
				InDimension = false;
		}

		public override void PostUpdateEverything()
		{
			if (!InDimension)
				return;
			// a held breath of a world: no weather, a fixed hour
			Main.raining = false;
			Main.maxRaining = 0f;
			Main.cloudAlpha = 0f;
			Main.windSpeedCurrent = Main.windSpeedTarget = 0.05f;
			Main.dayTime = true;
			Main.time = 27000.0;
			WrapLoop();
			// once nothing is left to happen here, the song lets you go home
			bool busy = TwinsFight.AnyTwinAlive || RiddleCutscene.Active || TwinsFight.SpawnOnArrival || TwinsFight.ReturningHome || EpilogueCutscene.Active
				|| DevourCutscene.Active || NPC.AnyNPCs(ModContent.NPCType<Content.NPCs.Recursion.RecursionHead>());
			idleTimer = busy ? 0 : idleTimer + 1;
			if (idleTimer > 360 && !Main.LocalPlayer.dead)
				ReturnHome();
		}

		/// <summary>Walk off one end of the field and you are at the other; everything moves with you.</summary>
		private static void WrapLoop()
		{
			Player p = Main.LocalPlayer;
			float left = DimensionTerrain.LoopStart * 16f, right = (DimensionTerrain.LoopStart + DimensionTerrain.LoopWidth) * 16f;
			float shift = 0f;
			if (p.Center.X < left)
				shift = DimensionTerrain.LoopWidth * 16f;
			else if (p.Center.X > right)
				shift = -DimensionTerrain.LoopWidth * 16f;
			if (shift == 0f)
				return;
			Vector2 d = new(shift, 0f);
			p.position += d;
			p.oldPosition += d;
			Main.screenPosition += d;
			foreach (NPC n in Main.ActiveNPCs) {
				n.position += d;
				n.oldPosition += d;
				for (int i = 0; i < n.oldPos.Length; i++)
					if (n.oldPos[i] != Vector2.Zero)
						n.oldPos[i] += d;
				if (n.ModNPC is TwinBase t)
					t.Anchor += d;
			}
			foreach (Projectile proj in Main.ActiveProjectiles) {
				proj.position += d;
				proj.oldPosition += d;
				for (int i = 0; i < proj.oldPos.Length; i++)
					if (proj.oldPos[i] != Vector2.Zero)
						proj.oldPos[i] += d;
			}
			ParticleSystem.ShiftWorld(d);
			RiddleCutscene.ShiftWorld(d);
		}
	}

	/// <summary>Puts the player back where they stood in the home world.</summary>
	public class SubworldPlayer : ModPlayer
	{
		internal static Vector2? returnTo;

		public override void OnEnterWorld()
		{
			if (Subworld.InDimension) {
				RiddleCutscene.OnArrivedInDimension();
				return;
			}
			if (returnTo is Vector2 pos) {
				returnTo = null;
				Player.position = pos;
				Player.velocity = Vector2.Zero;
				Player.fallStart = (int)(pos.Y / 16f);
				Player.immuneTime = 90;
				Player.immune = true;
			}
		}
	}

	/// <summary>No spawns and no digging or building inside the dimension.</summary>
	public class PocketRules : GlobalNPC
	{
		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
		{
			if (Subworld.InDimension) {
				spawnRate = int.MaxValue;
				maxSpawns = 0;
			}
		}
	}

	public class PocketTiles : GlobalTile
	{
		public override bool CanPlace(int i, int j, int type) => !Subworld.InDimension;
		public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => !Subworld.InDimension;
		public override bool CanExplode(int i, int j, int type) => !Subworld.InDimension;
	}

	public class PocketWalls : GlobalWall
	{
		public override bool CanPlace(int i, int j, int type) => !Subworld.InDimension;
		public override bool CanExplode(int i, int j, int type) => !Subworld.InDimension;
	}

	/// <summary>
	/// Earlier builds of Replicant borrowed a strip of the sky for the arena. If a world was saved while that
	/// strip was open, this puts the original tiles back when the world loads.
	/// </summary>
	public class PocketLegacyRestore : ModSystem
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct Cell
		{
			public TileTypeData Type;
			public WallTypeData Wall;
			public LiquidData Liquid;
			public TileWallWireStateData Wire;
			public TileWallBrightnessInvisibilityData Bright;
		}

		public override void SaveWorldData(TagCompound tag) { }

		public override void LoadWorldData(TagCompound tag)
		{
			if (!tag.ContainsKey("pocketRegion"))
				return;
			try {
				int[] r = tag.GetIntArray("pocketRegion");
				if (r.Length < 5 || r[4] != Marshal.SizeOf<Cell>())
					return;
				using var input = new System.IO.Compression.GZipStream(new MemoryStream(tag.GetByteArray("pocketTiles")), System.IO.Compression.CompressionMode.Decompress);
				using var output = new MemoryStream();
				input.CopyTo(output);
				Cell[] cells = MemoryMarshal.Cast<byte, Cell>(output.ToArray()).ToArray();
				if (cells.Length != r[2] * r[3])
					return;
				int i = 0;
				for (int x = r[0]; x < r[0] + r[2]; x++) {
					for (int y = r[1]; y < r[1] + r[3]; y++) {
						Tile t = Main.tile[x, y];
						Cell c = cells[i++];
						t.Get<TileTypeData>() = c.Type;
						t.Get<WallTypeData>() = c.Wall;
						t.Get<LiquidData>() = c.Liquid;
						t.Get<TileWallWireStateData>() = c.Wire;
						t.Get<TileWallBrightnessInvisibilityData>() = c.Bright;
					}
				}
				Mod.Logger.Info("[Replicant] Restored world tiles left under an old arena.");
			}
			catch (Exception e) {
				Mod.Logger.Warn("[Replicant] Could not restore old arena tiles: " + e.Message);
			}
		}
	}

	/// <summary>The facade the rest of the mod uses to reach the dimension.</summary>
	public static class DimensionBridge
	{
		private static bool reportedThisWorld;

		public static bool CanTravel => WhyNot() == null;
		public static string WhyNot() => Subworld.WhyNot();
		public static bool InDimension => Subworld.InDimension;
		public static bool IsTraveling => Subworld.Traveling;

		public static bool Travel()
		{
			string why = WhyNot();
			if (why == null && !Subworld.Enter())
				why = "the dimension could not be opened";
			Report(why);
			return why == null;
		}

		public static void ReturnHome() => Subworld.ReturnHome();

		public static void Report(string reason)
		{
			if (Main.dedServ || reportedThisWorld || reason == null)
				return;
			reportedThisWorld = true;
			Main.NewText("[Replicant] The dimension could not open: " + reason + ". The song continues here.", new Color(255, 196, 70));
			ModContent.GetInstance<Replicant>().Logger.Warn("[Replicant] Dimension travel skipped: " + reason);
		}

		internal static void ResetReport() => reportedThisWorld = false;
	}
}
