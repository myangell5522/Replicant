using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
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
	/// The Intrusion. Refrain reaches into the player; the screen fills with interference, the music cuts out,
	/// and the player is dropped into the sisters' own code: a small black-and-gold hacking game.
	/// Steer with WASD / arrows, fire with the left mouse button (aimed at the cursor; auto-aims at the core
	/// if you don't). Break the gold blocks of the turning shell (black ones can't be broken — the same rule
	/// as the spheres outside) and destroy the core before it overwrites you.
	///
	/// Win: both sisters are thrown open (stunned, struck). Lose: you are overwritten (heavy damage, confused).
	/// The world outside is frozen while you are inside.
	/// </summary>
	public class HackSequence : ModSystem
	{
		private enum Phase { None, Enter, Play, Exit }
		private static Phase phase;
		private static int timer;
		private static bool won;
		private static float savedMusicVolume = -1f;

		// ---- the game, in arena units (-1..1)
		private static Vector2 ship;
		private static int shipHp, shipInvuln, fireCooldown;
		private static int coreHp;
		private const int CoreMaxHp = 30, ShipMaxHp = 5, TimeLimit = 60 * 45, EnterLength = 150, Grace = 75;
		private static int dashTime, dashCooldown, hintTimer;
		private static bool lastRight;
		private static Vector2 dashDir;
		private static readonly List<(Vector2 pos, float life)> trail = new();
		private static float ringRot;
		private const int Segments = 16;
		private static readonly int[] segHp = new int[Segments];       // -1 = unbreakable (black), 0 = gone
		private struct Bullet { public Vector2 Pos, Vel; public bool Dark; }
		private static readonly List<Bullet> bullets = new();
		private static readonly List<Bullet> shots = new();
		private static readonly List<(Vector2 pos, float life, bool gold)> bursts = new();
		private static int playTime;
		private static float shake;

		public static bool Active => phase != Phase.None;
		/// <summary>The world is frozen (twins, their projectiles) while this is true.</summary>
		public static bool FreezeWorld => Active;

		public static void Begin(TwinBase hacker)
		{
			if (Active || Main.dedServ)
				return;
			phase = Phase.Enter;
			timer = 0;
			won = false;
			// the music freezes
			savedMusicVolume = Main.musicVolume;
			Main.musicVolume = 0f;
			SoundEngine.PlaySound(ReSounds.RiddleSelect(5) with { Volume = 0.9f, Pitch = -0.4f });
			SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Clink with { Volume = 1f, Pitch = -0.8f });
			ResetGame();
		}

		private static void ResetGame()
		{
			ship = new Vector2(0f, 0.75f);
			dashTime = dashCooldown = 0;
			hintTimer = 0;
			trail.Clear();
			shipHp = ShipMaxHp;
			shipInvuln = 60;
			coreHp = CoreMaxHp;
			for (int i = 0; i < Segments; i++)
				segHp[i] = i % 4 == 0 ? -1 : 2;
			bullets.Clear();
			shots.Clear();
			bursts.Clear();
			playTime = 0;
			ringRot = 0f;
		}

		private static void RestoreMusic()
		{
			if (savedMusicVolume >= 0f) {
				Main.musicVolume = savedMusicVolume;
				savedMusicVolume = -1f;
			}
		}

		public override void OnWorldUnload()
		{
			RestoreMusic();
			phase = Phase.None;
		}

		public override void Unload() => RestoreMusic();

		// ------------------------------------------------------------------ update
		public override void PostUpdateEverything()
		{
			if (!Active)
				return;
			timer++;
			shake *= 0.85f;
			Main.playerInventory = false;
			if (savedMusicVolume >= 0f)
				Main.musicVolume = 0f;
			switch (phase) {
				case Phase.Enter:
					if (timer % (timer < 90 ? 6 : 10) == 0)
						SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Clink with { Volume = 0.3f, Pitch = Main.rand.NextFloat(-1f, 0.5f) });
					GlitchPlayer.Apply(Main.LocalPlayer, 20);
					if (timer == 100) {
						SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.5f, Pitch = 0.6f });
						shake = 18f;
					}
					if (timer >= EnterLength) {
						phase = Phase.Play;
						timer = 0;
					}
					break;
				case Phase.Play:
					UpdateGame();
					break;
				case Phase.Exit:
					if (timer >= 90)
						Finish();
					break;
			}
		}

		private static void UpdateGame()
		{
			playTime++;
			float hpK = coreHp / (float)CoreMaxHp;
			bool rage = hpK < 0.5f;
			ringRot += rage ? 0.022f : 0.012f;
			if (shipInvuln > 0)
				shipInvuln--;

			// ---- input (read raw: the player's normal controls are locked during the intrusion)
			KeyboardState k = Main.keyState;
			Vector2 move = Vector2.Zero;
			if (k.IsKeyDown(Keys.A) || k.IsKeyDown(Keys.Left)) move.X -= 1f;
			if (k.IsKeyDown(Keys.D) || k.IsKeyDown(Keys.Right)) move.X += 1f;
			if (k.IsKeyDown(Keys.W) || k.IsKeyDown(Keys.Up)) move.Y -= 1f;
			if (k.IsKeyDown(Keys.S) || k.IsKeyDown(Keys.Down)) move.Y += 1f;
			if (move != Vector2.Zero)
				move.Normalize();
			if (dashCooldown > 0)
				dashCooldown--;
			bool dashKey = k.IsKeyDown(Keys.Space) || k.IsKeyDown(Keys.LeftShift) || Main.mouseRight;
			bool dashWas = Main.oldKeyState.IsKeyDown(Keys.Space) || Main.oldKeyState.IsKeyDown(Keys.LeftShift) || lastRight;
			lastRight = Main.mouseRight;
			if (dashKey && !dashWas && dashCooldown <= 0) {
				dashDir = move != Vector2.Zero ? move : -ship.SafeNormalize(Vector2.UnitY);
				dashTime = 9;
				dashCooldown = 40;
				shipInvuln = Math.Max(shipInvuln, 18);
				SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Clink with { Volume = 0.6f, Pitch = 0.9f });
			}
			if (dashTime > 0) {
				dashTime--;
				ship += dashDir * 0.055f;
				trail.Add((ship, 1f));
			}
			else
				ship += move * 0.021f;
			for (int i = trail.Count - 1; i >= 0; i--) {
				var tr = trail[i];
				tr.life -= 0.07f;
				if (tr.life <= 0f)
					trail.RemoveAt(i);
				else
					trail[i] = tr;
			}
			hintTimer++;
			ship = Vector2.Clamp(ship, new Vector2(-0.95f), new Vector2(0.95f));

			// ---- fire
			if (--fireCooldown <= 0) {
				fireCooldown = 7;
				Vector2 aim = Main.mouseLeft ? ScreenToArena(new Vector2(Main.mouseX, Main.mouseY)) - ship : -ship;
				if (aim.LengthSquared() < 0.0001f)
					aim = -Vector2.UnitY;
				aim.Normalize();
				shots.Add(new Bullet { Pos = ship, Vel = aim * 0.045f });
				if (playTime % 14 == 0)
					SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Note(playTime / 14) with { Volume = 0.12f, Pitch = 0.6f });
			}

			// ---- the core sings back (a moment of grace first, and it starts slow)
			float ramp = MathHelper.Lerp(0.55f, 1f, ReUtils.InverseLerp(Grace, Grace + 900, playTime));
			int every = rage ? 5 : 7;
			if (playTime > Grace && playTime % every == 0) {
				float a = playTime * (rage ? 0.17f : 0.11f);
				int arms = rage ? 4 : 3;
				for (int i = 0; i < arms; i++) {
					float ang = a + MathHelper.TwoPi * i / arms;
					bool dark = (playTime / every) % 3 == 0;
					bullets.Add(new Bullet { Pos = ang.ToRotationVector2() * 0.12f, Vel = ang.ToRotationVector2() * (rage ? 0.0105f : 0.009f) * ramp, Dark = dark });
				}
			}
			if (playTime > Grace * 3 && playTime % 110 == 45) {
				Vector2 d = ship.SafeDirectionTo(Vector2.Zero) * -1f;
				for (int i = -2; i <= 2; i++)
					bullets.Add(new Bullet { Pos = d * 0.12f, Vel = d.RotatedBy(i * 0.22f) * 0.014f * ramp, Dark = false });
			}

			// ---- shots
			for (int i = shots.Count - 1; i >= 0; i--) {
				Bullet s = shots[i];
				s.Pos += s.Vel;
				bool dead = MathF.Abs(s.Pos.X) > 1.05f || MathF.Abs(s.Pos.Y) > 1.05f;
				float r = s.Pos.Length();
				// the shell
				if (!dead && r > 0.25f && r < 0.31f) {
					int seg = SegmentAt(s.Pos);
					if (segHp[seg] != 0) {
						dead = true;
						if (segHp[seg] > 0) {
							segHp[seg]--;
							bursts.Add((s.Pos, 1f, true));
							if (segHp[seg] == 0)
								SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Shatter with { Volume = 0.35f, Pitch = 0.4f });
						}
						else
							bursts.Add((s.Pos, 0.6f, false));
					}
				}
				// the core
				if (!dead && r < 0.1f) {
					dead = true;
					coreHp--;
					shake = 6f;
					bursts.Add((s.Pos, 1f, true));
					SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Hurt with { Volume = 0.25f, Pitch = 0.8f });
					if (coreHp <= 0) {
						EndGame(true);
						return;
					}
				}
				// gold notes can be shot down
				if (!dead) {
					for (int j = bullets.Count - 1; j >= 0; j--) {
						if (!bullets[j].Dark && Vector2.DistanceSquared(bullets[j].Pos, s.Pos) < 0.03f * 0.03f) {
							bursts.Add((bullets[j].Pos, 0.7f, true));
							bullets.RemoveAt(j);
							dead = true;
							break;
						}
					}
				}
				if (dead)
					shots.RemoveAt(i);
				else
					shots[i] = s;
			}

			// ---- bullets
			for (int i = bullets.Count - 1; i >= 0; i--) {
				Bullet b = bullets[i];
				b.Pos += b.Vel;
				if (MathF.Abs(b.Pos.X) > 1.1f || MathF.Abs(b.Pos.Y) > 1.1f) {
					bullets.RemoveAt(i);
					continue;
				}
				bullets[i] = b;
				float hit = b.Dark ? 0.038f : 0.03f;
				if (shipInvuln <= 0 && Vector2.DistanceSquared(b.Pos, ship) < hit * hit) {
					bullets.RemoveAt(i);
					shipHp--;
					shipInvuln = 75;
					// a hit clears the notes around you: a breath to recover
					bullets.RemoveAll(o => Vector2.DistanceSquared(o.Pos, ship) < 0.25f * 0.25f);
					shake = 14f;
					SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Clink with { Volume = 0.9f, Pitch = -0.6f });
					if (shipHp <= 0) {
						EndGame(false);
						return;
					}
				}
			}
			for (int i = bursts.Count - 1; i >= 0; i--) {
				var bst = bursts[i];
				bst.life -= 0.08f;
				if (bst.life <= 0f)
					bursts.RemoveAt(i);
				else
					bursts[i] = bst;
			}
			if (playTime >= TimeLimit)
				EndGame(false);
		}

		private static int SegmentAt(Vector2 p)
		{
			float a = MathHelper.WrapAngle(p.ToRotation() - ringRot);
			if (a < 0)
				a += MathHelper.TwoPi;
			return (int)(a / MathHelper.TwoPi * Segments) % Segments;
		}

		private static void EndGame(bool win)
		{
			won = win;
			phase = Phase.Exit;
			timer = 0;
			SoundEngine.PlaySound(win ? ReSounds.Burst with { Volume = 0.8f, Pitch = 0.3f } : ReSounds.EyesClose with { Volume = 1f });
		}

		/// <summary>Back into the world; the music resumes where the song now is.</summary>
		private static void Finish()
		{
			phase = Phase.None;
			RestoreMusic();
			Player p = Main.LocalPlayer;
			if (won) {
				foreach (NPC n in Main.ActiveNPCs) {
					if (n.ModNPC is TwinBase t && t.State != TwinBase.StDying) {
						t.Stunned = 320;
						n.SimpleStrikeNPC((int)(n.lifeMax * 0.04f), 0, noPlayerInteraction: false);
						ParticleSystem.GlyphBurst(n.Center, 40, 10f, 1.1f);
					}
				}
				Subtitles.Push(Subtitles.Refrain, "Mods.Replicant.Twins.Lines.HackWin", 220);
			}
			else {
				int dmg = (int)(p.statLifeMax2 * 0.35f);
				p.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromKey("Mods.Replicant.Twins.HackDeath", p.name)), dmg, 0, dodgeable: false, armorPenetration: 9999f);
				p.AddBuff(BuffID.Confused, 240);
				p.AddBuff(BuffID.Darkness, 240);
				GlitchPlayer.Apply(p, 300);
				Subtitles.Push(Subtitles.Refrain, "Mods.Replicant.Twins.Lines.HackLose", 220);
			}
			ScreenFX.Shake(14f);
			Cinematic.Flash(0.5f);
		}

		// ------------------------------------------------------------------ drawing
		private static float A => MathF.Min(Main.screenHeight * 0.8f, Main.screenWidth * 0.62f);
		private static Vector2 Center => new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f) + Main.rand.NextVector2Circular(shake, shake);
		private static Vector2 ArenaToScreen(Vector2 a) => Center + a * A / 2f;
		private static Vector2 ScreenToArena(Vector2 s) => (s - new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f)) / (A / 2f);

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!Active)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Intrusion", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float t = Main.GlobalTimeWrappedHourly;
			Color gold = ReUtils.Gold;
			// how much of the world has been replaced by the code
			float cover = phase switch {
				Phase.Enter => ReUtils.EaseInCubic(ReUtils.InverseLerp(95, 130, timer)),
				Phase.Exit => 1f - ReUtils.EaseInCubic(ReUtils.InverseLerp(50, 88, timer)),
				_ => 1f,
			};
			// interference: noise blocks and tears, heaviest at the transitions
			float noise = phase switch {
				Phase.Enter => ReUtils.InverseLerp(0, 100, timer) * 0.75f * (1f - ReUtils.InverseLerp(120, 150, timer) * 0.7f),
				Phase.Exit => ReUtils.Bump(ReUtils.InverseLerp(40, 90, timer)),
				_ => 0.06f,
			};
			sb.Draw(px, new Rectangle(0, 0, W, H), new Color(5, 3, 2) * cover);
			if (cover > 0.3f && phase != Phase.Enter || phase == Phase.Enter && timer > 120)
				DrawArena(sb, cover);
			DrawInterference(sb, noise);
			if (phase == Phase.Enter && timer < 125)
				DrawPrelude(sb, timer);

			DynamicSpriteFont big = FontAssets.DeathText.Value;
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			if (phase == Phase.Enter && timer > 100) {
				float ta = ReUtils.InverseLerp(100, 112, timer) * (1f - ReUtils.InverseLerp(138, 150, timer));
				string title = Language.GetTextValue("Mods.Replicant.Twins.Hack.Title");
				DrawGlitchText(sb, big, title, new Vector2(W / 2f, H * 0.42f), 1.2f, ReUtils.PaleGold, ta);
			}
			if (phase == Phase.Play && hintTimer < 300)
				DrawHints(sb, 1f - ReUtils.InverseLerp(240, 300, hintTimer));
			if (phase == Phase.Exit) {
				string res = Language.GetTextValue(won ? "Mods.Replicant.Twins.Hack.Win" : "Mods.Replicant.Twins.Hack.Lose");
				DrawGlitchText(sb, big, res, new Vector2(W / 2f, H * 0.45f), 1.1f, won ? ReUtils.PaleGold : new Color(255, 80, 50), ReUtils.InverseLerp(0, 12, timer) * (1f - ReUtils.InverseLerp(60, 85, timer)));
			}
		}

		private static void DrawArena(SpriteBatch sb, float a)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			float t = Main.GlobalTimeWrappedHourly;
			float size = A;
			Vector2 c = Center;
			Rectangle box = new((int)(c.X - size / 2f), (int)(c.Y - size / 2f), (int)size, (int)size);
			Color gold = ReUtils.Gold;
			// floor: a scrolling grid
			sb.Draw(px, box, new Color(14, 8, 4) * a);
			float step = size / 16f;
			float scroll = (t * 20f) % step;
			for (float x = box.X + scroll; x < box.Right; x += step)
				sb.Draw(px, new Rectangle((int)x, box.Y, 1, box.Height), gold * (0.07f * a));
			for (float y = box.Y + scroll; y < box.Bottom; y += step)
				sb.Draw(px, new Rectangle(box.X, (int)y, box.Width, 1), gold * (0.07f * a));
			// frame of glyphs
			for (int i = 0; i < 4; i++) {
				Rectangle e = i switch {
					0 => new Rectangle(box.X - 3, box.Y - 3, box.Width + 6, 2),
					1 => new Rectangle(box.X - 3, box.Bottom + 1, box.Width + 6, 2),
					2 => new Rectangle(box.X - 3, box.Y - 3, 2, box.Height + 6),
					_ => new Rectangle(box.Right + 1, box.Y - 3, 2, box.Height + 6),
				};
				sb.Draw(px, e, gold.Additive(0.9f * a));
			}
			for (float x = box.X; x < box.Right; x += 18f)
				sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x, box.Y - 14), ReTex.GlyphFrame((int)(x / 18f) + (int)(t * 2f)), gold.Additive(0.4f * a), 0f, new Vector2(7, 9), 0.7f, SpriteEffects.None, 0f);

			float hpK = coreHp / (float)CoreMaxHp;
			// the core
			Vector2 cc = ArenaToScreen(Vector2.Zero);
			float pulse = 0.8f + 0.2f * MathF.Sin(t * 8f);
			Texture2D core = ReTex.SigilCore;
			sb.Draw(core, cc, null, ReUtils.PaleGold.Additive(0.9f * a), t * 1.4f, core.Size() / 2f, size * 0.2f / core.Width * pulse, SpriteEffects.None, 0f);
			FX.Bloom(sb, cc, Color.Lerp(new Color(255, 80, 40), ReUtils.Gold, hpK), size * 0.12f, 0.6f * a * pulse);
			FX.Bloom(sb, cc, Color.White, size * 0.04f, 0.8f * a);
			// the shell
			for (int i = 0; i < Segments; i++) {
				if (segHp[i] == 0)
					continue;
				float ang = ringRot + (i + 0.5f) / Segments * MathHelper.TwoPi;
				Vector2 p = ArenaToScreen(ang.ToRotationVector2() * 0.28f);
				float len = size * 0.28f * MathHelper.TwoPi / Segments * 0.42f;
				bool black = segHp[i] < 0;
				Color fill = black ? new Color(8, 5, 6) : (segHp[i] == 1 ? new Color(160, 90, 20) : gold);
				sb.Draw(px, p, new Rectangle(0, 0, 1, 1), black ? fill : fill.Additive(0.9f * a), ang + MathHelper.PiOver2, new Vector2(0.5f, 0.5f), new Vector2(len * 2f, size * 0.03f), SpriteEffects.None, 0f);
				if (black)
					sb.Draw(px, p, new Rectangle(0, 0, 1, 1), new Color(255, 120, 30).Additive(0.5f * a), ang + MathHelper.PiOver2, new Vector2(0.5f, 0.5f), new Vector2(len * 2f, 2f), SpriteEffects.None, 0f);
			}
			// bullets
			foreach (Bullet b in bullets) {
				Vector2 p = ArenaToScreen(b.Pos);
				float r = size * (b.Dark ? 0.02f : 0.016f);
				if (b.Dark) {
					FX.Smoke(sb, p, Color.Black * a, r * 1.6f);
					FX.Ring(sb, p, new Color(255, 110, 30), r, 0.8f * a);
				}
				else {
					FX.Bloom(sb, p, gold, r * 2.2f, 0.7f * a);
					FX.Bloom(sb, p, Color.White, r * 0.8f, 0.9f * a);
				}
			}
			// shots
			foreach (Bullet s in shots) {
				Vector2 p = ArenaToScreen(s.Pos);
				sb.Draw(px, p, new Rectangle(0, 0, 1, 1), Color.White.Additive(a), s.Vel.ToRotation(), new Vector2(0.5f, 0.5f), new Vector2(12f, 2f), SpriteEffects.None, 0f);
			}
			foreach (var bst in bursts) {
				Vector2 p = ArenaToScreen(bst.pos);
				FX.Flare(sb, p, bst.gold ? gold : new Color(255, 110, 30), 26f * (1.2f - bst.life), bst.life * a, bst.life * 3f, 0.6f);
			}
			// the player: a small pixel ship, a dash leaves after-images
			foreach (var tr in trail)
				DrawShip(sb, ArenaToScreen(tr.pos), 0f, ReUtils.Gold.Additive(0.5f * tr.life * a), 3f);
			Vector2 sp = ArenaToScreen(ship);
			bool blink = shipInvuln > 0 && shipInvuln / 4 % 2 == 0 && dashTime <= 0;
			if (!blink) {
				FX.Bloom(sb, sp, ReUtils.PaleGold, 30f, 0.5f * a);
				DrawShip(sb, sp, 0f, Color.White * a, 3f);
			}
			// dash ready pip
			if (dashCooldown <= 0)
				FX.Ring(sb, sp, ReUtils.Gold, 22f + MathF.Sin(t * 6f) * 2f, 0.35f * a);
			// the core is the target
			if (hintTimer < 300) {
				float ha = (1f - ReUtils.InverseLerp(240, 300, hintTimer)) * a;
				FX.Ring(sb, cc, new Color(255, 120, 60), size * (0.06f + 0.01f * MathF.Sin(t * 8f)), 0.8f * ha);
			}
			// HUD of the hack
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			sb.DrawString(small, Language.GetTextValue("Mods.Replicant.Twins.Hack.Header"), new Vector2(box.X, box.Y - 40), gold * a);
			for (int i = 0; i < ShipMaxHp; i++)
				DrawShip(sb, new Vector2(box.Right - 18 - i * 30, box.Y - 24), 0f, (i < shipHp ? ReUtils.PaleGold : new Color(60, 35, 20)) * a, 2f);
			sb.Draw(px, new Rectangle(box.X, box.Bottom + 12, box.Width, 6), new Color(40, 25, 15) * a);
			sb.Draw(px, new Rectangle(box.X, box.Bottom + 12, (int)(box.Width * hpK), 6), gold * a);
			float timeK = 1f - playTime / (float)TimeLimit;
			sb.Draw(px, new Rectangle(box.X, box.Bottom + 22, (int)(box.Width * timeK), 2), new Color(255, 120, 60) * a);
		}

		// a 9x9 pixel ship: a little arrow of light with a dark cockpit
		private static readonly string[] ShipPix = {
			"....#....",
			"...###...",
			"...#o#...",
			"..##o##..",
			"..#####..",
			".#######.",
			"##.###.##",
			"#..#.#..#",
			"...#.#...",
		};

		private static void DrawShip(SpriteBatch sb, Vector2 center, float rot, Color color, float px)
		{
			Texture2D tex = TextureAssets.MagicPixel.Value;
			for (int y = 0; y < 9; y++)
				for (int x = 0; x < 9; x++) {
					char c = ShipPix[y][x];
					if (c == '.')
						continue;
					Vector2 off = new Vector2(x - 4, y - 4) * px;
					Color col = c == 'o' ? new Color(30, 15, 8) * (color.A / 255f) : color;
					sb.Draw(tex, center + off.RotatedBy(rot), new Rectangle(0, 0, 1, 1), col, rot, Vector2.Zero, px, SpriteEffects.None, 0f);
				}
		}

		/// <summary>
		/// "You are being hacked": a terminal tears open over the world and types the intrusion, a progress bar
		/// eats itself, and the player's own name is rewritten letter by letter.
		/// </summary>
		private static void DrawPrelude(SpriteBatch sb, int t)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			int W = Main.screenWidth, H = Main.screenHeight;
			float a = ReUtils.InverseLerp(0, 10, t) * (1f - ReUtils.InverseLerp(110, 125, t));
			float s = MathHelper.Clamp(H / 1080f, 0.75f, 1.5f);
			float bw = 640 * s, bh = 230 * s;
			float open = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 14, t));
			Rectangle box = new((int)(W / 2f - bw / 2f), (int)(H * 0.3f - bh / 2f * open), (int)bw, (int)(bh * open));
			sb.Draw(px, box, new Color(8, 4, 2) * (0.92f * a));
			Color gold = ReUtils.Gold;
			sb.Draw(px, new Rectangle(box.X, box.Y, box.Width, 2), gold.Additive(a));
			sb.Draw(px, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), gold.Additive(a));
			sb.Draw(px, new Rectangle(box.X, box.Y, box.Width, (int)(22 * s)), new Color(255, 160, 50) * (0.85f * a));
			sb.DrawString(font, "refrain.sys  //  remote session", new Vector2(box.X + 10 * s, box.Y + 2 * s), Color.Black * a, 0f, Vector2.Zero, 0.8f * s, SpriteEffects.None, 0f);
			if (open < 0.9f)
				return;
			string[] lines = {
				"> handshake .......... forced",
				"> locating host: " + Main.LocalPlayer.name,
				"> memory ............ open",
				"> rewriting host",
			};
			float y = box.Y + 32 * s;
			int chars = (t - 14) * 2;
			foreach (string l in lines) {
				int n = Math.Clamp(chars, 0, l.Length);
				if (n <= 0)
					break;
				sb.DrawString(font, l.Substring(0, n), new Vector2(box.X + 16 * s, y), ReUtils.PaleGold * a, 0f, Vector2.Zero, 0.95f * s, SpriteEffects.None, 0f);
				chars -= l.Length + 4;
				y += 30 * s;
			}
			if (t / 8 % 2 == 0)
				sb.Draw(px, new Rectangle(box.X + 16 * (int)s, (int)y + 4, (int)(10 * s), (int)(18 * s)), gold * a);
			// progress bar that eats the box
			float prog = ReUtils.InverseLerp(30, 100, t);
			Rectangle bar = new(box.X + (int)(16 * s), box.Bottom - (int)(30 * s), box.Width - (int)(32 * s), (int)(12 * s));
			sb.Draw(px, bar, new Color(40, 22, 10) * a);
			for (int i = 0; i < (int)(prog * 40); i++) {
				bool glitch = ReUtils.Hash01(i, t / 3) > 0.9f;
				sb.Draw(px, new Rectangle(bar.X + (int)(i * bar.Width / 40f), bar.Y, (int)(bar.Width / 40f) - 2, bar.Height), (glitch ? new Color(255, 70, 40) : gold).Additive(a));
			}
			// the warning slab
			if (t > 60) {
				string warn = Language.GetTextValue("Mods.Replicant.Twins.Hack.Warning");
				bool flash = t / 6 % 2 == 0;
				DrawGlitchText(sb, FontAssets.DeathText.Value, warn, new Vector2(W / 2f, H * 0.62f), 0.9f * s, flash ? new Color(255, 80, 40) : ReUtils.PaleGold, ReUtils.InverseLerp(60, 66, t) * a);
			}
		}

		/// <summary>The rules, spelled out in the first seconds — and pointed at, not just written.</summary>
		private static void DrawHints(SpriteBatch sb, float a)
		{
			if (a <= 0.01f)
				return;
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			Vector2 c = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
			float size = A;
			string[] keys = { "Mods.Replicant.Twins.Hack.HintMove", "Mods.Replicant.Twins.Hack.HintDash", "Mods.Replicant.Twins.Hack.HintFire", "Mods.Replicant.Twins.Hack.HintShell" };
			float y = c.Y + size / 2f + 34f;
			for (int i = 0; i < keys.Length; i++) {
				float k = ReUtils.InverseLerp(i * 20, i * 20 + 12, hintTimer) * a;
				string str = Language.GetTextValue(keys[i]);
				Vector2 m = font.MeasureString(str);
				Vector2 at = new(c.X - m.X / 2f, y + i * 24f);
				sb.DrawString(font, str, at + new Vector2(2), Color.Black * (0.8f * k));
				sb.DrawString(font, str, at, (i == 3 ? new Color(255, 170, 90) : ReUtils.PaleGold) * k);
			}
			// "TARGET" over the core
			string target = Language.GetTextValue("Mods.Replicant.Twins.Hack.Target");
			Vector2 tm = font.MeasureString(target);
			float bob = MathF.Sin(Main.GlobalTimeWrappedHourly * 6f) * 4f;
			sb.DrawString(font, target, c - new Vector2(tm.X / 2f, size * 0.09f + 30f + bob), new Color(255, 130, 70) * a);
		}

		private static void DrawInterference(SpriteBatch sb, float k)
		{
			if (k <= 0.01f)
				return;
			Texture2D px = TextureAssets.MagicPixel.Value;
			int W = Main.screenWidth, H = Main.screenHeight;
			int frame = (int)(Main.GlobalTimeWrappedHourly * 30f);
			int blocks = (int)(140 * k);
			for (int i = 0; i < blocks; i++) {
				float x = ReUtils.Hash01(i, frame) * W, y = ReUtils.Hash01(i, frame, 1) * H;
				float w = 10 + ReUtils.Hash01(i, frame, 2) * 160 * k, h = 2 + ReUtils.Hash01(i, frame, 3) * 18;
				float c = ReUtils.Hash01(i, frame, 4);
				Color col = c < 0.4f ? Color.Black : c < 0.7f ? ReUtils.Gold : c < 0.85f ? new Color(255, 60, 30) : new Color(70, 190, 255);
				sb.Draw(px, new Rectangle((int)x, (int)y, (int)w, (int)h), (c < 0.4f ? col : col.Additive(0.8f)) * (0.85f * k));
			}
			// rolling scanlines
			float roll = (Main.GlobalTimeWrappedHourly * 300f) % H;
			sb.Draw(px, new Rectangle(0, (int)roll, W, (int)(40 * k)), Color.White.Additive(0.08f * k));
			for (int y = 0; y < H; y += 3)
				sb.Draw(px, new Rectangle(0, y, W, 1), Color.Black * (0.25f * k));
		}

		private static void DrawGlitchText(SpriteBatch sb, DynamicSpriteFont font, string text, Vector2 center, float scale, Color color, float a)
		{
			if (a <= 0.01f)
				return;
			Vector2 size = font.MeasureString(text) * scale;
			Vector2 p = center - size / 2f;
			float g = ReUtils.Hash01((int)(Main.GlobalTimeWrappedHourly * 20f), 5) > 0.8f ? 8f : 2f;
			sb.DrawString(font, text, p + new Vector2(-g, 0), new Color(255, 50, 30).Additive(0.5f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			sb.DrawString(font, text, p + new Vector2(g, 0), new Color(60, 190, 255).Additive(0.35f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			sb.DrawString(font, text, p, color * a, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		}
	}

	/// <summary>While the intrusion runs, the world outside holds its breath.</summary>
	public class HackFreezeProjectiles : GlobalProjectile
	{
		public override bool PreAI(Projectile projectile)
		{
			if (!HackSequence.FreezeWorld || projectile.ModProjectile is not Content.Projectiles.IReProjectile)
				return true;
			projectile.timeLeft++;
			projectile.position -= projectile.velocity;
			return false;
		}
	}
}
