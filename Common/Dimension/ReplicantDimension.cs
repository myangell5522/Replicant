using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Replicant.Common.Cutscene;
using Replicant.Content.NPCs.Twins;
using Replicant.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;

namespace Replicant.Common.Dimension
{
	/// <summary>
	/// Replicant's own dimension — no other mod needed.
	///
	/// When the explosion peaks, a band of the sky high above the world's centre is copied away, tile for tile,
	/// and rewritten as the oil fields: a black plain of low mounds and glyph lilies between two cliffs. The
	/// player is moved there behind the light of the burst. Everything outside the band is hidden behind the
	/// dimension's sky and a void at its edges, so it reads as another world.
	///
	/// When the fight ends (victory, death, leaving the world) the original tiles are put back exactly as they
	/// were. If the game saves while the dimension is open (autosave, crash), the copy of the original tiles is
	/// stored with the world and restored the next time it loads, so the world can never keep the arena.
	/// Single player only; in multiplayer phase 2 plays in place under the veil.
	/// </summary>
	public class PocketDimension : ModSystem
	{
		public const int Width = 560;
		public const int Above = 100;      // open sky above the plain
		public const int Below = 24;       // depth of the black stone under it

		public static bool Open { get; private set; }
		public static Rectangle Region { get; private set; }
		public static int GroundY { get; private set; }
		public static int CenterX { get; private set; }

		[StructLayout(LayoutKind.Sequential)]
		private struct Cell
		{
			public TileTypeData Type;
			public WallTypeData Wall;
			public LiquidData Liquid;
			public TileWallWireStateData Wire;
			public TileWallBrightnessInvisibilityData Bright;
		}

		private static Cell[] snapshot;
		private static Vector2 homePosition;
		private static int idleTimer;

		/// <summary>The player stands inside the open dimension.</summary>
		public static bool PlayerInside(Player p) => Open && Region.Contains((p.Center / 16f).ToPoint());

		// ------------------------------------------------------------------ placement
		/// <summary>
		/// The band sits just below the height where Terraria starts lowering gravity ("space"),
		/// so the fight keeps normal physics, and above the surface hills.
		/// </summary>
		private static bool ComputeRegion(out Rectangle region, out int ground, out string why)
		{
			float sizeK = Main.maxTilesX / 4200f;
			sizeK *= sizeK;
			int fullGravity = (int)(60f + 10f * sizeK + Main.worldSurface / 6.0);
			ground = fullGravity + Above - 40;
			int top = ground - Above;
			int bottom = ground + Below;
			int width = Math.Min(Width, Main.maxTilesX - 120);
			int x0 = Main.maxTilesX / 2 - width / 2;
			region = new Rectangle(x0, top, width, bottom - top);
			why = null;
			if (top < 42 || bottom >= Main.maxTilesY - 42) {
				why = "this world is too small to hold the dimension";
				return false;
			}
			return true;
		}

		public static string WhyNot()
		{
			if (Main.netMode != NetmodeID.SinglePlayer)
				return "the dimension only opens in single player";
			if (Open)
				return null;
			if (Main.LocalPlayer.dead)
				return "you are dead";
			return ComputeRegion(out _, out _, out string why) ? null : why;
		}

		// ------------------------------------------------------------------ open / close
		public static bool Enter()
		{
			if (Open)
				return true;
			if (WhyNot() != null || !ComputeRegion(out Rectangle r, out int ground, out _))
				return false;
			Region = r;
			GroundY = ground;
			CenterX = r.X + r.Width / 2;
			TakeSnapshot();
			Build();
			Open = true;
			idleTimer = 0;
			Player p = Main.LocalPlayer;
			homePosition = p.position;
			MovePlayer(p, new Vector2(CenterX * 16f + 8f, GroundY * 16f) - new Vector2(p.width / 2f, p.height + 2f));
			return true;
		}

		public static void Leave(bool movePlayer = true)
		{
			if (!Open)
				return;
			Player p = Main.LocalPlayer;
			bool inside = PlayerInside(p);
			Restore();
			Open = false;
			if (movePlayer && inside && p.active && !p.dead)
				MovePlayer(p, homePosition);
		}

		private static void MovePlayer(Player p, Vector2 position)
		{
			p.position = position;
			p.velocity = Vector2.Zero;
			p.fallStart = (int)(position.Y / 16f);
			p.immuneTime = Math.Max(p.immuneTime, 60);
			p.immune = true;
			p.RemoveAllGrapplingHooks();
			p.oldPosition = position;
		}

		// ------------------------------------------------------------------ snapshot
		private static void TakeSnapshot()
		{
			Rectangle r = Region;
			snapshot = new Cell[r.Width * r.Height];
			int i = 0;
			for (int x = r.Left; x < r.Right; x++) {
				for (int y = r.Top; y < r.Bottom; y++) {
					Tile t = Main.tile[x, y];
					snapshot[i++] = new Cell {
						Type = t.Get<TileTypeData>(),
						Wall = t.Get<WallTypeData>(),
						Liquid = t.Get<LiquidData>(),
						Wire = t.Get<TileWallWireStateData>(),
						Bright = t.Get<TileWallBrightnessInvisibilityData>(),
					};
				}
			}
		}

		private static void Restore()
		{
			if (snapshot == null)
				return;
			Rectangle r = Region;
			int i = 0;
			for (int x = r.Left; x < r.Right; x++) {
				for (int y = r.Top; y < r.Bottom; y++) {
					Tile t = Main.tile[x, y];
					Cell c = snapshot[i++];
					t.Get<TileTypeData>() = c.Type;
					t.Get<WallTypeData>() = c.Wall;
					t.Get<LiquidData>() = c.Liquid;
					t.Get<TileWallWireStateData>() = c.Wire;
					t.Get<TileWallBrightnessInvisibilityData>() = c.Bright;
				}
			}
			snapshot = null;
			Refresh(r);
		}

		private static void Refresh(Rectangle r)
		{
			if (Main.dedServ || Main.gameMenu)
				return;
			try {
				WorldGen.RangeFrame(r.Left, r.Top, r.Right, r.Bottom);
				Main.refreshMap = true;
			}
			catch { }
		}

		// ------------------------------------------------------------------ the oil fields
		private static void Build()
		{
			Rectangle r = Region;
			int stone = ModContent.TileType<OilStone>();
			int lily = ModContent.TileType<GlyphLily>();
			var rand = new UnifiedRandom(1337);
			int w = r.Width;
			int cx = w / 2;
			int[] ground = new int[w];
			float[] mounds = new float[w];
			for (int m = 0; m < 14; m++) {
				int mx = rand.Next(50, w - 50);
				if (Math.Abs(mx - cx) < 70)
					continue;
				float height = rand.NextFloat(2f, 7f);
				float width = rand.NextFloat(8f, 24f);
				for (int x = Math.Max(0, mx - (int)width * 2); x < Math.Min(w, mx + (int)width * 2); x++)
					mounds[x] += height * MathF.Exp(-MathF.Pow((x - mx) / width, 2f));
			}
			for (int x = 0; x < w; x++) {
				float swell = MathF.Sin(x * 0.013f) * 2.5f + MathF.Sin(x * 0.031f + 1.3f) * 1.2f;
				float flat = Utils.GetLerpValue(40f, 110f, Math.Abs(x - cx), true);
				ground[x] = GroundY - (int)MathF.Round((swell + mounds[x]) * flat);
				// cliffs of black stone at both ends: nobody walks out of this world
				int edge = Math.Min(x, w - 1 - x);
				if (edge < 44)
					ground[x] -= (int)((44 - edge) * 2.4f);
			}
			for (int x = 0; x < w; x++) {
				for (int y = r.Top; y < r.Bottom; y++) {
					Tile t = Main.tile[r.X + x, y];
					t.ClearEverything();
					if (y >= ground[x]) {
						t.HasTile = true;
						t.TileType = (ushort)stone;
					}
				}
			}
			for (int x = 46; x < w - 46; x++) {
				if (Math.Abs(x - cx) < 4)
					continue;
				float density = 0.55f - mounds[x] * 0.05f;
				if (rand.NextFloat() < density) {
					Tile t = Main.tile[r.X + x, ground[x] - 1];
					t.HasTile = true;
					t.TileType = (ushort)lily;
					t.TileFrameX = (short)(rand.Next(3) * 18);
					t.TileFrameY = 0;
				}
			}
			Refresh(r);
		}

		// ------------------------------------------------------------------ lifetime
		public override void PostUpdateEverything()
		{
			if (!Open)
				return;
			// the dimension closes itself once nothing is left to happen in it
			bool busy = TwinsFight.AnyTwinAlive || RiddleCutscene.Active || TwinsFight.SpawnOnArrival || TwinsFight.ReturningHome;
			idleTimer = busy ? 0 : idleTimer + 1;
			Player p = Main.LocalPlayer;
			if (idleTimer > 300 || (!PlayerInside(p) && !p.dead && idleTimer > 60))
				Leave();
		}

		public override void PreSaveAndQuit() => Leave(movePlayer: false);

		public override void OnWorldUnload()
		{
			// leaving without PreSaveAndQuit (e.g. the world was closed some other way): never keep the arena
			if (Open)
				Leave(movePlayer: false);
			Open = false;
			snapshot = null;
		}

		/// <summary>Autosave while open: store the original tiles so the next load puts them back.</summary>
		public override void SaveWorldData(TagCompound tag)
		{
			if (!Open || snapshot == null)
				return;
			ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(snapshot.AsSpan());
			using var ms = new MemoryStream();
			using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
				gz.Write(raw);
			tag["pocketRegion"] = new int[] { Region.X, Region.Y, Region.Width, Region.Height, Marshal.SizeOf<Cell>() };
			tag["pocketTiles"] = ms.ToArray();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			if (!tag.ContainsKey("pocketRegion"))
				return;
			try {
				int[] r = tag.GetIntArray("pocketRegion");
				if (r.Length < 5 || r[4] != Marshal.SizeOf<Cell>())
					return;
				using var input = new GZipStream(new MemoryStream(tag.GetByteArray("pocketTiles")), CompressionMode.Decompress);
				using var output = new MemoryStream();
				input.CopyTo(output);
				byte[] bytes = output.ToArray();
				var cells = MemoryMarshal.Cast<byte, Cell>(bytes).ToArray();
				if (cells.Length != r[2] * r[3])
					return;
				Region = new Rectangle(r[0], r[1], r[2], r[3]);
				snapshot = cells;
				Restore();
				Mod.Logger.Info("[Replicant] Restored the world tiles under an unclosed dimension.");
			}
			catch (Exception e) {
				Mod.Logger.Warn("[Replicant] Could not restore the tiles under the dimension: " + e.Message);
			}
		}
	}

	/// <summary>No spawns, no building, no digging inside the dimension.</summary>
	public class PocketRules : GlobalNPC
	{
		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
		{
			if (PocketDimension.PlayerInside(player)) {
				spawnRate = int.MaxValue;
				maxSpawns = 0;
			}
		}
	}

	public class PocketTiles : GlobalTile
	{
		private static bool InPocket(int i, int j) => PocketDimension.Open && PocketDimension.Region.Contains(i, j);
		public override bool CanPlace(int i, int j, int type) => !InPocket(i, j);
		public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => !InPocket(i, j);
		public override bool CanExplode(int i, int j, int type) => !InPocket(i, j);
	}

	public class PocketWalls : GlobalWall
	{
		public override bool CanPlace(int i, int j, int type) => !PocketDimension.Open || !PocketDimension.Region.Contains(i, j);
		public override bool CanExplode(int i, int j, int type) => !PocketDimension.Open || !PocketDimension.Region.Contains(i, j);
	}

	/// <summary>The facade the rest of the mod uses to reach the dimension.</summary>
	public static class DimensionBridge
	{
		private static bool reportedThisWorld;

		public static bool CanTravel => WhyNot() == null;
		public static string WhyNot() => PocketDimension.WhyNot();
		public static bool InDimension => PocketDimension.Open && PocketDimension.PlayerInside(Main.LocalPlayer);
		public static bool IsTraveling => false;

		public static bool Travel()
		{
			string why = WhyNot();
			if (why == null && !PocketDimension.Enter())
				why = "the dimension could not be built here";
			Report(why);
			return why == null;
		}

		public static void ReturnHome() => PocketDimension.Leave();

		/// <summary>Tell the player (once per world) why phase 2 is staying in this world.</summary>
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
