using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Replicant.Content.Projectiles;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// "The Score", reworked into a duel of rhythm. Refrain plants a music stand; it pulls the player in front
	/// of it, and a score of arrows is projected above it, one row at a time, with a fast timer under each row.
	/// Press the arrows (arrow keys or WASD) in order before the timer runs out.
	///
	/// While you play, nothing else can hurt you and everything near the stand fades out, so you can read.
	/// Versions vary every time: 1–3 rows, 4–7 notes, and after the awakening also INVERSE (press the opposite)
	/// and FADING (the arrows vanish once shown) scores.
	/// Clean performance: the notes are played back into Refrain. Too many mistakes: the stand breaks into you.
	/// </summary>
	public class RhythmStand : ModSystem
	{
		public enum Version { Plain, Inverse, Fading }

		private static bool active;
		private static Vector2 standPos;
		private static int timer;
		private static int pullTicks;
		private static readonly List<int[]> rows = new();
		private static int row, index, mistakes, rowTimer, rowLimit;
		private static Version version;
		private static int[] lit = Array.Empty<int>();       // per note of the current row: 0 waiting, 1 hit, 2 missed
		private static float shake, flash;
		private static int endTimer = -1;
		private static bool success;
		private static int refrainIndex = -1;
		private static int notesPlayed;
		private static int side = 1;           // which side of the stand the sphere hangs on
		private static Vector2 sphereFrom;     // where the player was taken from (for the pull trail)
		/// <summary>Where the player hangs: in a sphere of light beside the stand, at the height of the score.</summary>
		public static Vector2 SpherePos => standPos + new Vector2(side * 130f, -70f);

		public static bool Active => active;
		public static Vector2 StandPosition => standPos;
		public const float QuietRadius = 340f;

		/// <summary>Projectiles near the stand fade out while the score is played.</summary>
		public static bool InQuietZone(Vector2 world) => active && Vector2.DistanceSquared(world, standPos) < QuietRadius * QuietRadius;

		public static void Begin(Vector2 at, int rowCount, int notes, Version v, int refrain)
		{
			if (Main.dedServ || active)
				return;
			active = true;
			standPos = at;
			Player lp = Main.LocalPlayer;
			side = lp.Center.X < at.X ? -1 : 1;
			sphereFrom = lp.Center;
			Seize(lp);
			timer = 0;
			pullTicks = 40;
			version = v;
			refrainIndex = refrain;
			rows.Clear();
			for (int r = 0; r < rowCount; r++) {
				int[] seq = new int[notes + (r == rowCount - 1 && rowCount > 1 ? 1 : 0)];
				for (int i = 0; i < seq.Length; i++)
					seq[i] = Main.rand.Next(4);
				rows.Add(seq);
			}
			row = 0;
			mistakes = 0;
			notesPlayed = 0;
			endTimer = -1;
			StartRow();
			SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Bell with { Pitch = 0.3f }, at);
			ParticleSystem.Ring(at, ReUtils.PaleGold, 0.05f, 1.12f, 26);
		}

		private static void StartRow()
		{
			index = 0;
			lit = new int[rows[row].Length];
			bool awakened = Content.NPCs.Twins.TwinsFight.Stage == Content.NPCs.Twins.TwinStage.True;
			rowLimit = (int)((50 + rows[row].Length * (awakened ? 17 : 22)) * (version == Version.Fading ? 1.15f : 1f));
			rowTimer = 0;
		}

		/// <summary>Free the player of anything that would move them: mounts, hooks, ropes, seats, wings, channelled items.</summary>
		private static void Seize(Player p)
		{
			if (p.mount.Active)
				p.mount.Dismount(p);
			p.RemoveAllGrapplingHooks();
			p.pulley = false;
			p.sitting.isSitting = false;
			p.sleeping.isSleeping = false;
			p.gravDir = 1f;
			p.channel = false;
			p.wingTime = Math.Max(p.wingTime, 1f);
			p.rocketTime = Math.Max(p.rocketTime, 1);
			p.gfxOffY = 0f;
			p.fallStart = (int)(p.position.Y / 16f);
		}

		public override void OnWorldUnload() => active = false;

		/// <summary>Story beats take over: put the score away.</summary>
		public static void Abort() => active = false;

		// ------------------------------------------------------------------ update
		public override void PostUpdateEverything()
		{
			if (!active)
				return;
			timer++;
			shake *= 0.85f;
			flash *= 0.9f;
			Player p = Main.LocalPlayer;
			if (p.dead) {
				active = false;
				return;
			}
			// taken out of whatever they were doing — flying, mounted, hooked, sitting — and held in the sphere
			Vector2 spot = SpherePos + new Vector2(0f, MathF.Sin(timer * 0.06f) * 3f);
			Seize(p);
			if (pullTicks > 0) {
				pullTicks--;
				p.Center = Vector2.Lerp(p.Center, spot, 0.14f);
				if (timer % 2 == 0)
					ParticleSystem.Glyph(Vector2.Lerp(p.Center, standPos, Main.rand.NextFloat()), Main.rand.NextVector2Circular(1f, 1f), ReUtils.Gold, 0.8f, 20);
			}
			else {
				p.Center = Vector2.Lerp(p.Center, spot, 0.35f);
			}
			p.direction = -side;
			p.velocity = Vector2.Zero;
			p.fallStart = (int)(p.position.Y / 16f);

			if (endTimer >= 0) {
				if (++endTimer >= 50)
					active = false;
				return;
			}
			if (pullTicks > 0)
				return;
			rowTimer++;
			int pressed = ReadPress();
			if (pressed >= 0) {
				int want = rows[row][index];
				if (version == Version.Inverse)
					want = Opposite(want);
				if (pressed == want) {
					lit[index] = 1;
					notesPlayed++;
					SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Note(index + row * 2) with { Volume = 0.6f, Pitch = -0.3f + index * 0.12f }, standPos);
					ParticleSystem.Flare(NotePos(index, rows[row].Length), ReUtils.PaleGold, 1.2f, 14);
					index++;
				}
				else {
					lit[index] = 2;
					Mistake();
					index++;
				}
				if (index >= rows[row].Length)
					FinishRow(clean: Array.IndexOf(lit, 2) < 0);
			}
			else if (rowTimer >= rowLimit) {
				for (int i = index; i < lit.Length; i++)
					lit[i] = 2;
				Mistake();
				FinishRow(clean: false);
			}
		}

		private static int Opposite(int d) => d switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

		/// <summary>0 up, 1 down, 2 left, 3 right; -1 nothing new pressed this tick.</summary>
		private static int ReadPress()
		{
			KeyboardState k = Main.keyState, o = Main.oldKeyState;
			bool Down(Keys a, Keys b) => (k.IsKeyDown(a) && !o.IsKeyDown(a)) || (k.IsKeyDown(b) && !o.IsKeyDown(b));
			if (Down(Keys.Up, Keys.W)) return 0;
			if (Down(Keys.Down, Keys.S)) return 1;
			if (Down(Keys.Left, Keys.A)) return 2;
			if (Down(Keys.Right, Keys.D)) return 3;
			return -1;
		}

		private static void Mistake()
		{
			mistakes++;
			shake = 10f;
			flash = 1f;
			SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Clink with { Volume = 0.9f, Pitch = -0.7f }, standPos);
			Player p = Main.LocalPlayer;
			int dmg = Math.Max(10, (int)(p.statLifeMax2 * 0.07f));
			p.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromKey("Mods.Replicant.Twins.ScoreDeath", p.name)), dmg, 0, dodgeable: false, cooldownCounter: -1);
		}

		private static void FinishRow(bool clean)
		{
			// the row's notes leave the stand: toward Refrain if played, into you if not
			int n = rows[row].Length;
			for (int i = 0; i < n; i++) {
				Vector2 from = NotePos(i, n);
				ParticleSystem.Glyph(from, new Vector2(Main.rand.NextFloat(-2f, 2f), -3f), lit[i] == 1 ? ReUtils.PaleGold : new Color(255, 70, 40), 1.3f, 30);
			}
			SoundEngine.PlaySound(clean ? Content.NPCs.Twins.TwinSounds.Chain with { Pitch = 0.4f } : Content.NPCs.Twins.TwinSounds.Shatter with { Pitch = -0.4f }, standPos);
			row++;
			if (row >= rows.Count || mistakes >= 4) {
				End(mistakes <= rows.Count);
				return;
			}
			StartRow();
		}

		private static void End(bool won)
		{
			success = won;
			endTimer = 0;
			ScreenFX.Shake(won ? 10f : 16f);
			if (won) {
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.6f, Pitch = 0.4f }, standPos);
				if (refrainIndex >= 0 && Main.npc[refrainIndex].active && Main.npc[refrainIndex].ModNPC is TwinBase t) {
					NPC n = Main.npc[refrainIndex];
					int dmg = (int)(n.lifeMax * (0.012f * notesPlayed));
					n.SimpleStrikeNPC(Math.Max(1, dmg), 0);
					t.Stunned = Math.Max(t.Stunned, 120);
					for (int i = 0; i < 24; i++)
						ParticleSystem.Glyph(standPos, (n.Center - standPos) / 20f + Main.rand.NextVector2Circular(3f, 3f), ReUtils.PaleGold, 1.2f, 24);
				}
				Subtitles.Push(Subtitles.Refrain, "Mods.Replicant.Twins.Lines.ScoreWin", 180);
			}
			else {
				SoundEngine.PlaySound(ReSounds.Explode with { Volume = 0.7f, Pitch = 0.2f }, standPos);
				Player p = Main.LocalPlayer;
				p.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromKey("Mods.Replicant.Twins.ScoreDeath", p.name)), (int)(p.statLifeMax2 * 0.2f), 0, dodgeable: false);
				ParticleSystem.GlyphBurst(standPos, 50, 10f, 1.2f);
				Subtitles.Push(Subtitles.Refrain, "Mods.Replicant.Twins.Lines.ScoreLose", 180);
			}
		}

		// ------------------------------------------------------------------ drawing
		private static Vector2 PanelCenter => standPos + new Vector2(0, -196f);

		private static Vector2 NotePos(int i, int n) => PanelCenter + new Vector2((i - (n - 1) / 2f) * 46f, 0f) + Main.rand.NextVector2Circular(shake, shake) * 0f;

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!active)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Score", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.Game));
		}

		// 7x7 pixel arrow pointing up
		private static readonly string[] Arrow = {
			"...#...",
			"..###..",
			".#####.",
			"#######",
			"..###..",
			"..###..",
			"..###..",
		};

		private static void DrawArrow(SpriteBatch sb, Vector2 center, int dir, float px, Color fill, Color edge, float a)
		{
			Texture2D pix = TextureAssets.MagicPixel.Value;
			for (int y = 0; y < 7; y++) {
				for (int x = 0; x < 7; x++) {
					if (Arrow[y][x] != '#')
						continue;
					// rotate the up-arrow grid to the wanted direction
					int gx = x - 3, gy = y - 3;
					(int rx, int ry) = dir switch { 0 => (gx, gy), 1 => (gx, -gy), 2 => (gy, gx), _ => (-gy, gx) };
					Vector2 p = center + new Vector2(rx, ry) * px;
					bool border = IsBorder(x, y);
					sb.Draw(pix, new Rectangle((int)(p.X - px / 2f), (int)(p.Y - px / 2f), (int)MathF.Ceiling(px), (int)MathF.Ceiling(px)), (border ? edge : fill) * a);
				}
			}
		}

		private static bool IsBorder(int x, int y)
		{
			bool Filled(int xx, int yy) => xx >= 0 && yy >= 0 && xx < 7 && yy < 7 && Arrow[yy][xx] == '#';
			return !Filled(x - 1, y) || !Filled(x + 1, y) || !Filled(x, y - 1) || !Filled(x, y + 1);
		}

		/// <summary>
		/// The sphere the player is held in: a pixel-edged bubble of gold light with a staff of five lines
		/// running through it, notes orbiting, and a soft inner glow — formed from rings closing in.
		/// </summary>
		private static void DrawSphere(SpriteBatch sb, Vector2 c, float fade, float t)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			float form = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 30, timer));
			float r = 46f * form;
			if (r < 2f)
				return;
			if (timer < 30)
				FX.Ring(sb, c, ReUtils.Gold, r * (1f + (1f - form) * 3f), 0.4f * fade);
			FX.Bloom(sb, c, ReUtils.Gold, r * 1.6f, 0.22f * fade);
			// pixel circle: dots on a ring, stepped like a sprite
			int n = 48;
			for (int i = 0; i < n; i++) {
				float a = i / (float)n * MathHelper.TwoPi + t * 0.3f;
				Vector2 q = c + a.ToRotationVector2() * r;
				q = new Vector2(MathF.Round(q.X / 2f) * 2f, MathF.Round(q.Y / 2f) * 2f);
				float lit = 0.55f + 0.45f * MathF.Sin(a * 3f - t * 4f);
				sb.Draw(px, new Rectangle((int)q.X - 1, (int)q.Y - 1, 3, 3), Color.Lerp(ReUtils.Gold, ReUtils.PaleGold, lit).Additive(0.9f * fade));
			}
			// highlight arc, top-left, like glass
			for (int i = 0; i < 10; i++) {
				float a = MathHelper.Pi * 1.15f + i * 0.07f;
				Vector2 q = c + a.ToRotationVector2() * (r - 7f);
				sb.Draw(px, new Rectangle((int)q.X, (int)q.Y, 2, 2), Color.White.Additive(0.6f * fade));
			}
			// the staff through it
			for (int k = 0; k < 5; k++) {
				float y = (k - 2) * 7f;
				float half = MathF.Sqrt(MathF.Max(0f, r * r - y * y)) - 4f;
				sb.Draw(px, new Rectangle((int)(c.X - half), (int)(c.Y + y), (int)(half * 2f), 1), ReUtils.Gold.Additive(0.22f * fade));
			}
			// notes in orbit
			for (int i = 0; i < 4; i++) {
				float a = t * 1.6f + i * MathHelper.PiOver2;
				Vector2 q = c + new Vector2(MathF.Cos(a) * (r + 10f), MathF.Sin(a) * (r + 10f) * 0.45f);
				sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(i * 3 + 11), ReUtils.PaleGold.Additive(0.75f * fade), 0f, new Vector2(7, 9), 0.85f, SpriteEffects.None, 0f);
			}
			// a thread to the stand: the player is "plugged in" to the score
			Vector2 stand = standPos - Main.screenPosition + new Vector2(0, -30f);
			for (float k = 0f; k < 1f; k += 0.05f) {
				Vector2 q = Vector2.Lerp(c, stand, k) + new Vector2(0, MathF.Sin(k * MathHelper.Pi) * -14f);
				float pulse = 0.5f + 0.5f * MathF.Sin(k * 20f - t * 8f);
				sb.Draw(px, new Rectangle((int)q.X, (int)q.Y, 2, 2), ReUtils.Gold.Additive(0.5f * pulse * fade));
			}
		}

		private static void Draw(SpriteBatch sb)
		{
			float t = Main.GlobalTimeWrappedHourly;
			Texture2D pix = TextureAssets.MagicPixel.Value;
			Vector2 sp = Main.screenPosition;
			float fade = endTimer >= 0 ? 1f - endTimer / 50f : ReUtils.InverseLerp(0, 20, timer);
			Vector2 c = PanelCenter - sp + Main.rand.NextVector2Circular(shake, shake);
			int n = rows.Count > 0 && row < rows.Count ? rows[row].Length : 4;
			float w = n * 46f + 50f, h = 96f;
			DrawSphere(sb, SpherePos - sp, fade, t);
			// the projected sheet: a panel of light rising from the stand's page
			Vector2 standTop = standPos - sp + new Vector2(0, -40f);
			for (int i = 0; i < 6; i++) {
				float k = i / 5f;
				Vector2 q = Vector2.Lerp(standTop, c + new Vector2(0, h / 2f), k);
				FX.Bloom(sb, q, ReUtils.Gold, 30f + k * 40f, 0.06f * fade, 1.5f, 0.6f);
			}
			Rectangle panel = new((int)(c.X - w / 2f), (int)(c.Y - h / 2f), (int)w, (int)h);
			sb.Draw(pix, panel, new Color(14, 8, 5) * (0.85f * fade));
			// pixel frame with corner notches
			Color frame = Color.Lerp(ReUtils.Gold, new Color(255, 70, 40), flash);
			sb.Draw(pix, new Rectangle(panel.X, panel.Y, panel.Width, 2), frame * fade);
			sb.Draw(pix, new Rectangle(panel.X, panel.Bottom - 2, panel.Width, 2), frame * fade);
			sb.Draw(pix, new Rectangle(panel.X, panel.Y, 2, panel.Height), frame * fade);
			sb.Draw(pix, new Rectangle(panel.Right - 2, panel.Y, 2, panel.Height), frame * fade);
			foreach (Point corner in new[] { new Point(panel.X - 4, panel.Y - 4), new Point(panel.Right - 2, panel.Y - 4), new Point(panel.X - 4, panel.Bottom - 2), new Point(panel.Right - 2, panel.Bottom - 2) })
				sb.Draw(pix, new Rectangle(corner.X, corner.Y, 6, 6), ReUtils.PaleGold * fade);
			// five faint staff lines
			for (int k = 0; k < 5; k++)
				sb.Draw(pix, new Rectangle(panel.X + 10, (int)(c.Y - 24 + k * 12), panel.Width - 20, 1), ReUtils.Gold * (0.18f * fade));

			// header: row x/y and the version
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			string head = Language.GetTextValue("Mods.Replicant.Twins.Score.Row", Math.Min(row + 1, rows.Count), rows.Count);
			if (version != Version.Plain)
				head += "  ·  " + Language.GetTextValue("Mods.Replicant.Twins.Score." + version);
			Vector2 hs = font.MeasureString(head) * 0.8f;
			sb.DrawString(font, head, new Vector2(c.X - hs.X / 2f, panel.Y - 26f), (version == Version.Inverse ? new Color(255, 110, 70) : ReUtils.PaleGold) * fade, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);

			if (row < rows.Count && endTimer < 0 && pullTicks <= 0) {
				int[] seq = rows[row];
				for (int i = 0; i < seq.Length; i++) {
					Vector2 p = NotePos(i, seq.Length) - sp + Main.rand.NextVector2Circular(shake, shake);
					bool current = i == index;
					float bob = current ? MathF.Sin(t * 10f) * 2f : 0f;
					bool hidden = version == Version.Fading && rowTimer > 40 && lit[i] == 0 && !current;
					Color fill, edge;
					if (lit[i] == 1) { fill = ReUtils.PaleGold; edge = Color.White; }
					else if (lit[i] == 2) { fill = new Color(120, 20, 10); edge = new Color(255, 70, 40); }
					else if (current) { fill = Color.Lerp(ReUtils.Gold, Color.White, 0.5f + 0.5f * MathF.Sin(t * 12f)); edge = Color.White; }
					else { fill = new Color(40, 24, 10); edge = ReUtils.Gold; }
					if (hidden) {
						// fading score: only a glyph remains where the arrow was
						sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(i * 7 + row), ReUtils.Gold.Additive(0.5f * fade), 0f, new Vector2(7, 9), 1.2f, SpriteEffects.None, 0f);
						continue;
					}
					if (lit[i] == 1 || current)
						FX.Bloom(sb, p, lit[i] == 1 ? ReUtils.PaleGold : ReUtils.Gold, 30f, (current ? 0.35f : 0.25f) * fade);
					DrawArrow(sb, p + new Vector2(0, bob), seq[i], 4f, fill, edge, fade);
				}
				// the timer: a pixel bar under the row, burning down from gold to red
				float k2 = 1f - rowTimer / (float)rowLimit;
				int bw = panel.Width - 30;
				Rectangle bar = new(panel.X + 15, panel.Bottom - 14, bw, 6);
				sb.Draw(pix, bar, new Color(30, 16, 8) * fade);
				Color bc = k2 > 0.5f ? ReUtils.Gold : k2 > 0.25f ? ReUtils.Ember : new Color(255, 60, 40);
				sb.Draw(pix, new Rectangle(bar.X, bar.Y, (int)(bw * k2), bar.Height), bc * fade);
				for (int s = 0; s < bw; s += 8)
					sb.Draw(pix, new Rectangle(bar.X + s, bar.Y, 1, bar.Height), Color.Black * (0.5f * fade));
			}
			// mistakes as cracked pips
			for (int m = 0; m < 4; m++)
				sb.Draw(pix, new Rectangle((int)(panel.Right - 16 - m * 12), panel.Y + 6, 8, 8), (m < mistakes ? new Color(255, 60, 40) : new Color(60, 36, 16)) * fade);
			// the quiet zone's edge
			FX.Ring(sb, standPos - sp, ReUtils.Gold, QuietRadius, 0.12f * fade);
			if (endTimer >= 0) {
				string res = Language.GetTextValue(success ? "Mods.Replicant.Twins.Score.Clean" : "Mods.Replicant.Twins.Score.Broken");
				Vector2 rs = FontAssets.DeathText.Value.MeasureString(res) * 0.7f;
				sb.DrawString(FontAssets.DeathText.Value, res, c - rs / 2f, (success ? ReUtils.PaleGold : new Color(255, 80, 50)) * fade, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
			}
		}
	}

	/// <summary>While the score is played: untouchable, and the bullets around the stand fade out.</summary>
	public class RhythmProjectileFade : GlobalProjectile
	{
		public override bool PreDraw(Projectile projectile, ref Color lightColor)
		{
			if (!RhythmStand.InQuietZone(projectile.Center) || projectile.ModProjectile is not IReProjectile || projectile.ModProjectile is Content.Projectiles.Twins.MusicStand)
				return true;
			FX.Bloom(Main.spriteBatch, projectile.Center - Main.screenPosition, ReUtils.Gold, 10f, 0.15f);
			return false;
		}
	}

	public class RhythmPlayer : ModPlayer
	{
		public override bool CanBeHitByProjectile(Projectile proj) => !RhythmStand.Active;

		public override void SetControls()
		{
			if (!RhythmStand.Active || Player.whoAmI != Main.myPlayer)
				return;
			// your hands are on the score, not your weapon (the arrow keys are read directly)
			Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
			Player.controlJump = Player.controlUseItem = Player.controlUseTile = Player.controlHook = false;
			Player.controlMount = Player.controlThrow = false;
		}
		public override bool CanBeHitByNPC(NPC npc, ref int cooldownSlot) => !RhythmStand.Active;
	}
}
