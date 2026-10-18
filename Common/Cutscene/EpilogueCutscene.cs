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
	/// After the last sister falls: the world goes quiet, the sky cracks open once more — and the blue eyes
	/// from the riddle look down through it, enormous, and speak. This was not the end of the song.
	/// </summary>
	public class EpilogueCutscene : ModSystem
	{
		public const int Length = 640;
		private static int timer = -1;

		public static bool Active => timer >= 0;

		public static void Begin()
		{
			if (Main.dedServ || Active)
				return;
			timer = 0;
			Cinematic.Begin(Main.LocalPlayer.Center - new Vector2(0, 120), Length, silenceTicks: Length - 60);
		}

		public override void OnWorldUnload() => timer = -1;

		public override void PostUpdateEverything()
		{
			if (!Active)
				return;
			timer++;
			int t = timer;
			// in the dimension the eyes look down from the sky itself, behind everything
			if (Dimension.DimensionBridge.InDimension) {
				float eo = ReUtils.InverseLerp(120, 200, t) * (1f - ReUtils.InverseLerp(500, 540, t));
				float dk = ReUtils.InverseLerp(0, 80, t) * (1f - ReUtils.InverseLerp(560, Length, t));
				Dimension.DimensionSky.HoldEyes(eo, dk, t < 140 ? 0 : t < 160 ? 1 : t > 505 ? 1 : 2, t > 300 && t < 330 ? 1 : 0);
			}
			if (t == 2)
				SoundEngine.PlaySound(Content.NPCs.Recursion.RecursionSounds.OminousDrone with { Volume = 0.7f });
			if (t == 1)
				SoundEngine.PlaySound(ReSounds.Heartbeat with { Volume = 0.9f, Pitch = -0.3f });
			if (t == 70) {
				SoundEngine.PlaySound(ReSounds.EyesOpen with { Pitch = -0.5f });
				ScreenFX.Shake(12f);
			}
			if (t % 70 == 0 && t < 470)
				SoundEngine.PlaySound(ReSounds.Heartbeat with { Volume = 0.6f, Pitch = -0.4f });
			if (t == 150)
				Subtitles.Push(Subtitles.Unknown, "Mods.Replicant.Twins.Lines.Epilogue1", 170);
			if (t == 320)
				Subtitles.Push(Subtitles.Unknown, "Mods.Replicant.Twins.Lines.Epilogue2", 160);
			if (t == 470)
				Subtitles.Push(Subtitles.Unknown, "Mods.Replicant.Twins.Lines.Epilogue3", 150);
			if (t == 500)
				SoundEngine.PlaySound(ReSounds.EyesClose with { Pitch = -0.4f });
			if (t == 560) {
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 0.6f, Pitch = -0.6f });
				Cinematic.Flash(0.7f);
				ScreenFX.Shake(20f);
			}
			if (t >= Length)
				timer = -1;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (!Active)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Epilogue", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			int t = timer;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float time = Main.GlobalTimeWrappedHourly;
			float s = MathHelper.Clamp(H / 1080f, 0.7f, 1.8f);
			bool sky = Dimension.DimensionBridge.InDimension;
			float dark = ReUtils.InverseLerp(0, 60, t) * (1f - ReUtils.InverseLerp(570, Length, t));
			sb.Draw(px, new Rectangle(0, 0, W, H), new Color(2, 3, 8) * ((sky ? 0.3f : 0.72f) * dark));

			// the crack in the sky
			float open = sky ? 0f : ReUtils.EaseOutCubic(ReUtils.InverseLerp(60, 140, t)) * (1f - ReUtils.EaseInCubic(ReUtils.InverseLerp(520, 562, t)));
			float y = H * 0.27f;
			const int col = 6;
			for (int x = 0; x < W; x += col) {
				float u = x / (float)W;
				float env = MathF.Pow(MathF.Sin(u * MathHelper.Pi), 0.6f);
				float half = (6f + 120f * env * (0.7f + 0.3f * ReUtils.Hash01(x / col, 4))) * open * s;
				float wob = MathF.Sin(u * 17f) * 14f * s;
				sb.Draw(px, new Rectangle(x, (int)(y + wob - half), col, (int)(half * 2f)), new Color(1, 2, 6) * MathF.Min(1f, open * 2f));
				Color lip = Color.Lerp(ReUtils.EyeBlue, Color.White, 0.3f + 0.3f * MathF.Sin(time * 4f + x * 0.05f));
				sb.Draw(px, new Rectangle(x, (int)(y + wob - half - 2), col, 3), lip.Additive(0.9f * open));
				sb.Draw(px, new Rectangle(x, (int)(y + wob + half - 1), col, 3), lip.Additive(0.9f * open));
			}
			FX.Bloom(sb, new Vector2(W / 2f, y), ReUtils.EyeBlue, W * 0.5f, 0.15f * open, 1f, 0.2f);

			// the eyes, looking down through it
			float eyeOpen = sky ? 0f : ReUtils.InverseLerp(120, 200, t) * (1f - ReUtils.InverseLerp(500, 540, t));
			if (eyeOpen > 0f) {
				Texture2D eyes = ReTex.Eyes;
				Texture2D glow = ReTex.EyesGlow;
				const int cols = 6, rows = 10;
				int fw = eyes.Width / cols, fh = eyes.Height / rows;
				int row = t < 140 ? 7 : t < 160 ? 4 : t > 520 ? 8 : t > 505 ? 5 : 0;
				int level = t > 300 && t < 330 && t % 6 < 3 ? 2 : 0;   // a flicker of corruption as it speaks
				Rectangle src = new(level * fw, row * fh, fw, fh);
				float es = 7.5f * s;
				Vector2 c = new(W / 2f + MathF.Sin(time * 0.6f) * 6f, y + MathF.Sin(time * 0.9f) * 3f);
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
				sb.Draw(eyes, c, src, new Color(200, 205, 215) * eyeOpen, 0f, new Vector2(fw / 2f, fh / 2f), es, SpriteEffects.None, 0f);
				sb.Draw(glow, c, src, Color.White.Additive(0.7f * eyeOpen), 0f, new Vector2(fw / 2f, fh / 2f), es, SpriteEffects.None, 0f);
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
				FX.Bloom(sb, c, ReUtils.EyeBlue, 260f * s, 0.18f * eyeOpen, 2f, 0.6f);
			}

			// the closing card: this was only the first verse
			float card = ReUtils.InverseLerp(575, 595, t) * (1f - ReUtils.InverseLerp(Length - 20, Length, t));
			if (card > 0f) {
				DynamicSpriteFont big = FontAssets.DeathText.Value;
				DynamicSpriteFont small = FontAssets.MouseText.Value;
				string a = Language.GetTextValue("Mods.Replicant.Twins.Epilogue.Title");
				string b = Language.GetTextValue("Mods.Replicant.Twins.Epilogue.Sub");
				Vector2 sa = big.MeasureString(a) * 0.9f * s, sbz = small.MeasureString(b) * s;
				sb.DrawString(big, a, new Vector2(W / 2f, H * 0.62f) - sa / 2f, ReUtils.EyeBlue * card, 0f, Vector2.Zero, 0.9f * s, SpriteEffects.None, 0f);
				sb.DrawString(small, b, new Vector2(W / 2f, H * 0.62f + sa.Y * 0.7f) - sbz / 2f, new Color(200, 210, 230) * card, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			}
		}
	}
}
