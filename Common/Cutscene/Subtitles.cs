using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Replicant.Common.VFX;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// Yellow battle subtitles with a speaker portrait. Letters are "written" in glyphs first and resolve
	/// into text; the line shimmers with a faint chromatic split, and fades out on its own.
	/// </summary>
	public class Subtitles : ModSystem
	{
		public const int Requiem = 0, Refrain = 1, Both = 2, Unknown = 3;

		private class Line
		{
			public int Speaker;
			public string Text;
			public int Age;
			public int Duration;
			public float Fade = 1f;
		}

		private static readonly List<Line> lines = new();

		public static void Push(int speaker, string key, int duration = 300)
		{
			if (Main.dedServ)
				return;
			foreach (Line l in lines)
				l.Duration = Math.Min(l.Duration, l.Age + 20); // hurry the previous line out
			lines.Add(new Line { Speaker = speaker, Text = Language.GetTextValue(key), Duration = duration });
		}

		public static void Clear() => lines.Clear();

		/// <summary>Someone is speaking right now.</summary>
		public static bool Busy => lines.Count > 0;

		public override void OnWorldUnload() => lines.Clear();

		public override void PostUpdateEverything()
		{
			for (int i = lines.Count - 1; i >= 0; i--) {
				Line l = lines[i];
				l.Age++;
				l.Fade = ReUtils.InverseLerp(0, 12, l.Age) * ReUtils.InverseLerp(l.Duration, l.Duration - 30, l.Age);
				if (l.Age > l.Duration)
					lines.RemoveAt(i);
			}
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (lines.Count == 0)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			// above the cinematic bars and the riddle overlay
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Subtitles", () => {
				foreach (Line l in lines)
					DrawLine(Main.spriteBatch, l);
				return true;
			}, InterfaceScaleType.None));
		}

		private static string SpeakerName(int s) => Language.GetTextValue("Mods.Replicant.Twins.Speaker" + s);

		private static void DrawLine(SpriteBatch sb, Line l)
		{
			if (l.Fade <= 0.01f)
				return;
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			float s = MathHelper.Clamp(Main.screenHeight / 1080f, 0.75f, 1.6f);
			float scale = 1.22f * s;
			float y = Main.screenHeight * 0.79f;
			float a = l.Fade;
			string text = l.Text;
			Vector2 size = font.MeasureString(text) * scale;
			float portraitW = 92f * s;
			float totalW = size.X + portraitW;
			float x0 = Main.screenWidth / 2f - totalW / 2f + portraitW;

			// dark backing band (soft, so it never reads as a box)
			FX.Smoke(sb, new Vector2(Main.screenWidth / 2f, y + 18 * s), Color.Black * (0.55f * a), totalW * 0.62f + 80f, 0f, 1f);
			FX.Smoke(sb, new Vector2(Main.screenWidth / 2f, y + 18 * s), Color.Black * (0.35f * a), totalW * 0.4f + 60f, 1.7f, 1.5f);

			DrawPortrait(sb, l.Speaker, new Vector2(x0 - portraitW * 0.55f, y + 14 * s), s, a, l.Age);

			// speaker name: small, gold, tracked out
			string name = SpeakerName(l.Speaker).ToUpperInvariant();
			float nx = x0;
			foreach (char ch in name) {
				sb.DrawString(font, ch.ToString(), new Vector2(nx, y - 22 * s), new Color(200, 150, 70) * a, 0f, Vector2.Zero, 0.78f * s, SpriteEffects.None, 0f);
				nx += font.MeasureString(ch.ToString()).X * 0.78f * s + 2.5f * s;
			}

			// what they actually sing: the line in their own script (like NieR's chaos language), written
			// a beat ahead of the translation and drifting faintly, a little dimmer
			{
				float gx = x0;
				float gy = y + font.LineSpacing * scale * 0.95f + 10f * s;
				int sung = (int)(l.Age * 1.3f);
				float gScale = 0.85f * s;
				for (int i = 0; i < text.Length && i < sung; i++) {
					char ch = text[i];
					if (ch == ' ') {
						gx += 9f * s;
						continue;
					}
					if (char.IsPunctuation(ch))
						continue;
					int g = (int)(ReUtils.Hash01(char.ToLowerInvariant(ch), 17) * ReTex.GlyphCount);
					float drift = MathF.Sin(Main.GlobalTimeWrappedHourly * 2.4f + i * 0.5f) * 1.2f * s;
					sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(gx, gy + drift), ReTex.GlyphFrame(g), (l.Speaker == Unknown ? new Color(140, 180, 255) : new Color(230, 160, 70)).Additive(0.55f * a), 0f, Vector2.Zero, gScale, SpriteEffects.None, 0f);
					gx += ReTex.GlyphCellW * gScale * 0.82f;
				}
			}

			// the line itself
			int reveal = (int)(l.Age * 0.9f);
			Texture2D atlas = ReTex.GlyphAtlas;
			Color gold = l.Speaker == Unknown ? new Color(170, 205, 255) : l.Speaker == Both ? new Color(255, 226, 150) : new Color(255, 212, 88);
			float t = Main.GlobalTimeWrappedHourly;
			for (int i = 0; i < text.Length && i < reveal; i++) {
				char ch = text[i];
				if (ch == ' ')
					continue;
				float cx = x0 + font.MeasureString(text.Substring(0, i)).X * scale;
				float wave = MathF.Sin(t * 3f + i * 0.35f) * 0.8f * s;
				Vector2 p = new(cx, y + wave);
				int age = reveal - i;
				if (age < 7) {
					// freshly written: still a glyph
					Rectangle g = ReTex.GlyphFrame((int)(ReUtils.Hash01(i, age / 2, l.Text.Length) * ReTex.GlyphCount));
					Vector2 gp = p + new Vector2(font.MeasureString(ch.ToString()).X * scale / 2f, font.LineSpacing * scale * 0.45f);
					sb.Draw(atlas, gp, g, gold * a, 0f, new Vector2(7, 9), 0.9f * s, SpriteEffects.None, 0f);
					continue;
				}
				string c = ch.ToString();
				sb.DrawString(font, c, p + new Vector2(2f, 2f) * s, Color.Black * (0.85f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				// faint chromatic split
				sb.DrawString(font, c, p + new Vector2(-1.2f * s, 0f), new Color(255, 60, 30).Additive(0.22f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				sb.DrawString(font, c, p + new Vector2(1.2f * s, 0f), new Color(60, 200, 255).Additive(0.12f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				if (l.Speaker == Both)
					sb.DrawString(font, c, p + new Vector2(2.5f, 1.5f) * s, new Color(255, 120, 60) * (0.35f * a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				sb.DrawString(font, c, p, gold * a, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			}
		}

		private static void DrawPortrait(SpriteBatch sb, int speaker, Vector2 center, float s, float a, int age)
		{
			if (speaker == Unknown) {
				// no face: just the blue eyes from the riddle
				Texture2D eyes = ReTex.Eyes;
				int efw = eyes.Width / 6, efh = eyes.Height / 10;
				FX.Bloom(sb, center, ReUtils.EyeBlue, 40f * s, 0.3f * a);
				sb.Draw(eyes, center, new Rectangle(0, 0, efw, efh), Color.White * a, 0f, new Vector2(efw / 2f, efh / 2f), 0.9f * s, SpriteEffects.None, 0f);
				return;
			}
			if (speaker == Both) {
				DrawPortrait(sb, Requiem, center + new Vector2(-14 * s, -4 * s), s * 0.85f, a, age);
				DrawPortrait(sb, Refrain, center + new Vector2(14 * s, 4 * s), s * 0.85f, a, age);
				return;
			}
			Texture2D sheet = speaker == Requiem ? ReTex.RequiemSheet : ReTex.RefrainSheet;
			int fh = sheet.Height / 24;
			// head region of the idle frame (blink on a slow cycle; frame 6 has closed eyes)
			int frame = (age / 8) % 8;   // no eyes to blink: idle frames carry their glyph glitch
			Rectangle src = new(50, frame * fh + 12, 44, 48);
			float ps = 1.55f * s;
			FX.Bloom(sb, center, speaker == Requiem ? new Color(255, 170, 80) : new Color(255, 210, 120), 46f * s, 0.25f * a);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			sb.Draw(sheet, center, src, Color.White * a, 0f, src.Size() / 2f, ps, SpriteEffects.None, 0f);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		}
	}
}
