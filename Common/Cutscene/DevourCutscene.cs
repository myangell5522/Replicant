using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Recursion;
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
	/// The third verse begins. The last sister finishes her monologue — and doesn't get to die. The eyes open in
	/// the sky: "WE'RE NOT DONE YET." Chains of verse lash out of the edges of the world and bind her in the air.
	/// The colours bleed; something enormous is coming; and Recursion tears in from off-screen and devours her
	/// in a single pass, in a spray of oil-black, ember and gold. Then it turns, roars, and the fight is yours.
	/// </summary>
	public class DevourCutscene : ModSystem
	{
		public const int Length = 560;
		private const int ChainStart = 130, ChainGap = 9, Chains = 6, ChainTravel = 12;
		private const int SpawnAt = 290, BiteLatest = 340;

		private static int timer = -1;
		private static int victim = -1;
		private static int head = -1;
		private static int biteAt = -1;
		private static Vector2 victimPos, biteDir;
		private static readonly Vector2[] chainFrom = new Vector2[Chains];
		private static readonly List<Splat> splats = new();
		private static int impact;

		private struct Splat
		{
			public Vector2 Pos, Vel;
			public float Size, Life;
			public bool Dark;
		}

		public static bool Active => timer >= 0;

		/// <summary>The victim is held in the air (her dying never finishes) until the bite.</summary>
		public static bool Holds(int whoAmI) => Active && whoAmI == victim && biteAt < 0;

		public static bool TryBegin(NPC v)
		{
			if (Active || !DimensionBridge.InDimension || Main.netMode != NetmodeID.SinglePlayer || Main.dedServ)
				return false;
			timer = 0;
			victim = v.whoAmI;
			victimPos = v.Center;
			head = -1;
			biteAt = -1;
			impact = 0;
			splats.Clear();
			for (int i = 0; i < Chains; i++) {
				float a = -MathHelper.PiOver2 + (i - (Chains - 1) / 2f) * 0.62f + (i % 2 == 0 ? 0.12f : -0.12f);
				if (i >= 4)
					a += MathHelper.Pi;   // two from below
				chainFrom[i] = victimPos + a.ToRotationVector2() * 1300f;
			}
			Cinematic.Begin(victimPos, Length, silenceTicks: SpawnAt + 70);
			return true;
		}

		public override void OnWorldUnload()
		{
			timer = -1;
			splats.Clear();
		}

		private static NPC Victim => victim >= 0 && Main.npc[victim].active && Main.npc[victim].ModNPC is TwinBase ? Main.npc[victim] : null;
		private static RecursionHead Head => head >= 0 && Main.npc[head].active && Main.npc[head].ModNPC is RecursionHead h ? h : null;

		public override void PostUpdateEverything()
		{
			if (!Active)
				return;
			timer++;
			int t = timer;
			NPC v = Victim;
			if (v != null) {
				// hung in the chains: a little struggle, then held tight
				float bound = ReUtils.InverseLerp(ChainStart, ChainStart + Chains * ChainGap + ChainTravel, t);
				Vector2 hold = victimPos + new Vector2(0f, MathF.Sin(t * 0.05f) * 6f * (1f - bound));
				if (bound > 0f && t % 40 < 6)
					hold += Main.rand.NextVector2Circular(5f, 3f);   // she pulls against them
				v.Center = Vector2.Lerp(v.Center, hold, 0.2f);
				v.velocity = Vector2.Zero;
			}

			if (t == 1) {
				SoundEngine.PlaySound(ReSounds.Heartbeat with { Volume = 1f, Pitch = -0.4f });
				SoundEngine.PlaySound(RecursionSounds.OminousDrone);
				Subtitles.Clear();
			}
			// the sky darkens, and the eyes open in it, far behind everything
			{
				float open = ReUtils.InverseLerp(20, 60, t) * (1f - ReUtils.InverseLerp(SpawnAt - 10, SpawnAt + 40, t));
				float dark = ReUtils.InverseLerp(0, 70, t) * (biteAt < 0 ? 1f : 1f - ReUtils.InverseLerp(biteAt + 30, biteAt + 160, t));
				int row = t < 34 ? 0 : t < 48 ? 1 : 2;
				int level = t > 70 && t < 230 ? 2 : t > 230 ? 1 : 0;
				Dimension.DimensionSky.HoldEyes(open, dark, row, level);
			}
			if (t % 60 == 0 && t > 0 && t < SpawnAt)
				SoundEngine.PlaySound(ReSounds.Heartbeat with { Volume = 0.55f, Pitch = -0.5f });
			if (t == 30) {
				SoundEngine.PlaySound(ReSounds.EyesOpen with { Pitch = -0.6f });
				ScreenFX.Shake(10f);
			}
			if (t == 70) {
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.5f, Pitch = -0.7f });
				ScreenFX.Shake(16f);
				Cinematic.Flash(0.3f);
			}
			if (t >= 70 && t < 110 && t % 5 == 0)
				SoundEngine.PlaySound(ReSounds.RiddleSelect(5) with { Volume = 0.35f, Pitch = -0.6f + (t - 70) * 0.01f });
			for (int i = 0; i < Chains; i++) {
				if (t == ChainStart + i * ChainGap)
					SoundEngine.PlaySound(TwinSounds.Chain with { Pitch = -0.4f + i * 0.1f }, victimPos);
				if (t == ChainStart + i * ChainGap + ChainTravel) {
					SoundEngine.PlaySound(RecursionSounds.ChainLock with { Pitch = -0.2f + i * 0.08f }, victimPos);
					ScreenFX.Shake(9f);
					Vector2 dir = (victimPos - chainFrom[i]).SafeNormalize(Vector2.UnitY);
					for (int k = 0; k < 12; k++)
						ParticleSystem.Spark(victimPos - dir * 30f, (dir.ToRotation() + MathHelper.Pi + Main.rand.NextFloat(-1f, 1f)).ToRotationVector2() * Main.rand.NextFloat(3f, 9f), ReUtils.PaleGold, 0.9f, 20);
				}
			}
			if (t == 195) {
				SoundEngine.PlaySound(TwinSounds.Bell with { Pitch = -0.5f }, victimPos);
				ParticleSystem.Ring(victimPos, ReUtils.Gold, 0.1f, 1.08f, 30);
			}
			if (t == 210 && v?.ModNPC is TwinBase tb)
				Subtitles.Push(tb.Speaker, "Mods.Replicant.Recursion.Lines.Plea" + (tb.Speaker == Subtitles.Requiem ? "a" : "b"), 150);
			if (t == 245) {
				SoundEngine.PlaySound(RecursionSounds.Roar with { Volume = 0.55f, Pitch = -0.5f });
				SoundEngine.PlaySound(RecursionSounds.Chitter with { Volume = 0.8f, Pitch = -0.6f });
			}
			if (t > 240 && t < SpawnAt + 30)
				ScreenFX.Shake(2f + (t - 240) * 0.12f);

			// it comes
			if (t == SpawnAt) {
				biteDir = new Vector2(-1f, -0.32f);
				biteDir.Normalize();
				Vector2 from = victimPos - biteDir * 2100f;
				int n = NPC.NewNPC(new Terraria.DataStructures.EntitySource_Misc("Replicant: Devour"), (int)from.X, (int)from.Y, ModContent.NPCType<RecursionHead>(), 0, RecursionHead.StScripted);
				if (n >= 0 && n < Main.maxNPCs) {
					head = n;
					NPC hn = Main.npc[n];
					hn.Center = from;
					hn.velocity = biteDir * 70f;
					hn.target = Main.myPlayer;
				}
				SoundEngine.PlaySound(RecursionSounds.Rush with { Volume = 1f, Pitch = -0.2f }, victimPos);
			}
			RecursionHead h = Head;
			if (h != null) {
				if (biteAt < 0) {
					h.ScriptTarget = victimPos;
					h.ScriptSpeed = 72f;
					h.ScriptTurn = 0.3f;
					Cinematic.SetFocus(Vector2.Lerp(victimPos, h.NPC.Center, 0.25f));
					if (h.NPC.Distance(victimPos) < 90f || t >= BiteLatest)
						Bite(h);
				}
				else {
					int k = t - biteAt;
					// through, then up and around: it circles the sky above you
					Player p = Main.LocalPlayer;
					if (k < 40)
						h.ScriptTarget = victimPos + biteDir * 1400f;
					else {
						float ang = (k - 40) * 0.035f + biteDir.ToRotation() - 1.2f;
						h.ScriptTarget = p.Center + new Vector2(0f, -420f) + ang.ToRotationVector2() * new Vector2(760f, 360f);
					}
					h.ScriptSpeed = k < 40 ? 60f : 28f;
					h.ScriptTurn = k < 40 ? 0.05f : 0.12f;
					if (k > 30)
						Cinematic.SetFocus(Vector2.Lerp(p.Center, h.NPC.Center, 0.5f));
					if (k == 60) {
						SoundEngine.PlaySound(RecursionSounds.Roar, h.NPC.Center);
						ScreenFX.Shake(26f);
					}
					if (k == 70)
						RecursionTitle.Show();
				}
			}

			// blood on the lens
			for (int i = splats.Count - 1; i >= 0; i--) {
				Splat s = splats[i];
				s.Pos += s.Vel;
				s.Vel *= 0.86f;
				s.Vel.Y += 0.15f;
				s.Life -= 0.008f;
				if (s.Life <= 0f)
					splats.RemoveAt(i);
				else
					splats[i] = s;
			}
			if (impact > 0)
				impact--;

			if (t >= Length) {
				h?.Release();
				timer = -1;
			}
		}

		private static void Bite(RecursionHead h)
		{
			biteAt = timer;
			impact = 6;
			biteDir = h.NPC.velocity.SafeNormalize(biteDir);
			SoundEngine.PlaySound(RecursionSounds.Bite with { Volume = 1f, Pitch = -0.2f }, victimPos);
			SoundEngine.PlaySound(RecursionSounds.Screech, victimPos);
			SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.8f, Pitch = -0.3f }, victimPos);
			ScreenFX.Shake(45f);
			ScreenFX.ZoomPunch(0.22f);
			Cinematic.Flash(0.9f);
			NPC v = Victim;
			if (v != null) {
				RecursionHead.Bleed(v.Center, 180, 2f);
				ParticleSystem.GlyphBurst(v.Center, 90, 16f, 1.2f);
				for (int i = 0; i < 3; i++)
					ParticleSystem.Ring(v.Center, i == 1 ? new Color(200, 30, 10) : ReUtils.PaleGold, 0.1f + i * 0.08f, 1.09f + i * 0.02f, 34 + i * 8);
				// devoured: gone without a death of her own (the fight isn't over)
				v.life = 0;
				v.active = false;
			}
			// the chains snap and fall apart
			for (int i = 0; i < Chains; i++) {
				Vector2 d = (victimPos - chainFrom[i]).SafeNormalize(Vector2.UnitY);
				for (int k = 0; k < 10; k++)
					ParticleSystem.Spark(victimPos - d * Main.rand.NextFloat(40f, 400f), Main.rand.NextVector2Circular(6f, 6f), ReUtils.Gold, 1f, 40).Gravity = 0.2f;
			}
			// blood across the screen
			Vector2 sc = Vector2.Transform(victimPos - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);
			for (int i = 0; i < 46; i++) {
				Vector2 vel = (biteDir.ToRotation() + Main.rand.NextFloat(-1.2f, 1.2f)).ToRotationVector2() * Main.rand.NextFloat(10f, 60f);
				splats.Add(new Splat { Pos = sc, Vel = vel, Size = Main.rand.NextFloat(16f, 70f), Life = Main.rand.NextFloat(0.6f, 1f), Dark = i % 3 != 0 });
			}
		}

		// ------------------------------------------------------------------ drawing
		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!Active && splats.Count == 0)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			// world-space chains under the UI scale of the game
			layers.Insert(Math.Max(0, index - 1), new LegacyGameInterfaceLayer("Replicant: Devour Chains", () => {
				if (Active)
					DrawChains(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.Game));
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Devour", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void DrawChains(SpriteBatch sb)
		{
			int t = timer;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float time = Main.GlobalTimeWrappedHourly;
			NPC v = Victim;
			Vector2 target = v?.Center ?? victimPos;
			float snap = biteAt >= 0 ? ReUtils.InverseLerp(biteAt, biteAt + 10, t) : 0f;
			if (snap >= 1f)
				return;
			for (int i = 0; i < Chains; i++) {
				float k = ReUtils.EaseOutCubic(ReUtils.InverseLerp(ChainStart + i * ChainGap, ChainStart + i * ChainGap + ChainTravel, t));
				if (k <= 0f)
					continue;
				Vector2 a = chainFrom[i];
				Vector2 b = Vector2.Lerp(a, target, k);
				Vector2 d = b - a;
				float len = d.Length();
				if (len < 1f)
					continue;
				Vector2 dir = d / len;
				Vector2 n = new(-dir.Y, dir.X);
				float rot = dir.ToRotation();
				float tension = MathF.Sin(time * 40f + i) * (t % 40 < 6 ? 2.5f : 0.6f);
				int link = 0;
				for (float s = 0f; s < len; s += 13f, link++) {
					float sag = MathF.Sin(s / len * MathHelper.Pi) * 18f * (1f - k * 0.8f);
					Vector2 q = a + dir * s + n * (sag + tension) - Main.screenPosition;
					float fade = 1f - snap;
					if (link % 2 == 0) {
						// a link seen face-on: a gold ring
						sb.Draw(px, q, new Rectangle(0, 0, 1, 1), new Color(20, 12, 6) * fade, rot, new Vector2(0.5f), new Vector2(17f, 10f), SpriteEffects.None, 0f);
						sb.Draw(px, q, new Rectangle(0, 0, 1, 1), ReUtils.Gold * fade, rot, new Vector2(0.5f), new Vector2(15f, 8f), SpriteEffects.None, 0f);
						sb.Draw(px, q, new Rectangle(0, 0, 1, 1), new Color(20, 12, 6) * fade, rot, new Vector2(0.5f), new Vector2(9f, 3f), SpriteEffects.None, 0f);
					}
					else {
						// edge-on
						sb.Draw(px, q, new Rectangle(0, 0, 1, 1), new Color(150, 90, 20) * fade, rot, new Vector2(0.5f), new Vector2(16f, 3f), SpriteEffects.None, 0f);
					}
					if (link % 6 == 0)
						sb.Draw(ReTex.GlyphAtlasGlow, q + n * 12f, ReTex.GlyphFrame(link + i * 7), ReUtils.PaleGold.Additive(0.6f * fade), 0f, new Vector2(7, 9), 0.8f, SpriteEffects.None, 0f);
				}
				FX.Bloom(sb, b - Main.screenPosition, ReUtils.Gold, 40f, 0.5f * (1f - snap));
			}
			// the seal that locks her
			float seal = ReUtils.InverseLerp(190, 205, t) * (1f - snap);
			if (seal > 0f) {
				Vector2 c = target - Main.screenPosition;
				FX.Ring(sb, c, ReUtils.Gold, 90f * ReUtils.EaseOutBack(seal), 0.6f * seal);
				for (int g = 0; g < 12; g++) {
					float ang = g / 12f * MathHelper.TwoPi + time * 0.8f;
					sb.Draw(ReTex.GlyphAtlasGlow, c + ang.ToRotationVector2() * 96f, ReTex.GlyphFrame(g * 3 + 1), ReUtils.PaleGold.Additive(0.8f * seal), ang + MathHelper.PiOver2, new Vector2(7, 9), 1.1f, SpriteEffects.None, 0f);
				}
			}
		}

		private static void Draw(SpriteBatch sb)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float time = Main.GlobalTimeWrappedHourly;
			float s = MathHelper.Clamp(H / 1080f, 0.7f, 1.8f);
			int t = timer;

			if (Active) {
				float dark = ReUtils.InverseLerp(0, 40, t) * (1f - ReUtils.InverseLerp(biteAt < 0 ? Length : biteAt + 20, biteAt < 0 ? Length : biteAt + 120, t));
				sb.Draw(px, new Rectangle(0, 0, W, H), new Color(4, 2, 2) * (0.22f * dark));

				// WE'RE NOT DONE YET.
				float say = ReUtils.InverseLerp(70, 74, t) * (1f - ReUtils.InverseLerp(220, 240, t));
				if (say > 0f)
					DrawNotDone(sb, W, H, s, say, t, time);

				// colour bleed: the world can't hold its palette as it approaches
				float bleed = ReUtils.InverseLerp(235, SpawnAt + 20, t) * (biteAt < 0 ? 1f : 1f - ReUtils.InverseLerp(biteAt, biteAt + 30, t));
				if (bleed > 0f) {
					int phase = (int)(time * 14f) % 3;
					Color c = phase == 0 ? new Color(140, 16, 6) : phase == 1 ? new Color(255, 170, 40) : Color.Black;
					sb.Draw(px, new Rectangle(0, 0, W, H), (phase == 1 ? c.Additive(0.12f * bleed) : c * (0.25f * bleed)));
					for (int i = 0; i < 9; i++) {
						float y = ReUtils.Hash01(i, (int)(time * 20f)) * H;
						float hh = 4f + ReUtils.Hash01(i, (int)(time * 20f), 1) * 30f;
						float off = (ReUtils.Hash01(i, (int)(time * 20f), 2) - 0.5f) * 40f * bleed;
						sb.Draw(px, new Rectangle((int)off, (int)y, W, (int)hh), (i % 2 == 0 ? new Color(255, 40, 20) : new Color(255, 200, 90)).Additive(0.2f * bleed));
					}
					FX.Smoke(sb, new Vector2(W / 2f, H / 2f), new Color(80, 6, 4) * (0.35f * bleed), W * 0.9f, 0f, 1.2f);
				}

				// impact frames: white, then black with ember lines converging on her
				if (impact > 0) {
					Vector2 c = Vector2.Transform(victimPos - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);
					// softened: a warm flash that fades, then a dimmed frame with lines converging on her
					if (impact > 3)
						sb.Draw(px, new Rectangle(0, 0, W, H), new Color(255, 236, 190) * (impact == 6 ? 0.8f : impact == 5 ? 0.55f : 0.35f));
					else {
						sb.Draw(px, new Rectangle(0, 0, W, H), Color.Black * (0.25f + impact * 0.1f));
						for (int i = 0; i < 40; i++) {
							float a = i / 40f * MathHelper.TwoPi + ReUtils.Hash01(i, 3) * 0.2f;
							Vector2 far = c + a.ToRotationVector2() * (W + H);
							float len = (W + H) * (0.6f + 0.4f * ReUtils.Hash01(i, 4));
							sb.Draw(px, far, new Rectangle(0, 0, 1, 1), (i % 3 == 0 ? ReUtils.Ember : ReUtils.PaleGold) * 0.9f, a + MathHelper.Pi, new Vector2(0f, 0.5f), new Vector2(len, 2f + ReUtils.Hash01(i, 5) * 5f), SpriteEffects.None, 0f);
						}
						FX.Bloom(sb, c, Color.White, 140f, 1f);
					}
				}
			}

			// blood on the lens
			foreach (Splat sp in splats) {
				float a = MathF.Min(1f, sp.Life * 1.5f);
				if (sp.Dark) {
					FX.Smoke(sb, sp.Pos, new Color(12, 3, 2) * (0.85f * a), sp.Size);
					FX.Bloom(sb, sp.Pos, new Color(180, 30, 8), sp.Size * 0.6f, 0.25f * a);
				}
				else {
					FX.Bloom(sb, sp.Pos, ReUtils.Ember, sp.Size * 0.7f, 0.5f * a);
					FX.Bloom(sb, sp.Pos, ReUtils.PaleGold, sp.Size * 0.25f, 0.6f * a);
				}
			}
		}

		private static void DrawEyes(SpriteBatch sb, int W, int H, float s, float open, int t, float time)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			float y = H * 0.22f;
			const int col = 6;
			for (int x = 0; x < W; x += col) {
				float u = x / (float)W;
				float env = MathF.Pow(MathF.Sin(u * MathHelper.Pi), 0.6f);
				float half = (6f + 110f * env * (0.7f + 0.3f * ReUtils.Hash01(x / col, 9))) * open * s;
				float wob = MathF.Sin(u * 19f + time) * 12f * s;
				sb.Draw(px, new Rectangle(x, (int)(y + wob - half), col, (int)(half * 2f)), new Color(4, 1, 2) * MathF.Min(1f, open * 2f));
				Color lip = Color.Lerp(ReUtils.Ember, ReUtils.EyeBlue, 0.5f + 0.5f * MathF.Sin(time * 5f + x * 0.03f));
				sb.Draw(px, new Rectangle(x, (int)(y + wob - half - 2), col, 3), lip.Additive(0.9f * open));
				sb.Draw(px, new Rectangle(x, (int)(y + wob + half - 1), col, 3), lip.Additive(0.9f * open));
			}
			Texture2D eyes = ReTex.Eyes, glow = ReTex.EyesGlow;
			const int cols = 6, rows = 10;
			int fw = eyes.Width / cols, fh = eyes.Height / rows;
			int row = t < 40 ? 7 : t < 52 ? 4 : 0;
			int level = t > 70 && t < 220 ? (t % 8 < 4 ? 4 : 3) : 1;
			Rectangle src = new(level * fw, row * fh, fw, fh);
			float es = 7f * s;
			Vector2 c = new(W / 2f + MathF.Sin(time * 0.7f) * 6f + (t > 70 && t < 110 ? Main.rand.NextFloat(-6f, 6f) : 0f), y);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			sb.Draw(eyes, c, src, new Color(210, 205, 210) * open, 0f, new Vector2(fw / 2f, fh / 2f), es, SpriteEffects.None, 0f);
			sb.Draw(glow, c, src, Color.White.Additive(0.75f * open), 0f, new Vector2(fw / 2f, fh / 2f), es, SpriteEffects.None, 0f);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			FX.Bloom(sb, c, ReUtils.EyeBlue, 280f * s, 0.2f * open, 2f, 0.6f);
		}

		private static void DrawNotDone(SpriteBatch sb, int W, int H, float s, float a, int t, float time)
		{
			DynamicSpriteFont big = FontAssets.DeathText.Value;
			string text = Language.GetTextValue("Mods.Replicant.Recursion.NotDone");
			int shown = Math.Clamp((t - 70) / 3 + 1, 0, text.Length);
			float scale = 1.5f * s;
			Vector2 full = big.MeasureString(text) * scale;
			Vector2 p = new(W / 2f - full.X / 2f, H * 0.42f - full.Y / 2f);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, (int)(p.Y - 10), W, (int)(full.Y + 20)), Color.Black * (0.6f * a));
			string part = text.Substring(0, shown);
			Vector2 jitter = t < 110 ? Main.rand.NextVector2Circular(4f, 4f) : Main.rand.NextVector2Circular(1f, 1f);
			float g = ReUtils.Hash01((int)(time * 24f), 13) > 0.75f ? 12f : 3f;
			sb.DrawString(big, part, p + jitter + new Vector2(-g, 0), new Color(60, 140, 255).Additive(0.55f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			sb.DrawString(big, part, p + jitter + new Vector2(g, 0), new Color(255, 60, 20).Additive(0.55f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			sb.DrawString(big, part, p + jitter, new Color(235, 240, 255) * a, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			// its own script beneath, like every voice in this song
			float gx = W / 2f - shown * 9f * s;
			for (int i = 0; i < shown; i++) {
				if (text[i] == ' ')
					continue;
				int gi = (int)(ReUtils.Hash01(char.ToLowerInvariant(text[i]), 17) * ReTex.GlyphCount);
				sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(gx + i * 18f * s, p.Y + full.Y + 14f * s), ReTex.GlyphFrame(gi), ReUtils.EyeBlue.Additive(0.7f * a), 0f, new Vector2(7, 9), 1.1f * s, SpriteEffects.None, 0f);
			}
		}
	}

	/// <summary>The title card for the third verse: RECURSION, written in and then torn.</summary>
	public class RecursionTitle : ModSystem
	{
		private static int timer = -1;
		private const int Length = 240;

		public static void Show() => timer = 0;

		public override void OnWorldUnload() => timer = -1;

		public override void PostUpdateEverything()
		{
			if (timer >= 0 && ++timer > Length)
				timer = -1;
			if (timer == 1)
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.7f, Pitch = 0.2f });
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (timer < 0)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Recursion Title", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			float s = MathHelper.Clamp(H / 1080f, 0.7f, 1.8f);
			float time = Main.GlobalTimeWrappedHourly;
			float a = ReUtils.InverseLerp(0, 14, timer) * (1f - ReUtils.InverseLerp(Length - 40, Length, timer));
			DynamicSpriteFont big = FontAssets.DeathText.Value, small = FontAssets.MouseText.Value;
			string title = Language.GetTextValue("Mods.Replicant.Recursion.Title");
			string sub = Language.GetTextValue("Mods.Replicant.Recursion.Sub");
			string verse = Language.GetTextValue("Mods.Replicant.Recursion.Verse");
			float tracking = MathHelper.Lerp(26f, 8f, ReUtils.EaseOutCubic(ReUtils.InverseLerp(0, 60, timer))) * s;
			float scale = 1.9f * s;
			float total = 0f;
			foreach (char c in title)
				total += big.MeasureString(c.ToString()).X * scale + tracking;
			float x = W / 2f - total / 2f;
			float y = H * 0.36f;
			Texture2D px = TextureAssets.MagicPixel.Value;
			sb.Draw(px, new Rectangle(0, (int)(y - 24 * s), W, (int)(150 * s)), Color.Black * (0.5f * a));
			for (int i = 0; i < title.Length; i++) {
				string c = title[i].ToString();
				float k = ReUtils.InverseLerp(i * 3, i * 3 + 10, timer);
				Vector2 p = new(x, y + (1f - k) * -30f * s);
				bool tear = ReUtils.Hash01(i, (int)(time * 20f)) > 0.9f;
				sb.DrawString(big, c, p + new Vector2(tear ? 8f : 2f, 0), new Color(255, 40, 20).Additive(0.5f * a * k), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				sb.DrawString(big, c, p, ReUtils.PaleGold * (a * k), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				x += big.MeasureString(c).X * scale + tracking;
			}
			Vector2 ss = small.MeasureString(sub) * 1.2f * s;
			sb.DrawString(small, sub, new Vector2(W / 2f - ss.X / 2f, y + 96f * s), new Color(255, 200, 140) * (a * ReUtils.InverseLerp(40, 60, timer)), 0f, Vector2.Zero, 1.2f * s, SpriteEffects.None, 0f);
			Vector2 vs = small.MeasureString(verse) * 0.85f * s;
			sb.DrawString(small, verse, new Vector2(W / 2f - vs.X / 2f, y - 18f * s), ReUtils.Ember * (a * ReUtils.InverseLerp(20, 40, timer)), 0f, Vector2.Zero, 0.85f * s, SpriteEffects.None, 0f);
			float line = ReUtils.EaseOutCubic(ReUtils.InverseLerp(30, 70, timer)) * W * 0.32f;
			sb.Draw(px, new Rectangle((int)(W / 2f - line), (int)(y + 86f * s), (int)(line * 2f), 2), ReUtils.Gold.Additive(0.8f * a));
		}
	}
}
