using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using ReLogic.Utilities;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.ReBoss;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// The death sequence of Re:
	///   Darken (music fades) → pixel eyes open in a grey void → riddle (5 answers, every pick corrupts the eyes
	///   and the text) → eyes close → silence → slow-motion fountain of glyph streams (the second phase awakening).
	/// Runs locally on each client; the boss waits in its Dying state until <see cref="Finish"/>.
	/// </summary>
	public class RiddleCutscene : ModSystem
	{
		public enum Stage { None, Darken, EyesOpen, Riddle, EyesClose, Silence, Burst }

		public static Stage Current { get; private set; } = Stage.None;
		public static bool Active => Current != Stage.None;
		/// <summary>Slow-motion factor for cutscene-driven effects (1 = normal).</summary>
		public static float TimeScale { get; private set; } = 1f;

		public const int AnswerCount = 5;
		public const int CorrectAnswer = 3;

		private static int npcIndex = -1;
		private static Vector2 bossWorldPos;
		private static int stageTimer;
		private static float burstClock;            // slow-mo scaled ticks since impact
		private static readonly bool[] picked = new bool[AnswerCount];
		private static int picks;
		private static int firstPick = -1;
		private static int hovered = -1, keyboardCursor = -1;
		private static int lastPickTimer;
		private static int glitchTimer;
		private static float levelVisual;
		private static int responseKey;
		private static int responseTimer;
		private static float textReveal;
		private static float overlay;
		private static float flash;
		private static float pickDarken;            // brief dip into darkness on every answer
		private static int eyeRow, blinkTimer, gazeTimer, gazeRow;
		private static SlotId? streamLoop;
		// ---- the jump between worlds (the pocket dimension): taken at the peak of the burst
		private const int TravelAtReal = 70;
		/// <summary>True from the moment we leave the home world until we land in the dimension.</summary>
		public static bool TravelingToDimension { get; private set; }
		/// <summary>This cutscene has already handed Re's death over (twins are spawned by <see cref="Finish"/>).</summary>
		public static bool SpawnHandledByCutscene { get; private set; }
		private static Vector2? fixedScreen;       // burst centre in screen pixels while worlds are swapped
		private static float fixedBlend;           // 1 = fixedScreen, 0 = follow the world
		private static int arrivedReal = -1;
		private static int loadingFrames;
		private static int settleTicks;
		private static int relocateTicks;
		private static readonly List<Stream> streams = new();
		private static readonly List<FogPuff> fog = new();

		private enum StreamKind { Glyphs, Filament, Loop }

		private struct Stream
		{
			public StreamKind Kind;
			public float Angle, Bend, Length, Width, Scroll, Delay, Wobble, Seed;
			public Color Tint;
		}

		private struct FogPuff
		{
			public float X, Speed, Size, Rot, Delay;
		}

		// ================================================================== control
		public static void Begin(int npc)
		{
			if (Main.dedServ || Active)
				return;
			npcIndex = npc;
			bossWorldPos = Main.npc[npc].Center;
			Array.Clear(picked);
			picks = 0;
			firstPick = -1;
			hovered = keyboardCursor = -1;
			levelVisual = 0f;
			textReveal = 0f;
			responseTimer = 0;
			responseKey = 0;
			overlay = 0f;
			flash = 0f;
			pickDarken = 0f;
			TimeScale = 1f;
			streams.Clear();
			fog.Clear();
			ParticleSystem.ClearScreen();
			TravelingToDimension = false;
			SpawnHandledByCutscene = false;
			fixedScreen = null;
			fixedBlend = 0f;
			arrivedReal = -1;
			SetStage(Stage.Darken);
		}

		private static void SetStage(Stage s)
		{
			Current = s;
			stageTimer = 0;
		}

		private static void Finish()
		{
			StopStreamLoop();
			Current = Stage.None;
			TimeScale = 1f;
			overlay = 0f;
			ParticleSystem.ClearScreen();
			fixedScreen = null;
			if (SpawnHandledByCutscene) {
				// Re already fell in the world we left; her daughters wake here
				SpawnHandledByCutscene = false;
				TwinsFight.SpawnTwins(bossWorldPos, Main.myPlayer);
			}
			else if (npcIndex >= 0 && Main.npc[npcIndex].active && Main.npc[npcIndex].ModNPC is Re re) {
				if (Main.netMode == NetmodeID.SinglePlayer)
					re.FinishDeathSequence();
				else {
					ModPacket p = ModContent.GetInstance<Replicant>().GetPacket();
					p.Write((byte)Replicant.PacketType.CutsceneDone);
					p.Send();
				}
			}
			npcIndex = -1;
		}

		private static void Abort()
		{
			StopStreamLoop();
			Current = Stage.None;
			TimeScale = 1f;
			overlay = 0f;
			ParticleSystem.ClearScreen();
			npcIndex = -1;
		}

		private static void StopStreamLoop()
		{
			if (streamLoop.HasValue && SoundEngine.TryGetActiveSound(streamLoop.Value, out ActiveSound s))
				s.Stop();
			streamLoop = null;
		}

		public override void OnWorldUnload()
		{
			// leaving for the dimension mid-burst: the cutscene carries on across the worlds
			if (TravelingToDimension)
				return;
			SpawnHandledByCutscene = false;
			Abort();
		}

		/// <summary>The burst's peak: Re is finished in the home world and we step into the dimension behind the light.</summary>
		private static void StartTravel()
		{
			fixedScreen = WorldToScreen(bossWorldPos);
			fixedBlend = 1f;
			gameScreenW = Main.screenWidth;
			gameScreenH = Main.screenHeight;
			SpawnHandledByCutscene = true;
			Re boss = Boss;
			boss?.FinishDeathSequence();
			npcIndex = -1;
			TravelingToDimension = true;
			loadingFrames = 0;
			loadLast = -1;
			if (!DimensionBridge.Travel()) {
				// no dimension after all: the fight continues here
				TravelingToDimension = false;
				fixedBlend = 0f;
			}
		}

		/// <summary>Called by the dimension when we arrive (and as a fallback when travelling ends).</summary>
		public static void OnArrivedInDimension()
		{
			if (!TravelingToDimension)
				return;
			TravelingToDimension = false;
			arrivedReal = stageTimer - SuckTime;
			// the burst now stands above the player in the new world
			bossWorldPos = Main.LocalPlayer.Bottom;
			Main.LocalPlayer.velocity = Vector2.Zero;
			relocateTicks = 4;
			// the first frames of a new world are heavy; don't let the game "catch up" afterwards by running
			// a burst of fast updates (that read as the explosion suddenly speeding up)
			Main.instance.ResetElapsedTime();
			settleTicks = 3;
		}

		/// <summary>Drawn by the dimension's loading screen while the worlds are swapped.</summary>
		/// <summary>
		/// Covers the whole loading screen (every pixel of the back buffer — the menu runs at its own UI scale, so its
		/// screen size is not the game's), then draws the burst in the game's own screen space, scaled to fit, so
		/// the light on the loading screen sits exactly where it stood in the world a frame earlier.
		/// </summary>
		public static void DrawLoadingScreen(SpriteBatch sb)
		{
			GraphicsDevice gd = Main.instance.GraphicsDevice;
			int bw = gd.PresentationParameters.BackBufferWidth, bh = gd.PresentationParameters.BackBufferHeight;
			Viewport old = gd.Viewport;
			gd.Viewport = new Viewport(0, 0, bw, bh);
			int gw = gameScreenW > 0 ? gameScreenW : bw, gh = gameScreenH > 0 ? gameScreenH : bh;
			int sw = Main.screenWidth, sh = Main.screenHeight;
			try {
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);
				sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(-8, -8, bw + 16, bh + 16), Color.Black);
				sb.End();
				Main.screenWidth = gw;
				Main.screenHeight = gh;
				burstMatrix = Matrix.CreateScale(bw / (float)gw, bh / (float)gh, 1f);
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, burstMatrix);
				DrawWhileLoading(sb);
				sb.End();
			}
			finally {
				Main.screenWidth = sw;
				Main.screenHeight = sh;
				gd.Viewport = old;
				burstMatrix = Matrix.Identity;
			}
		}

		private static int gameScreenW, gameScreenH;
		private static readonly System.Diagnostics.Stopwatch loadWatch = System.Diagnostics.Stopwatch.StartNew();
		private static double loadLast = -1;
		private static Matrix burstMatrix = Matrix.Identity;

		public static void DrawWhileLoading(SpriteBatch sb)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			Rectangle screen = new(-8, -8, Main.screenWidth + 16, Main.screenHeight + 16);
			sb.Draw(px, screen, Color.Black);
			loadingFrames++;
			if (!(TravelingToDimension && Current == Stage.Burst)) {
				DrawPassage(sb);
				return;
			}
			// time keeps crawling at exactly the in-world slow-motion rate, measured in real time (loading frames
			// arrive unevenly), so the burst never stalls and never has to catch up when the new world appears
			double now = loadWatch.Elapsed.TotalSeconds;
			float frames = loadLast < 0 ? 1f : MathHelper.Clamp((float)(now - loadLast) * 60f, 0f, 4f);
			loadLast = now;
			burstClock = MathF.Min(burstClock + 0.15f * frames, 90f);
			ParticleSystem.UpdateScreen(0.15f * frames);
			if (loadingFrames % 3 == 0) {
				Vector2 c = BurstCenter;
				float a = -MathHelper.PiOver2 + Main.rand.NextFloat(-1.3f, 1.3f);
				ParticleSystem.Glyph(c, a.ToRotationVector2() * Main.rand.NextFloat(6f, 24f), ReUtils.GoldRamp(Main.rand.NextFloat(0.4f, 1f)), Main.rand.NextFloat(0.9f, 1.6f), 200, true).Drag = 0.97f;
			}
			overlay = 1f;
			DrawBurst(sb);
			ParticleSystem.DrawScreen(sb);
		}

		/// <summary>The way home: a quiet black with a slow ring of glyphs.</summary>
		private static void DrawPassage(SpriteBatch sb)
		{
			Vector2 c = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
			float t = loadingFrames / 60f;
			float a = ReUtils.InverseLerp(0f, 1.5f, t);
			for (int i = 0; i < 24; i++) {
				float ang = i / 24f * MathHelper.TwoPi + t * 0.15f;
				float flick = 0.5f + 0.5f * MathF.Sin(t * 2f + i * 1.3f);
				Vector2 p = c + ang.ToRotationVector2() * 90f * UIScale;
				sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(i * 5 + (int)(t * 0.5f)), ReUtils.Gold.Additive(0.5f * a * flick), ang + MathHelper.PiOver2, new Vector2(7, 9), 1.3f * UIScale, SpriteEffects.None, 0f);
			}
			FX.Bloom(sb, c, ReUtils.DeepGold, 140f * UIScale, 0.15f * a);
		}

		/// <summary>First solid ground (or platform) below a point, within ~70 tiles.</summary>
		private static Vector2 FindGround(Vector2 from)
		{
			int x = (int)(from.X / 16f);
			int y0 = Math.Max(10, (int)(from.Y / 16f));
			for (int y = y0; y < Math.Min(Main.maxTilesY - 10, y0 + 70); y++) {
				if (!WorldGen.InWorld(x, y))
					break;
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && !tile.IsActuated && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]))
					return new Vector2(from.X, y * 16f);
			}
			// floating over a void: keep a believable "horizon" below the boss
			return from + new Vector2(0f, 160f);
		}

		/// <summary>The dimension's loop moved the world under us.</summary>
		public static void ShiftWorld(Vector2 d) => bossWorldPos += d;

		private static Vector2 BurstCenter {
			get {
				Vector2 world = WorldToScreen(bossWorldPos);
				return fixedScreen.HasValue ? Vector2.Lerp(world, fixedScreen.Value, fixedBlend) : world;
			}
		}

		private static Re Boss => npcIndex >= 0 && Main.npc[npcIndex].active ? Main.npc[npcIndex].ModNPC as Re : null;

		// ================================================================== update
		public override void PostUpdateEverything()
		{
			if (Main.dedServ || !Active)
				return;

			Re boss = Boss;
			if (boss == null && Current < Stage.Burst) {
				Abort();
				return;
			}
			if (boss != null && Current < Stage.Burst)
				bossWorldPos = boss.NPC.Center;
			if (TravelingToDimension && !DimensionBridge.IsTraveling)
				OnArrivedInDimension();
			if (settleTicks > 0 && --settleTicks >= 0)
				Main.instance.ResetElapsedTime();
			if (relocateTicks > 0) {
				// the player may be placed at the entry a tick after the stage starts
				relocateTicks--;
				bossWorldPos = Main.LocalPlayer.Bottom;
			}
			if (arrivedReal >= 0)
				fixedBlend = MathHelper.Max(0f, fixedBlend - 1f / 50f);

			stageTimer++;
			if (glitchTimer > 0)
				glitchTimer--;
			pickDarken *= 0.94f;
			levelVisual = MathHelper.Lerp(levelVisual, picks, 0.06f);
			Main.playerInventory = false;
			// during the burst the camera sits higher so the fountain has the sky to itself
			// during the burst the camera sits high: the fountain rises from the ground with the sky to itself
			Vector2 camTarget = Current == Stage.Burst ? bossWorldPos - new Vector2(0f, Main.screenHeight * 0.26f / Main.GameViewMatrix.Zoom.Y) : bossWorldPos;
			ScreenFX.Focus(camTarget, Current == Stage.Darken ? ReUtils.EaseInOutSine(stageTimer / 120f) : 1f);
			UpdateEyeAnimation();

			bool skip = ModContent.GetInstance<ReClientConfig>().SkipRiddleCutscene;

			switch (Current) {
				case Stage.Darken:
					// slow fall into black while the music fades away
					overlay = ReUtils.EaseInCubic(stageTimer / 200f);
					if (stageTimer >= 220)
						SetStage(skip ? Stage.Silence : Stage.EyesOpen);
					break;

				case Stage.EyesOpen:
					overlay = 1f;
					if (stageTimer == 1)
						SoundEngine.PlaySound(ReSounds.EyesOpen);
					if (stageTimer > 90)
						textReveal += 0.85f;
					SpawnGloom();
					if (stageTimer >= 150)
						SetStage(Stage.Riddle);
					break;

				case Stage.Riddle:
					overlay = 1f;
					textReveal += 0.85f;
					if (responseTimer > 0)
						responseTimer++;
					HandleKeyboard();
					SpawnGloom();
					if (picks >= AnswerCount && stageTimer - lastPickTimer > 190)
						SetStage(Stage.EyesClose);
					break;

				case Stage.EyesClose:
					overlay = 1f;
					if (stageTimer == 1)
						SoundEngine.PlaySound(ReSounds.EyesClose);
					if (stageTimer < 60)
						SpawnGloom();
					if (stageTimer >= 120)
						SetStage(Stage.Silence);
					break;

				case Stage.Silence:
					overlay = 1f;
					if (stageTimer == 1)
						ParticleSystem.ClearScreen();
					if (stageTimer >= 160) {
						// the explosion erupts from the ground beneath Re
						bossWorldPos = FindGround(bossWorldPos);
						SetStage(Stage.Burst);
					}
					break;

				case Stage.Burst:
					UpdateBurst();
					break;
			}

			ParticleSystem.UpdateScreen(TimeScale);
			flash *= 0.92f;
		}

		private static void UpdateEyeAnimation()
		{
			if (Current == Stage.EyesOpen) {
				int k = 9 - (int)(stageTimer / 12f);   // slow, heavy lids
				eyeRow = k >= 5 ? k : 0;
				return;
			}
			if (Current == Stage.EyesClose) {
				int k = 5 + (int)((stageTimer - 14) / 12f);
				eyeRow = stageTimer < 14 ? 0 : Math.Min(9, k);
				return;
			}
			if (Current != Stage.Riddle)
				return;
			if (--gazeTimer <= 0) {
				gazeTimer = Main.rand.Next(90, 240);
				gazeRow = Main.rand.Next(3);
				if (picks >= 3 && Main.rand.NextBool(2))
					gazeRow = 0;
			}
			if (--blinkTimer <= 0)
				blinkTimer = Main.rand.Next(200, 380) - picks * 25;
			int row = hovered >= 0 ? 3 : gazeRow;
			if (blinkTimer < 9 && picks < 5)
				row = 4;
			eyeRow = row;
		}

		private static void HandleKeyboard()
		{
			if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
				return;
			KeyboardState now = Main.keyState, old = Main.oldKeyState;
			bool Pressed(Keys k) => now.IsKeyDown(k) && !old.IsKeyDown(k);
			if (Pressed(Keys.Down) || Pressed(Keys.S))
				keyboardCursor = NextOpen(keyboardCursor, 1);
			if (Pressed(Keys.Up) || Pressed(Keys.W))
				keyboardCursor = NextOpen(keyboardCursor, -1);
			if ((Pressed(Keys.Enter) || Pressed(Keys.Space)) && keyboardCursor >= 0 && !picked[keyboardCursor])
				Pick(keyboardCursor);
		}

		private static int NextOpen(int from, int dir)
		{
			for (int step = 1; step <= AnswerCount; step++) {
				int i = ((from < 0 ? (dir > 0 ? -1 : 0) : from) + dir * step + AnswerCount * 4) % AnswerCount;
				if (!picked[i])
					return i;
			}
			return -1;
		}

		private static bool OptionsVisible => Current == Stage.Riddle && textReveal > QuestionLength + 10;
		private static int QuestionLength => Language.GetTextValue("Mods.Replicant.Riddle.Question").Length;

		private static void Pick(int index)
		{
			if (picked[index] || picks >= AnswerCount || stageTimer - lastPickTimer < 40 && picks > 0)
				return;
			picked[index] = true;
			picks++;
			if (firstPick < 0) {
				firstPick = index;
				if (index == CorrectAnswer)
					ReDownedSystem.AnsweredRiddleFirstTry = true;
			}
			lastPickTimer = stageTimer;
			glitchTimer = 18 + picks * 6;
			responseKey = picks >= AnswerCount ? 6 : index == CorrectAnswer ? 0 : picks;
			responseTimer = 1;
			if (keyboardCursor == index)
				keyboardCursor = NextOpen(index, 1);
			SoundEngine.PlaySound(ReSounds.RiddleSelect(picks));
			ScreenFX.Shake(1.5f + picks * 1.5f);
			pickDarken = 0.5f + picks * 0.08f;
			// a few dead glyphs flake off the eyes and sink
			Vector2 c = EyeCenter;
			for (int i = 0; i < 6 + picks * 4; i++)
				ParticleSystem.Glyph(c + Main.rand.NextVector2Circular(220, 50), new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), Main.rand.NextFloat(0.3f, 1.4f)), Color.Lerp(new Color(120, 110, 100), LevelColor(picks), 0.5f) * 0.7f, 1f, 90, true);
		}

		/// <summary>Grey ash drifting down through the void; it turns to dull embers as the eyes rot.</summary>
		private static void SpawnGloom()
		{
			if (!Main.rand.NextBool(3))
				return;
			Vector2 p = new(Main.rand.NextFloat(-40, Main.screenWidth + 40), -20f);
			float c = MathHelper.Clamp(levelVisual / 5f, 0f, 1f);
			Color col = Color.Lerp(new Color(70, 70, 76), new Color(90, 38, 22), Main.rand.NextFloat() < c ? 0.8f : 0f) * Main.rand.NextFloat(0.35f, 0.75f);
			ParticleSystem.Ash(p, new Vector2(Main.rand.NextFloat(-0.25f, 0.25f), Main.rand.NextFloat(0.5f, 1.3f)), col, Main.rand.NextFloat(0.5f, 1.3f), 1500);
		}

		// ================================================================== burst
		private const int SuckTime = 80;

		private static void UpdateBurst()
		{
			Vector2 center = BurstCenter;
			int t = stageTimer;
			if (t == 1)
				SoundEngine.PlaySound(ReSounds.Burst);

			if (t < SuckTime) {
				overlay = 1f;
				float k = t / (float)SuckTime;
				ScreenFX.HoldZoom(1f + 0.1f * ReUtils.EaseInCubic(k));
				ScreenFX.Shake(1f + 5f * k * k);
				for (int i = 0; i < 1 + (int)(k * 5); i++) {
					Vector2 from = center + Main.rand.NextVector2Unit() * Main.rand.NextFloat(350f, 900f);
					ParticleSystem.Glyph(from, (center - from) * 0.05f * (0.6f + k), ReUtils.GoldRamp(0.2f + k * 0.5f) * (0.4f + k * 0.6f), 0.9f, 24, true);
				}
				return;
			}

			if (t == SuckTime) {
				// ------------------------------------------------ IMPACT
				Re boss = Boss;
				if (boss != null)
					boss.Vanished = true;
				SoundEngine.PlaySound(ReSounds.Explode with { Volume = 0.8f, Pitch = -0.35f });
				flash = ModContent.GetInstance<ReClientConfig>().FlashIntensity;
				ScreenFX.Shake(40f);
				ScreenFX.ZoomPunch(0.22f);
				TimeScale = 0.15f;
				burstClock = 0f;
				BuildBurst();
				// decay: black flakes and embers thrown up and out
				for (int i = 0; i < 160; i++) {
					float a = -MathHelper.PiOver2 + Main.rand.NextFloat(-1.35f, 1.35f);
					Vector2 v = a.ToRotationVector2() * Main.rand.NextFloat(4f, 26f);
					if (i % 2 == 0)
						ParticleSystem.Ash(center, v, new Color(12, 8, 6) * 0.9f, Main.rand.NextFloat(0.8f, 2f), Main.rand.Next(160, 300)).Gravity = 0.04f;
					else
						ParticleSystem.Spark(center, v * 1.2f, ReUtils.Gold, Main.rand.NextFloat(0.8f, 2f), Main.rand.Next(60, 140), true).Drag = 0.975f;
				}
				for (int i = 0; i < 120; i++) {
					float a = -MathHelper.PiOver2 + Main.rand.NextFloat(-1.5f, 1.5f);
					ParticleSystem.Glyph(center, a.ToRotationVector2() * Main.rand.NextFloat(4f, 30f), ReUtils.GoldRamp(Main.rand.NextFloat(0.4f, 1f)), Main.rand.NextFloat(0.9f, 1.8f), Main.rand.Next(140, 260), true).Drag = 0.972f;
				}
				ParticleSystem.Flare(center, ReUtils.PaleGold, 9f, 100, true);
				streamLoop = SoundEngine.PlaySound(ReSounds.GlyphStreamLoop);
			}

			int real = t - SuckTime;
			if (real == TravelAtReal && !SpawnHandledByCutscene && Main.netMode == NetmodeID.SinglePlayer) {
				string why = DimensionBridge.WhyNot();
				if (why == null) {
					StartTravel();
					if (TravelingToDimension)
						return;
				}
				else
					DimensionBridge.Report(why);
			}
			if (real < 95)
				TimeScale = 0.15f;
			else
				TimeScale = MathHelper.Lerp(0.15f, 1f, ReUtils.EaseInOutSine((real - 95) / 200f));
			burstClock += TimeScale;

			ScreenFX.HoldZoom(1f + 0.15f * (1f - ReUtils.EaseOutCubic(real / 300f)));
			if (arrivedReal >= 0)
				// a new world under the light: the black thins away while the burst still stands at its peak
				overlay = 1f - ReUtils.EaseInOutSine((real - arrivedReal - 24) / 170f);
			else
				overlay = 1f - ReUtils.EaseInOutSine((real - 280) / 200f);
			if (real % 24 == 0 && real < 260)
				ScreenFX.Shake(9f * (1f - real / 260f));

			// the decay: as streams fade, they crumble into rising glyph ash
			if (burstClock > 150f && burstClock < 260f) {
				foreach (Stream s in streams) {
					if (s.Kind != StreamKind.Glyphs || Main.rand.NextFloat() > 0.12f)
						continue;
					Vector2 p = StreamPoint(s, center, Main.rand.NextFloat(0.2f, 1f), 1f);
					ParticleSystem.Glyph(p, new Vector2(Main.rand.NextFloat(-0.5f, 0.5f), Main.rand.NextFloat(-1.6f, -0.4f)), s.Tint * 0.8f, Main.rand.NextFloat(0.7f, 1.2f), 100, true);
				}
			}

			if (streamLoop.HasValue && SoundEngine.TryGetActiveSound(streamLoop.Value, out ActiveSound loop))
				loop.Volume = MathHelper.Clamp(1f - (real - 300) / 160f, 0f, 1f);

			if (real > 500)
				Finish();
		}

		private static void BuildBurst()
		{
			streams.Clear();
			fog.Clear();
			float H = Main.screenHeight;
			// glyph columns: a lotus-shaped fountain, tallest in the middle, outer ones bending out and down
			int n = 50;
			for (int i = 0; i < n; i++) {
				float u = (i / (float)(n - 1)) * 2f - 1f;
				float uu = MathF.Sign(u) * MathF.Pow(MathF.Abs(u), 1.25f);
				streams.Add(new Stream {
					Kind = StreamKind.Glyphs,
					Angle = -MathHelper.PiOver2 + uu * 1.42f + Main.rand.NextFloat(-0.04f, 0.04f),
					Bend = uu * Main.rand.NextFloat(0.7f, 1.3f),
					Length = H * (0.92f - 0.42f * MathF.Abs(uu)) * Main.rand.NextFloat(0.85f, 1.08f),
					Width = Main.rand.NextFloat(20f, 32f) * (1f - 0.3f * MathF.Abs(uu)),
					Scroll = Main.rand.NextFloat(0.7f, 1.3f),
					Delay = MathF.Abs(uu) * 6f + Main.rand.NextFloat(0f, 4f),
					Wobble = Main.rand.NextFloat(MathHelper.TwoPi),
					Seed = Main.rand.NextFloat(),
					Tint = Color.Lerp(ReUtils.Gold, new Color(255, 170, 60), Main.rand.NextFloat(0f, 0.6f)),
				});
			}
			// thin golden filaments woven through the fountain
			for (int i = 0; i < 90; i++) {
				float u = Main.rand.NextFloat(-1f, 1f);
				streams.Add(new Stream {
					Kind = StreamKind.Filament,
					Angle = -MathHelper.PiOver2 + u * 1.5f,
					Bend = u * Main.rand.NextFloat(0.8f, 1.9f),
					Length = H * Main.rand.NextFloat(0.4f, 1.05f),
					Width = Main.rand.NextFloat(2.5f, 5f),
					Scroll = Main.rand.NextFloat(0.5f, 2f),
					Delay = Main.rand.NextFloat(0f, 12f),
					Wobble = Main.rand.NextFloat(MathHelper.TwoPi),
					Seed = Main.rand.NextFloat(),
					Tint = Color.Lerp(ReUtils.Gold, ReUtils.PaleGold, Main.rand.NextFloat()),
				});
			}
			// great looping arcs on both flanks
			for (int i = 0; i < 16; i++) {
				float side = i % 2 == 0 ? -1f : 1f;
				streams.Add(new Stream {
					Kind = StreamKind.Loop,
					Angle = -MathHelper.PiOver2 + side * Main.rand.NextFloat(1.0f, 1.5f),
					Bend = -side * Main.rand.NextFloat(2.4f, 3.6f),
					Length = H * Main.rand.NextFloat(0.9f, 1.6f),
					Width = Main.rand.NextFloat(3f, 9f),
					Scroll = Main.rand.NextFloat(0.6f, 1.6f),
					Delay = Main.rand.NextFloat(4f, 18f),
					Wobble = Main.rand.NextFloat(MathHelper.TwoPi),
					Seed = Main.rand.NextFloat(),
					Tint = Color.Lerp(ReUtils.Gold, ReUtils.Ember, Main.rand.NextFloat(0f, 0.4f)),
				});
			}
			// ground fog rolling outward
			for (int i = 0; i < 18; i++) {
				float side = i % 2 == 0 ? -1f : 1f;
				fog.Add(new FogPuff {
					X = side * Main.rand.NextFloat(0f, 120f),
					Speed = side * Main.rand.NextFloat(1.2f, 4.5f),
					Size = Main.rand.NextFloat(0.5f, 1.2f),
					Rot = Main.rand.NextFloat(-0.15f, 0.15f),
					Delay = Main.rand.NextFloat(0f, 20f),
				});
			}
		}

		private static float StreamGrow(Stream s)
		{
			float speed = s.Kind == StreamKind.Loop ? 80f : s.Kind == StreamKind.Filament ? 55f : 48f;
			return ReUtils.EaseOutCubic((burstClock - s.Delay) / speed);
		}

		private static Vector2 StreamPoint(Stream s, Vector2 origin, float along, float grow)
		{
			float r = s.Length * along * grow;
			float bendT = s.Kind == StreamKind.Loop ? along : along * along;
			float ang = s.Angle + s.Bend * bendT * (0.4f + 0.6f * grow)
				+ MathF.Sin(burstClock * 0.025f + along * 5f + s.Wobble) * 0.05f * along;
			// integrate the curve approximately: radius shrinks as it curls (keeps loops round)
			float curlShrink = s.Kind == StreamKind.Loop ? 1f / (1f + MathF.Abs(s.Bend) * along * 0.35f) : 1f;
			Vector2 p = origin + ang.ToRotationVector2() * r * curlShrink;
			// nothing passes through the ground: anything below it is pressed flat and sweeps sideways
			if (p.Y > origin.Y + 10f)
				p.Y = origin.Y + 10f + (p.Y - origin.Y - 10f) * 0.08f;
			return p;
		}

		// ================================================================== drawing
		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!Active)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Riddle Cutscene", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static float UIScale => MathHelper.Clamp(Main.screenHeight / 1080f, 0.6f, 1.8f);
		private static Vector2 EyeCenter => new(Main.screenWidth / 2f, Main.screenHeight * 0.30f);

		private static Vector2 WorldToScreen(Vector2 world) => Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);

		private static void Draw(SpriteBatch sb)
		{
			Texture2D pixel = TextureAssets.MagicPixel.Value;
			Rectangle screen = new(0, 0, Main.screenWidth, Main.screenHeight);

			if (overlay > 0f)
				sb.Draw(pixel, screen, Color.Black * overlay);

			switch (Current) {
				case Stage.EyesOpen:
				case Stage.Riddle:
				case Stage.EyesClose:
					DrawVoid(sb);
					ParticleSystem.DrawScreen(sb);
					DrawEyes(sb);
					DrawRiddleText(sb);
					DrawGloomOverlay(sb);
					break;
				case Stage.Burst:
					DrawBurst(sb);
					ParticleSystem.DrawScreen(sb);
					break;
				default:
					ParticleSystem.DrawScreen(sb);
					break;
			}

			if (flash > 0.01f)
				sb.Draw(pixel, screen, new Color(255, 245, 225).Additive(flash));
		}

		// ------------------------------------------------------------------ the void
		private static Color LevelColor(float level)
		{
			// muted: cold steel blue → tarnished gold → rust → dried blood
			Color[] stops = { new(90, 130, 170), new(120, 140, 150), new(180, 140, 70), new(190, 110, 45), new(170, 60, 30), new(140, 25, 18) };
			int a = (int)MathHelper.Clamp(MathF.Floor(level), 0, 5);
			int b = Math.Min(5, a + 1);
			return Color.Lerp(stops[a], stops[b], level - a);
		}

		private static float SceneFade => Current == Stage.EyesOpen ? ReUtils.InverseLerp(0, 90, stageTimer) : Current == Stage.EyesClose ? ReUtils.InverseLerp(115, 60, stageTimer) : 1f;

		private static void DrawVoid(SpriteBatch sb)
		{
			float fade = SceneFade;
			float t = Main.GlobalTimeWrappedHourly;
			float c = MathHelper.Clamp(levelVisual / 5f, 0f, 1f);
			Vector2 eye = EyeCenter;
			float s = UIScale;

			// a weak, cold shaft of light from above — the only light in the room
			Texture2D ray = ReTex.GodRay;
			Color shaft = Color.Lerp(new Color(110, 120, 140), new Color(120, 60, 40), c);
			sb.Draw(ray, new Vector2(eye.X, -40f), null, shaft.Additive(0.05f * fade), 0f, new Vector2(ray.Width / 2f, 0f), new Vector2(12f * s, Main.screenHeight * 1.3f / ray.Height), SpriteEffects.None, 0f);
			sb.Draw(ray, new Vector2(eye.X, -40f), null, shaft.Additive(0.035f * fade), 0f, new Vector2(ray.Width / 2f, 0f), new Vector2(5f * s, Main.screenHeight * 1.1f / ray.Height), SpriteEffects.None, 0f);

			// slow grey fog banks (alpha-blended: they dim, they do not glow)
			Texture2D fogTex = ReTex.Fog;
			Color fogCol = Color.Lerp(new Color(26, 27, 31), new Color(34, 18, 12), c);
			for (int i = 0; i < 6; i++) {
				float speed = 6f + i * 3f;
				float x = ((t * speed + i * 520f) % (Main.screenWidth + 1200f)) - 600f;
				float y = Main.screenHeight * (0.25f + 0.13f * i) + MathF.Sin(t * 0.2f + i) * 30f;
				sb.Draw(fogTex, new Vector2(i % 2 == 0 ? x : Main.screenWidth - x, y), null, fogCol * (0.55f * fade), 0f, fogTex.Size() / 2f, new Vector2(2.6f, 1.4f) * s, SpriteEffects.None, 0f);
			}
		}

		private static void DrawGloomOverlay(SpriteBatch sb)
		{
			float fade = SceneFade;
			Rectangle screen = new(0, 0, Main.screenWidth, Main.screenHeight);
			// heavy vignette
			sb.Draw(ReTex.Vignette, screen, Color.White * (0.95f * fade));
			sb.Draw(ReTex.Vignette, screen, Color.White * (0.5f * fade));
			// darkness pulse on every answer, plus a faint, uneasy breathing flicker
			float breathe = 0.06f + 0.05f * MathF.Sin(Main.GlobalTimeWrappedHourly * 0.9f) + levelVisual * 0.015f;
			sb.Draw(TextureAssets.MagicPixel.Value, screen, Color.Black * MathHelper.Clamp(pickDarken + breathe, 0f, 0.85f));
			// film grain
			Texture2D grain = ReTex.Grain;
			Vector2 off = new(Main.rand.Next(grain.Width), Main.rand.Next(grain.Height));
			for (int x = -1; x <= Main.screenWidth / grain.Width + 1; x++) {
				for (int y = -1; y <= Main.screenHeight / grain.Height + 1; y++)
					sb.Draw(grain, new Vector2(x * grain.Width, y * grain.Height) - off, null, new Color(200, 200, 200).Additive(0.05f * fade), 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
			}
		}

		// ------------------------------------------------------------------ eyes
		private static void DrawEyes(SpriteBatch sb)
		{
			Texture2D eyes = ReTex.Eyes;
			Texture2D glow = ReTex.EyesGlow;
			const int cols = 6, rows = 10;
			int fw = eyes.Width / cols, fh = eyes.Height / rows;
			float s = 3.2f * UIScale;
			Vector2 c = EyeCenter + new Vector2(0, MathF.Sin(Main.GlobalTimeWrappedHourly * 0.8f) * 3f * UIScale);

			float appear = Current == Stage.EyesOpen ? ReUtils.InverseLerp(0, 50, stageTimer) : Current == Stage.EyesClose ? ReUtils.InverseLerp(112, 75, stageTimer) : 1f;
			int level = (int)MathF.Round(levelVisual);
			if (glitchTimer > 0 && glitchTimer % 4 < 2)
				level = Math.Max(0, picks - 1);
			level = Math.Clamp(level, 0, 5);
			Rectangle src = new(level * fw, eyeRow * fh, fw, fh);
			Vector2 origin = new(fw / 2f, fh / 2f);
			Color lc = LevelColor(levelVisual);
			float open = eyeRow >= 5 ? 1f - (eyeRow - 5) / 4f : eyeRow == 4 ? 0.5f : 1f;
			// dim, small halos — the eyes are the light source, not a lamp
			foreach (float side in new[] { -1f, 1f }) {
				Vector2 ep = c + new Vector2(side * 40f * s, 0f);
				FX.Bloom(sb, ep, lc, 46f * s, (0.07f + levelVisual * 0.02f) * appear * open, 1.6f, 0.8f);
			}
			// slightly dimmed sprite: they look tired, not radiant
			Color tint = Color.Lerp(new Color(185, 185, 190), Color.White, MathHelper.Clamp(levelVisual / 4f, 0f, 1f)) * appear;
			float glowA = (0.45f + levelVisual * 0.07f) * appear;

			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			if (glitchTimer > 0) {
				int slices = 9;
				float strength = glitchTimer / 30f;
				for (int i = 0; i < slices; i++) {
					int y0 = src.Y + fh * i / slices;
					int y1 = src.Y + fh * (i + 1) / slices;
					Rectangle part = new(src.X, y0, fw, y1 - y0);
					float offX = (ReUtils.Hash01(i, glitchTimer / 2) - 0.5f) * 60f * strength * UIScale;
					Vector2 pos = c + new Vector2(offX, (y0 - src.Y - fh / 2f) * s);
					Vector2 org = new(fw / 2f, 0f);
					sb.Draw(eyes, pos + new Vector2(-5 * strength * s, 0), part, new Color(160, 30, 30, 0) * appear, 0f, org, s, SpriteEffects.None, 0f);
					sb.Draw(eyes, pos + new Vector2(5 * strength * s, 0), part, new Color(30, 120, 150, 0) * appear, 0f, org, s, SpriteEffects.None, 0f);
					sb.Draw(eyes, pos, part, tint, 0f, org, s, SpriteEffects.None, 0f);
					sb.Draw(glow, pos, part, Color.White.Additive(glowA), 0f, org, s, SpriteEffects.None, 0f);
				}
			}
			else {
				sb.Draw(eyes, c, src, tint, 0f, origin, s, SpriteEffects.None, 0f);
				sb.Draw(glow, c, src, Color.White.Additive(glowA * (0.9f + 0.1f * MathF.Sin(Main.GlobalTimeWrappedHourly * 2f))), 0f, origin, s, SpriteEffects.None, 0f);
			}
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		}

		// ------------------------------------------------------------------ text
		private static void DrawRiddleText(SpriteBatch sb)
		{
			float s = UIScale;
			float textFade = Current == Stage.EyesClose ? ReUtils.InverseLerp(50, 0, stageTimer) : 1f;
			if (textFade <= 0f)
				return;
			DynamicSpriteFont big = FontAssets.DeathText.Value;
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			float corruption = CorruptionFor(levelVisual);
			float c01 = MathHelper.Clamp(levelVisual / 5f, 0f, 1f);
			Color textColor = Color.Lerp(new Color(176, 174, 170), new Color(150, 80, 55), c01) * textFade;
			Color lineCol = Color.Lerp(new Color(70, 72, 80), new Color(90, 40, 28), c01);

			Texture2D div = ReTex.Divider;
			float y = Main.screenHeight * 0.45f;
			sb.Draw(div, new Vector2(Main.screenWidth / 2f, y), null, lineCol * textFade, 0f, div.Size() / 2f, 2.2f * s, SpriteEffects.None, 0f);
			y += 26f * s;

			string question = Language.GetTextValue("Mods.Replicant.Riddle.Question");
			string[] lines = question.Split('\n');
			int shown = (int)textReveal;
			int consumed = 0;
			float qScale = 0.46f * s;
			foreach (string line in lines) {
				int reveal = Math.Clamp(shown - consumed, 0, line.Length);
				DrawCorruptLine(sb, big, line, new Vector2(Main.screenWidth / 2f, y), qScale, textColor, corruption, consumed * 31 + 7, reveal);
				consumed += line.Length + 1;
				y += big.LineSpacing * qScale * 0.95f;
			}

			y += 8f * s;
			if (responseTimer > 0) {
				string resp = Language.GetTextValue("Mods.Replicant.Riddle.Response" + responseKey);
				int reveal = (int)(responseTimer * 0.9f);
				Color rc = Color.Lerp(new Color(120, 124, 135), new Color(140, 50, 35), c01) * textFade * ReUtils.InverseLerp(0, 20, responseTimer);
				DrawCorruptLine(sb, small, resp, new Vector2(Main.screenWidth / 2f, y), 1.05f * s, rc, MathF.Min(1f, corruption * 1.3f), 991 + responseKey * 17, reveal);
			}
			y += 46f * s;

			if (!OptionsVisible && Current != Stage.EyesClose)
				return;
			float optFade = Current == Stage.Riddle ? ReUtils.InverseLerp(QuestionLength + 10, QuestionLength + 70, textReveal) * textFade : textFade;
			hovered = -1;
			Vector2 mouse = new(Main.mouseX, Main.mouseY);
			Texture2D atlas = ReTex.GlyphAtlasGlow;
			Texture2D px = TextureAssets.MagicPixel.Value;
			for (int i = 0; i < AnswerCount; i++) {
				string label = Language.GetTextValue("Mods.Replicant.Riddle.Answer" + i);
				float optScale = 1.1f * s;
				Vector2 size = small.MeasureString(label) * optScale;
				Vector2 pos = new(Main.screenWidth / 2f, y + i * 40f * s);
				Rectangle hit = new((int)(pos.X - size.X / 2f - 30 * s), (int)(pos.Y - 6 * s), (int)(size.X + 60 * s), (int)(34 * s));
				bool isHover = Current == Stage.Riddle && !picked[i] && optFade > 0.5f && (hit.Contains(mouse.ToPoint()) || keyboardCursor == i);
				if (isHover && hit.Contains(mouse.ToPoint()))
					keyboardCursor = -1;
				if (isHover) {
					hovered = i;
					Main.LocalPlayer.mouseInterface = true;
					if (Main.mouseLeft && Main.mouseLeftRelease)
						Pick(i);
				}

				float optCorruption = picked[i] ? MathF.Min(1f, corruption + 0.35f) : corruption * 0.6f;
				Color oc = picked[i] ? new Color(70, 64, 60) * 0.7f : isHover ? new Color(225, 220, 205) : new Color(140, 138, 134);
				oc *= optFade;
				Vector2 bullet = new(pos.X - size.X / 2f - 22f * s, pos.Y + 13f * s);
				sb.Draw(atlas, bullet, ReTex.GlyphFrame(i * 7 + 4), (isHover ? new Color(200, 170, 110) : lineCol).Additive(optFade * 0.8f), 0f, new Vector2(7, 9), 1f * s, SpriteEffects.None, 0f);
				if (isHover) {
					// a thin, quiet underline instead of a glow
					float w = size.X * ReUtils.EaseOutCubic(MathHelper.Clamp((Main.GlobalTimeWrappedHourly * 4f) % 1f + 0.6f, 0f, 1f));
					sb.Draw(px, new Rectangle((int)(pos.X - size.X / 2f), (int)(pos.Y + size.Y * 0.95f), (int)w, Math.Max(1, (int)s)), new Color(200, 180, 140) * 0.6f * optFade);
				}
				DrawCorruptLine(sb, small, label, pos, optScale, oc, optCorruption, 400 + i * 53, label.Length, isHover ? 1.2f : 0f);
				if (picked[i])
					sb.Draw(px, new Rectangle((int)(pos.X - size.X / 2f - 4), (int)(pos.Y + size.Y * 0.45f), (int)(size.X + 8), Math.Max(1, (int)(2 * s))), new Color(110, 40, 30) * 0.8f * optFade);
			}
			float yb = y + AnswerCount * 40f * s + 10f * s;
			sb.Draw(div, new Vector2(Main.screenWidth / 2f, yb), null, lineCol * textFade * optFade, MathHelper.Pi, div.Size() / 2f, 2.2f * s, SpriteEffects.None, 0f);
		}

		private static float CorruptionFor(float level)
		{
			float[] table = { 0f, 0.06f, 0.15f, 0.3f, 0.5f, 0.78f };
			int a = (int)MathHelper.Clamp(MathF.Floor(level), 0, 5);
			int b = Math.Min(5, a + 1);
			return MathHelper.Lerp(table[a], table[b], level - a);
		}

		/// <summary>
		/// Centred line where a stable, growing subset of characters is replaced by dim glyphs;
		/// corruption also adds flicker and jitter. No bloom: the text should feel like it is rotting, not shining.
		/// </summary>
		private static void DrawCorruptLine(SpriteBatch sb, DynamicSpriteFont font, string text, Vector2 centerTop, float scale, Color color, float corruption, int seed, int reveal, float hoverWave = 0f)
		{
			if (string.IsNullOrEmpty(text) || color.A == 0)
				return;
			float width = font.MeasureString(text).X * scale;
			float x0 = centerTop.X - width / 2f;
			Texture2D atlas = ReTex.GlyphAtlas;
			int flickerFrame = (int)(Main.GlobalTimeWrappedHourly * 10f);
			float lineHeight = font.LineSpacing * scale;
			float a = color.A / 255f;
			Color gc = Color.Lerp(new Color(170, 130, 70), new Color(130, 30, 22), MathHelper.Clamp(corruption * 1.3f - 0.2f, 0f, 1f));
			for (int i = 0; i < text.Length && i < reveal; i++) {
				char ch = text[i];
				float x = x0 + font.MeasureString(text.Substring(0, i)).X * scale;
				if (ch == ' ')
					continue;
				bool stable = ReUtils.Hash01(seed, i) < corruption;
				bool flicker = ReUtils.Hash01(seed, i, flickerFrame) < corruption * 0.3f;
				bool typing = i >= reveal - 1 && reveal < text.Length;
				Vector2 jitter = corruption > 0.05f && ReUtils.Hash01(seed + 3, i, flickerFrame) < corruption * 0.4f
					? new Vector2(ReUtils.Hash01(seed, i, flickerFrame + 1) - 0.5f, ReUtils.Hash01(seed, i, flickerFrame + 2) - 0.5f) * 5f * corruption * scale
					: Vector2.Zero;
				float wave = hoverWave > 0f ? MathF.Sin(Main.GlobalTimeWrappedHourly * 4f + i * 0.5f) * hoverWave * scale : 0f;
				Vector2 pos = new Vector2(x, centerTop.Y + wave) + jitter;

				if (stable || flicker || typing) {
					Rectangle g = ReTex.GlyphFrame((int)(ReUtils.Hash01(seed, i, stable ? 0 : flickerFrame) * ReTex.GlyphCount));
					float gs = lineHeight / 18f * 0.55f;
					Vector2 gp = pos + new Vector2(font.MeasureString(ch.ToString()).X * scale / 2f, lineHeight * 0.48f);
					sb.Draw(atlas, gp, g, (typing && !stable ? new Color(150, 150, 150) : gc) * a, 0f, new Vector2(7, 9), gs, SpriteEffects.None, 0f);
				}
				else {
					sb.DrawString(font, ch.ToString(), pos + new Vector2(2f, 2f) * scale, Color.Black * a * 0.9f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
					sb.DrawString(font, ch.ToString(), pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				}
			}
		}

		// ------------------------------------------------------------------ burst drawing
		private static readonly List<Vector2> pts = new(64);
		private static readonly List<Vector2> ptsShift = new(64);

		private static void DrawBurst(SpriteBatch sb)
		{
			Vector2 o = BurstCenter;
			int t = stageTimer;
			float W = Main.screenWidth;

			if (t < SuckTime) {
				float k = t / (float)SuckTime;
				// a hairline horizon and a seed of light
				FX.Bloom(sb, o, ReUtils.Gold, 600f * k, 0.25f * k, 3f, 0.04f);
				FX.Bloom(sb, o, ReUtils.PaleGold, 30f + 60f * k, 0.4f + 0.6f * k);
				FX.Flare(sb, o, Color.White, 60f + 200f * k * k, k, k * 2f, 0.6f);
				return;
			}

			float bc = burstClock;
			float fadeOut = ReUtils.InverseLerp(280f, 170f, bc);
			float hot = ReUtils.InverseLerp(80f, 0f, bc);
			Texture2D px = TextureAssets.MagicPixel.Value;

			// warm darkness behind everything (the world is still hidden here)
			sb.Draw(px, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), new Color(40, 14, 4) * (0.6f * overlay * fadeOut));

			// anime speed lines converging on the blast for the first beats
			float lines = ReUtils.InverseLerp(45f, 0f, bc);
			if (lines > 0f) {
				Texture2D st = ReTex.Streak;
				for (int i = 0; i < 48; i++) {
					float a = ReUtils.Hash01(i, 11) * MathHelper.TwoPi;
					float r = W * (0.45f + 0.5f * ReUtils.Hash01(i, 12));
					Vector2 p = o + a.ToRotationVector2() * r;
					float flick = ReUtils.Hash01(i, (int)(bc * 2f)) > 0.4f ? 1f : 0.3f;
					sb.Draw(st, p, null, new Color(255, 240, 210).Additive(0.22f * lines * flick), a, st.Size() / 2f, new Vector2(2.5f + ReUtils.Hash01(i, 13) * 3f, 0.05f), SpriteEffects.None, 0f);
				}
			}

			// god rays fanning up from the base
			Texture2D ray = ReTex.GodRay;
			for (int i = 0; i < 14; i++) {
				float a = -MathHelper.PiOver2 + (i / 13f - 0.5f) * 2.6f + MathF.Sin(bc * 0.01f + i) * 0.05f;
				float len = (0.7f + 0.6f * ReUtils.Hash01(i, 1)) * ReUtils.EaseOutCubic(bc / 50f) * Main.screenHeight / ray.Height * 1.3f;
				float flick = 0.6f + 0.4f * MathF.Sin(bc * 0.15f + i * 1.7f);
				sb.Draw(ray, o, null, ReUtils.Gold.Additive(0.13f * flick * fadeOut), a - MathHelper.PiOver2, new Vector2(ray.Width / 2f, 0f), new Vector2(3f + ReUtils.Hash01(i, 2) * 3f, len), SpriteEffects.None, 0f);
			}

			// ---- the fountain (textured primitives)
			sb.End();
			Texture2D strip = ReTex.GlyphStrip, trail = ReTex.Trail, thread = ReTex.Thread;
			float chroma = ReUtils.InverseLerp(40f, 0f, bc);
			foreach (Stream s in streams) {
				float grow = StreamGrow(s);
				if (grow <= 0.01f)
					continue;
				pts.Clear();
				int N = s.Kind == StreamKind.Loop ? 56 : 36;
				for (int i = 0; i < N; i++)
					pts.Add(StreamPoint(s, o, i / (float)(N - 1), grow));
				float decay = s.Kind == StreamKind.Glyphs ? ReUtils.InverseLerp(260f, 150f + s.Seed * 60f, bc) : fadeOut;
				float baseA = decay * ReUtils.InverseLerp(0f, 0.1f, grow);
				float length = s.Length * grow;
				Stream st = s;

				if (s.Kind == StreamKind.Glyphs) {
					float uRepeat = length / (strip.Width * (s.Width / strip.Height));
					float uStart = -bc * 0.01f * s.Scroll + s.Seed * 5f;
					Color ColorAt(float tt)
					{
						Color c = Color.Lerp(new Color(255, 250, 235), st.Tint, ReUtils.Saturate(tt * 2.2f));
						c = Color.Lerp(c, new Color(200, 90, 30), ReUtils.Saturate((tt - 0.7f) * 3f) * 0.6f);
						return c * (baseA * ReUtils.InverseLerp(1f, 0.75f, tt) * (0.75f + 0.25f * hot));
					}
					float WidthAt(float tt) => st.Width * (0.45f + 0.55f * ReUtils.Bump(MathF.Min(1f, tt * 1.3f + 0.15f)));
					if (chroma > 0f) {
						ptsShift.Clear();
						foreach (Vector2 p in pts)
							ptsShift.Add(p + new Vector2(6f * chroma, 0f));
						Primitives.DrawStrip(ptsShift, strip, tt => WidthAt(tt), tt => new Color(255, 30, 20) * (0.45f * chroma * baseA), uStart, uRepeat, worldSpace: false);
					}
					Primitives.DrawStrip(pts, strip, tt => WidthAt(tt), ColorAt, uStart, uRepeat, worldSpace: false);
					Primitives.DrawStrip(pts, thread, tt => WidthAt(tt) * 0.18f, tt => ColorAt(tt) * 0.6f, 0f, 1f, worldSpace: false);
				}
				else {
					float shimmer = 0.7f + 0.3f * MathF.Sin(bc * 0.2f + s.Seed * 20f);
					Color ColorAt(float tt) => Color.Lerp(new Color(255, 245, 220), st.Tint, ReUtils.Saturate(tt * 1.5f)) * (baseA * shimmer * ReUtils.InverseLerp(1f, 0.6f, tt) * (st.Kind == StreamKind.Loop ? 0.8f : 0.6f));
					Primitives.DrawStrip(pts, thread, tt => st.Width * (1f - tt * 0.6f), ColorAt, -bc * 0.02f * s.Scroll, 2f, worldSpace: false);
					if (s.Kind == StreamKind.Loop)
						Primitives.DrawStrip(pts, trail, tt => st.Width * 4f * (1f - tt * 0.7f), tt => ColorAt(tt) * 0.35f, -bc * 0.01f, 3f, worldSpace: false);
				}
			}
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, burstMatrix);

			// hot knots along the central columns
			foreach (Stream s in streams) {
				if (s.Kind != StreamKind.Glyphs || MathF.Abs(s.Angle + MathHelper.PiOver2) > 0.8f)
					continue;
				float grow = StreamGrow(s);
				float along = 0.32f + 0.2f * s.Seed;
				if (grow < along)
					continue;
				Vector2 p = StreamPoint(s, o, along, grow);
				float f = 0.5f + 0.5f * MathF.Sin(bc * 0.3f + s.Seed * 30f);
				FX.Bloom(sb, p, ReUtils.Gold, 26f, 0.35f * f * fadeOut);
				FX.Bloom(sb, p, Color.White, 9f, 0.6f * f * fadeOut);
			}

			// base of the fountain: white-hot seam on the ground
			float pulse = 1f + 0.08f * MathF.Sin(bc * 0.4f);
			FX.Bloom(sb, o, ReUtils.Gold, 420f * pulse, 0.55f * fadeOut, 2.6f, 0.35f);
			FX.Bloom(sb, o, ReUtils.PaleGold, 260f * pulse, 0.7f * fadeOut, 2.2f, 0.18f);
			FX.Bloom(sb, o, Color.White, 120f * pulse, 0.9f * fadeOut, 1.8f, 0.25f);
			FX.Flare(sb, o, Color.White, 520f * pulse, 0.6f * fadeOut * (0.4f + 0.6f * hot), 0f, 0.12f);

			// flattened ground shockwaves
			Texture2D ring = ReTex.Ring;
			for (int i = 0; i < 3; i++) {
				float k = ReUtils.Saturate((bc - i * 8f) / 120f);
				if (k <= 0f || k >= 1f)
					continue;
				float r = W * 1.2f * ReUtils.EaseOutCubic(k);
				sb.Draw(ring, o, null, ReUtils.PaleGold.Additive((1f - k) * 0.6f), 0f, ring.Size() / 2f, new Vector2(r * 2f / (ring.Width * 0.9f), r * 0.5f / (ring.Width * 0.9f)), SpriteEffects.None, 0f);
			}

			// rolling ground fog / dust (alpha-blended warm haze + a faint additive lift)
			Texture2D fogTex = ReTex.Fog;
			foreach (FogPuff f in fog) {
				float age = MathF.Max(0f, bc - f.Delay);
				if (age <= 0f)
					continue;
				float x = o.X + f.X + f.Speed * age * 3f;
				float grow = 0.6f + age * 0.012f;
				float a = ReUtils.InverseLerp(0f, 12f, age) * ReUtils.InverseLerp(300f, 160f, bc);
				Vector2 p = new(x, o.Y + 20f - age * 0.15f);
				Vector2 sc = new Vector2(1.4f, 0.55f) * f.Size * grow;
				sb.Draw(fogTex, p, null, new Color(230, 200, 160) * (0.4f * a), f.Rot, fogTex.Size() / 2f, sc, SpriteEffects.None, 0f);
				sb.Draw(fogTex, p, null, new Color(255, 190, 110).Additive(0.12f * a), f.Rot, fogTex.Size() / 2f, sc * 0.8f, SpriteEffects.None, 0f);
			}
		}
	}

	/// <summary>Locks the player during cutscenes/cinematics and makes them untouchable.</summary>
	public class CutscenePlayer : ModPlayer
	{
		private static bool Locked => RiddleCutscene.Active || Cinematic.Locked || HackSequence.Active;

		public override void SetControls()
		{
			if (!Locked || Player.whoAmI != Main.myPlayer)
				return;
			Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
			Player.controlJump = Player.controlUseItem = Player.controlUseTile = Player.controlHook = false;
			Player.controlMount = Player.controlQuickHeal = Player.controlQuickMana = Player.controlThrow = false;
			Player.controlSmart = Player.controlTorch = Player.controlInv = Player.controlMap = false;
			Player.releaseUseItem = false;
		}

		public override bool FreeDodge(Player.HurtInfo info) => Locked;

		public override void PostUpdate()
		{
			if (Locked && Player.whoAmI == Main.myPlayer) {
				Player.velocity.X *= 0.85f;
				Player.immune = true;
				Player.immuneNoBlink = true;
				Player.immuneTime = Math.Max(Player.immuneTime, 30);
			}
		}
	}

	/// <summary>Music: silence until the entrance roar, silence as Re dies, dark ambience under the riddle.</summary>
	public class CutsceneMusic : ModSceneEffect
	{
		public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;

		public override int Music => RiddleCutscene.Current switch {
			RiddleCutscene.Stage.EyesOpen or RiddleCutscene.Stage.Riddle or RiddleCutscene.Stage.EyesClose => MusicLoader.GetMusicSlot(Mod, "Assets/Music/RiddleAmbience"),
			RiddleCutscene.Stage.Burst => MusicLoader.GetMusicSlot(Mod, "Assets/Music/RebirthSwell"),
			_ => MusicLoader.GetMusicSlot(Mod, "Assets/Music/Silence"),
		};

		public override bool IsSceneEffectActive(Player player) => RiddleCutscene.Active || Cinematic.SilenceMusic;
	}
}
