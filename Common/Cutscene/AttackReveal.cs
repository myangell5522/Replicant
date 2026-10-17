using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// The first time a sister sings a new movement, its name tears through the screen: the HUD cracks like
	/// glass from the point where she stands, a black wound opens across the screen with burning glyph edges,
	/// the title writes itself in glyphs and resolves, then shatters into falling runes.
	/// Drawn as the very last interface layer so it breaks over the hotbar, hearts and boss bar.
	/// </summary>
	public class AttackReveal : ModSystem
	{
		private struct Crack
		{
			public Vector2[] Points;
			public float Delay;
		}

		private struct Shard
		{
			public Vector2 Pos, Vel;
			public float Rot, Spin, Life, MaxLife, Scale;
			public int Glyph;
			public Color Color;
		}

		private static bool active;
		private static int age, duration;
		private static string title, sub, header;
		private static Color accent;
		private static Vector2 worldOrigin;
		private static bool big;
		private static int seed;
		private static readonly List<Crack> cracks = new();
		private static readonly List<Shard> shards = new();
		private static int movementCount;

		public static bool Active => active;

		/// <summary>Reset the movement numbering (new fight).</summary>
		public static void ResetCount() => movementCount = 0;

		/// <param name="speaker">Subtitles.Requiem / Refrain / Both</param>
		/// <param name="key">entry under Mods.Replicant.Twins.Attacks</param>
		public static void Show(int speaker, string key, Vector2 origin, bool reprise = false, bool bigCard = false)
		{
			if (Main.dedServ)
				return;
			active = true;
			age = 0;
			big = bigCard;
			duration = bigCard ? 200 : 135;
			worldOrigin = origin;
			seed = Main.rand.Next(100000);
			title = Language.GetTextValue("Mods.Replicant.Twins.Attacks." + key + ".Title").ToUpperInvariant();
			sub = reprise ? Language.GetTextValue("Mods.Replicant.Twins.Attacks.Reprise") : Language.GetTextValue("Mods.Replicant.Twins.Attacks." + key + ".Sub");
			string who = Language.GetTextValue("Mods.Replicant.Twins.Speaker" + speaker).ToUpperInvariant();
			if (bigCard) {
				header = who;
			}
			else {
				movementCount++;
				header = who + "  ·  " + Language.GetTextValue("Mods.Replicant.Twins.Attacks.Movement") + " " + Roman(movementCount);
			}
			accent = speaker switch {
				Subtitles.Requiem => new Color(255, 150, 60),
				Subtitles.Refrain => new Color(255, 214, 120),
				_ => ReUtils.PaleGold,
			};
			BuildCracks();
			SoundEngine.PlaySound(ReSounds.RiddleSelect(bigCard ? 5 : 4) with { Volume = 0.75f });
			SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Bell with { Volume = bigCard ? 0.9f : 0.5f, Pitch = bigCard ? -0.3f : 0.1f });
			ScreenFX.Shake(bigCard ? 18f : 9f);
			ScreenFX.ZoomPunch(bigCard ? 0.14f : 0.07f);
		}

		private static string Roman(int n)
		{
			string[] r = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX", "XXI", "XXII" };
			return n >= 1 && n <= r.Length ? r[n - 1] : n.ToString();
		}

		private static Vector2 ImpactPoint {
			get {
				Vector2 p = Vector2.Transform(worldOrigin - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);
				return Vector2.Clamp(p, new Vector2(60, 60), new Vector2(Main.screenWidth - 60, Main.screenHeight - 60));
			}
		}

		private static void BuildCracks()
		{
			cracks.Clear();
			var r = new Random(seed);
			int n = big ? 18 : 12;
			for (int i = 0; i < n; i++) {
				float a = MathHelper.TwoPi * i / n + (float)(r.NextDouble() - 0.5) * 0.4f;
				float len = Main.screenWidth * (0.25f + 0.55f * (float)r.NextDouble());
				int segs = 8 + r.Next(6);
				var pts = new Vector2[segs + 1];
				Vector2 p = Vector2.Zero;
				pts[0] = p;
				for (int s = 1; s <= segs; s++) {
					a += (float)(r.NextDouble() - 0.5) * 0.7f;
					p += a.ToRotationVector2() * len / segs;
					pts[s] = p;
				}
				cracks.Add(new Crack { Points = pts, Delay = (float)r.NextDouble() * 6f });
				// a branch
				if (r.NextDouble() < 0.6) {
					int from = 2 + r.Next(segs - 3);
					float b = a + (r.NextDouble() < 0.5 ? 0.8f : -0.8f);
					var bp = new Vector2[5];
					bp[0] = pts[from];
					for (int s = 1; s < 5; s++) {
						b += (float)(r.NextDouble() - 0.5) * 0.6f;
						bp[s] = bp[s - 1] + b.ToRotationVector2() * len / segs * 0.8f;
					}
					cracks.Add(new Crack { Points = bp, Delay = 4f + from });
				}
			}
		}

		public override void OnWorldUnload()
		{
			active = false;
			shards.Clear();
		}

		public override void PostUpdateEverything()
		{
			for (int i = shards.Count - 1; i >= 0; i--) {
				Shard s = shards[i];
				s.Life++;
				s.Vel.Y += 0.18f;
				s.Vel *= 0.985f;
				s.Pos += s.Vel;
				s.Rot += s.Spin;
				shards[i] = s;
				if (s.Life >= s.MaxLife)
					shards.RemoveAt(i);
			}
			if (!active)
				return;
			age++;
			if (age == duration - 24)
				ShatterTitle();
			if (age >= duration)
				active = false;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!active && shards.Count == 0)
				return;
			// the very top: it breaks over everything
			layers.Add(new LegacyGameInterfaceLayer("Replicant: Attack Reveal", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static float S => MathHelper.Clamp(Main.screenHeight / 1080f, 0.7f, 1.8f);
		private static float BandY => Main.screenHeight * 0.34f;

		private static void Draw(SpriteBatch sb)
		{
			if (active) {
				DrawFlash(sb);
				DrawCracks(sb);
				DrawHudGlitch(sb);
				DrawBand(sb);
				DrawTitle(sb);
			}
			DrawShards(sb);
		}

		private static void DrawFlash(SpriteBatch sb)
		{
			float f = MathF.Max(0f, 1f - age / 10f) * (big ? 0.45f : 0.25f) * ModContent.GetInstance<ReClientConfig>().FlashIntensity;
			if (f > 0.01f)
				sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), accent.Additive(f));
		}

		private static void DrawCracks(SpriteBatch sb)
		{
			float fade = ReUtils.InverseLerp(duration, duration - 40, age) * (big ? 1f : 0.85f);
			Vector2 o = ImpactPoint;
			Texture2D th = ReTex.Thread;
			foreach (Crack c in cracks) {
				float grow = ReUtils.EaseOutCubic((age - c.Delay) / 12f);
				if (grow <= 0f)
					continue;
				int visible = (int)(grow * (c.Points.Length - 1));
				for (int s = 0; s < visible; s++) {
					Vector2 a = o + c.Points[s], b = o + c.Points[s + 1];
					Vector2 d = b - a;
					float rot = d.ToRotation();
					float sc = d.Length() / th.Width;
					float w = (1f - s / (float)c.Points.Length) * 3f * S;
					// chromatic split of the crack: the glass refracts our palette
					sb.Draw(th, a + new Vector2(-1.5f, 0), null, new Color(255, 70, 40).Additive(0.35f * fade), rot, new Vector2(0, th.Height / 2f), new Vector2(sc, w / th.Height), SpriteEffects.None, 0f);
					sb.Draw(th, a + new Vector2(1.5f, 0), null, new Color(70, 190, 255).Additive(0.2f * fade), rot, new Vector2(0, th.Height / 2f), new Vector2(sc, w / th.Height), SpriteEffects.None, 0f);
					sb.Draw(th, a, null, accent.Additive(0.9f * fade), rot, new Vector2(0, th.Height / 2f), new Vector2(sc, w / th.Height), SpriteEffects.None, 0f);
					sb.Draw(th, a, null, Color.White.Additive(0.6f * fade), rot, new Vector2(0, th.Height / 2f), new Vector2(sc, w * 0.4f / th.Height), SpriteEffects.None, 0f);
					// glyphs caught in the fracture
					if (s % 2 == 1)
						sb.Draw(ReTex.GlyphAtlasGlow, a, ReTex.GlyphFrame(s * 13 + (int)c.Delay * 7 + age / 6), accent.Additive(0.8f * fade), 0f, new Vector2(7, 9), 0.9f * S, SpriteEffects.None, 0f);
				}
			}
			FX.Bloom(sb, o, accent, 120f * S, 0.5f * fade * MathF.Max(0.3f, 1f - age / 30f));
			FX.Flare(sb, o, accent, 220f * S * MathF.Max(0f, 1f - age / 25f), 0.8f * fade, 0f, 0.3f);
		}

		/// <summary>The HUD itself glitches: torn blocks and glyph noise across the hotbar, hearts and boss bar.</summary>
		private static void DrawHudGlitch(SpriteBatch sb)
		{
			float k = MathF.Max(0f, 1f - age / (big ? 60f : 38f));
			if (k <= 0f)
				return;
			Texture2D px = TextureAssets.MagicPixel.Value;
			int frame = age / 3;
			Rectangle[] zones = {
				new(10, 10, 640, 90),                                                      // hotbar
				new(Main.screenWidth - 460, 6, 450, 90),                                   // life & mana
				new(Main.screenWidth / 2 - 360, Main.screenHeight - 110, 720, 80),         // boss bar
			};
			for (int z = 0; z < zones.Length; z++) {
				Rectangle zr = zones[z];
				int blocks = (int)(10 * k) + 2;
				for (int b = 0; b < blocks; b++) {
					float hx = ReUtils.Hash01(seed + b, frame, z);
					float hy = ReUtils.Hash01(seed + b, frame, z + 10);
					float hw = ReUtils.Hash01(seed + b, frame, z + 20);
					int w = (int)(30 + hw * 160);
					int h = (int)(4 + hy * 14);
					int x = zr.X + (int)(hx * (zr.Width - w));
					int y = zr.Y + (int)(hy * (zr.Height - h));
					sb.Draw(px, new Rectangle(x, y, w, h), Color.Black * (0.85f * k));
					// a displaced copy of the strip's edge in our gold, as if the pixels slid
					sb.Draw(px, new Rectangle(x + (int)((hw - 0.5f) * 24), y + h / 2, w, 1), accent.Additive(0.7f * k));
					int glyphs = w / 16;
					for (int g = 0; g < glyphs; g++)
						sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x + 8 + g * 16, y + h / 2f), ReTex.GlyphFrame(b * 5 + g + frame), accent.Additive(0.75f * k), 0f, new Vector2(7, 9), 0.6f, SpriteEffects.None, 0f);
				}
				// a full-width tear line through each zone
				int ty = zr.Y + (int)(ReUtils.Hash01(seed, frame / 2, z + 40) * zr.Height);
				sb.Draw(px, new Rectangle(zr.X - 20, ty, zr.Width + 40, 2), accent.Additive(0.5f * k));
			}
		}

		private static float BandOpen => ReUtils.EaseOutCubic(ReUtils.InverseLerp(4, 20, age)) * (1f - ReUtils.EaseInCubic(ReUtils.InverseLerp(duration - 26, duration - 4, age)));

		private static void DrawBand(SpriteBatch sb)
		{
			float open = BandOpen;
			if (open <= 0.001f)
				return;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float half = (big ? 110f : 78f) * S * open;
			float y = BandY;
			int W = Main.screenWidth;
			// the wound: torn columns of black with jagged edges
			const int col = 6;
			for (int x = 0; x < W; x += col) {
				float j1 = (ReUtils.Hash01(x / col, seed) - 0.5f) * 14f * S;
				float j2 = (ReUtils.Hash01(x / col, seed + 1) - 0.5f) * 14f * S;
				float wob = MathF.Sin(x * 0.01f + age * 0.08f) * 4f * S;
				int top = (int)(y - half + j1 + wob);
				int bot = (int)(y + half + j2 - wob);
				sb.Draw(px, new Rectangle(x, top, col, Math.Max(0, bot - top)), new Color(6, 3, 4) * (0.9f * open));
				// burning rims
				sb.Draw(px, new Rectangle(x, top - 1, col, 2), accent.Additive(0.9f * open));
				sb.Draw(px, new Rectangle(x, bot - 1, col, 2), accent.Additive(0.9f * open));
			}
			// glyph streams along both edges, flowing in opposite directions
			float scroll = age * 2.4f;
			for (float x = -(scroll % 18f); x < W; x += 18f) {
				int idx = (int)((x + scroll) / 18f);
				sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x, y - half - 10f * S), ReTex.GlyphFrame(idx * 3), accent.Additive(0.55f * open), 0f, new Vector2(7, 9), 0.8f * S, SpriteEffects.None, 0f);
				sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(W - x, y + half + 10f * S), ReTex.GlyphFrame(idx * 5 + 2), accent.Additive(0.55f * open), 0f, new Vector2(7, 9), 0.8f * S, SpriteEffects.None, 0f);
			}
			// light leaking out of the tear
			FX.Bloom(sb, new Vector2(W / 2f, y), accent, W * 0.45f, 0.18f * open, 1f, 0.12f);
			// scanlines inside
			for (float sy = y - half; sy < y + half; sy += 4f)
				sb.Draw(px, new Rectangle(0, (int)sy, W, 1), Color.Black * (0.25f * open));
		}

		private static void DrawTitle(SpriteBatch sb)
		{
			float open = BandOpen;
			if (open <= 0.05f || age >= duration - 24)
				return;
			float s = S;
			DynamicSpriteFont big_ = FontAssets.DeathText.Value;
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			float tScale = (big ? 1.25f : 0.95f) * s;
			float spacing = (big ? 10f : 7f) * s;
			float y = BandY;
			float total = 0f;
			foreach (char ch in title)
				total += big_.MeasureString(ch.ToString()).X * tScale + spacing;
			total -= spacing;
			float x = Main.screenWidth / 2f - total / 2f;
			int reveal = (int)((age - 10) * 0.7f);
			float jitterAmt = MathF.Max(0f, 1f - (age - 10) / 40f);
			Vector2 glyphOrigin = new(7, 9);
			for (int i = 0; i < title.Length; i++) {
				string c = title[i].ToString();
				Vector2 size = big_.MeasureString(c) * tScale;
				if (title[i] != ' ' && i < reveal) {
					int since = reveal - i;
					Vector2 p = new(x, y - size.Y * 0.42f);
					p += new Vector2(ReUtils.Hash01(i, age / 2, seed) - 0.5f, ReUtils.Hash01(i, age / 2, seed + 3) - 0.5f) * 6f * jitterAmt * s;
					if (since < 6) {
						// still being written: a burning glyph
						sb.Draw(ReTex.GlyphAtlasGlow, p + size * new Vector2(0.5f, 0.55f), ReTex.GlyphFrame(i * 11 + since), accent.Additive(1f), 0f, glyphOrigin, 2.4f * s, SpriteEffects.None, 0f);
					}
					else {
						float glitch = ReUtils.Hash01(i, age / 4, seed + 9) > 0.96f ? 1f : 0f;
						sb.DrawString(big_, c, p + new Vector2(3f, 3f) * s, Color.Black * 0.9f, 0f, Vector2.Zero, tScale, SpriteEffects.None, 0f);
						sb.DrawString(big_, c, p + new Vector2(-2.5f - 6f * glitch, 0f) * s, new Color(255, 60, 30).Additive(0.35f), 0f, Vector2.Zero, tScale, SpriteEffects.None, 0f);
						sb.DrawString(big_, c, p + new Vector2(2.5f + 6f * glitch, 0f) * s, new Color(60, 190, 255).Additive(0.2f), 0f, Vector2.Zero, tScale, SpriteEffects.None, 0f);
						sb.DrawString(big_, c, p, Color.Lerp(Color.White, accent, 0.55f), 0f, Vector2.Zero, tScale, SpriteEffects.None, 0f);
					}
				}
				x += size.X + spacing;
			}
			// header (who sings) and the subtitle
			float hk = ReUtils.InverseLerp(14, 34, age) * open;
			Vector2 hs = small.MeasureString(header) * 0.85f * s;
			sb.DrawString(small, header, new Vector2(Main.screenWidth / 2f - hs.X / 2f, y - (big ? 92f : 66f) * s), accent * hk, 0f, Vector2.Zero, 0.85f * s, SpriteEffects.None, 0f);
			float sk = ReUtils.InverseLerp(30, 55, age) * open;
			Vector2 ss = small.MeasureString(sub) * s;
			sb.DrawString(small, sub, new Vector2(Main.screenWidth / 2f - ss.X / 2f, y + (big ? 52f : 36f) * s), new Color(230, 210, 170) * sk, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			// divider ornaments
			Texture2D div = ReTex.Divider;
			float dw = MathHelper.Min(total * 0.6f, 520f * s);
			sb.Draw(div, new Vector2(Main.screenWidth / 2f, y + (big ? 44f : 30f) * s), null, accent.Additive(0.7f * sk), 0f, div.Size() / 2f, new Vector2(dw / div.Width, 0.6f * s), SpriteEffects.None, 0f);
		}

		/// <summary>The title breaks apart into runes that fall through the screen.</summary>
		private static void ShatterTitle()
		{
			var r = new Random(seed + 77);
			int count = title.Length * 4;
			float y = BandY;
			for (int i = 0; i < count; i++) {
				float x = Main.screenWidth / 2f + (float)(r.NextDouble() - 0.5) * MathHelper.Min(title.Length * 40f * S, Main.screenWidth * 0.9f);
				shards.Add(new Shard {
					Pos = new Vector2(x, y + (float)(r.NextDouble() - 0.5) * 40f * S),
					Vel = new Vector2((float)(r.NextDouble() - 0.5) * 9f, -2f - (float)r.NextDouble() * 6f),
					Rot = 0f,
					Spin = (float)(r.NextDouble() - 0.5) * 0.3f,
					MaxLife = 50 + r.Next(50),
					Scale = (0.9f + (float)r.NextDouble()) * S,
					Glyph = r.Next(ReTex.GlyphCount),
					Color = Color.Lerp(accent, Color.White, (float)r.NextDouble() * 0.5f),
				});
			}
			SoundEngine.PlaySound(Content.NPCs.Twins.TwinSounds.Shatter with { Volume = 0.6f, Pitch = -0.2f });
		}

		private static void DrawShards(SpriteBatch sb)
		{
			foreach (Shard s in shards) {
				float a = 1f - s.Life / s.MaxLife;
				sb.Draw(ReTex.GlyphAtlasGlow, s.Pos, ReTex.GlyphFrame(s.Glyph + (int)(s.Life / 8)), s.Color.Additive(a), s.Rot, new Vector2(7, 9), s.Scale * 1.3f, SpriteEffects.None, 0f);
				FX.Bloom(sb, s.Pos, s.Color, 14f * s.Scale, 0.25f * a);
			}
		}
	}
}
