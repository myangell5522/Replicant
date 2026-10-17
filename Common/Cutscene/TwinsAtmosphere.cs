using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// The film the twins fight is shot on: a breathing amber vignette, grain, light leaks, slow glyphs drifting
	/// past the lens in the foreground — and, as the false fight nears 2:00, the picture itself starts tearing.
	/// Drawn under the HUD (the attack reveals are the ones that break through it).
	/// </summary>
	public class TwinsAtmosphere : ModSystem
	{
		private static float amount;
		private static float tear;         // 0..1, the picture tearing
		private static int tearSeed;
		private struct Mote { public Vector2 Pos; public float Speed, Scale, Phase; public int Glyph; }
		private static readonly List<Mote> motes = new();

		public override void PostUpdateEverything()
		{
			if (Main.dedServ)
				return;
			bool on = TwinsFight.AnyTwinAlive && !RiddleCutscene.Active;
			amount = MathHelper.Lerp(amount, on ? 1f : 0f, 0.02f);
			tear *= 0.9f;
			if (!on)
				return;
			int t = TwinsFight.FightTicks;
			// something isn't right: the picture tears more and more often as 2:00 approaches
			if (t > TwinsTimeline.IntroEnd && t < TwinsTimeline.RevealStart) {
				float k = ReUtils.InverseLerp(TwinsTimeline.IntroEnd + 1800, TwinsTimeline.RevealStart, t);
				if (Main.rand.NextFloat() < 0.002f + k * 0.03f) {
					tear = MathF.Max(tear, 0.35f + k * 0.6f);
					tearSeed = Main.rand.Next(10000);
				}
			}
			// foreground glyphs drifting past the lens, big and soft
			if (motes.Count < 10 && Main.rand.NextBool(40)) {
				motes.Add(new Mote {
					Pos = new Vector2(Main.rand.NextFloat(Main.screenWidth), Main.screenHeight + 60f),
					Speed = Main.rand.NextFloat(0.4f, 1.1f),
					Scale = Main.rand.NextFloat(2.5f, 5f),
					Phase = Main.rand.NextFloat(6.28f),
					Glyph = Main.rand.Next(ReTex.GlyphCount),
				});
			}
			for (int i = motes.Count - 1; i >= 0; i--) {
				Mote m = motes[i];
				m.Pos.Y -= m.Speed;
				m.Pos.X += MathF.Sin(Main.GlobalTimeWrappedHourly * 0.6f + m.Phase) * 0.4f;
				motes[i] = m;
				if (m.Pos.Y < -80f)
					motes.RemoveAt(i);
			}
		}

		public override void OnWorldUnload() => motes.Clear();

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (amount < 0.01f)
				return;
			// below every vanilla HUD element
			layers.Insert(0, new LegacyGameInterfaceLayer("Replicant: Twins Atmosphere", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			Rectangle screen = new(0, 0, W, H);
			float time = Main.GlobalTimeWrappedHourly;
			float aw = DimensionSystem.Awaken;
			Texture2D px = TextureAssets.MagicPixel.Value;

			// foreground glyph motes (out of focus: soft and large)
			foreach (Mote m in motes) {
				float a = 0.12f * amount * (0.6f + 0.4f * MathF.Sin(time * 1.3f + m.Phase));
				FX.Bloom(sb, m.Pos, new Color(255, 190, 100), 22f * m.Scale, a * 0.6f);
				sb.Draw(ReTex.GlyphAtlasGlow, m.Pos, ReTex.GlyphFrame(m.Glyph + (int)(time * 0.5f + m.Phase)), new Color(255, 200, 120).Additive(a), MathF.Sin(time * 0.3f + m.Phase) * 0.3f, new Vector2(7, 9), m.Scale, SpriteEffects.None, 0f);
			}

			// light leaks from the edges, warmer once the sky has split
			float leak = (0.08f + 0.05f * MathF.Sin(time * 0.7f) + aw * 0.06f) * amount;
			Color leakC = Color.Lerp(new Color(255, 170, 70), new Color(255, 100, 40), aw);
			FX.Bloom(sb, new Vector2(-W * 0.05f, H * (0.3f + 0.1f * MathF.Sin(time * 0.21f))), leakC, H * 0.7f, leak, 0.8f, 1.4f);
			FX.Bloom(sb, new Vector2(W * 1.05f, H * (0.6f + 0.1f * MathF.Sin(time * 0.17f + 2f))), leakC, H * 0.6f, leak * 0.8f, 0.8f, 1.4f);

			// breathing vignette, pulsing with the song
			float pulse = 0.55f + 0.08f * MathF.Sin(time * 1.6f) + aw * 0.1f;
			sb.Draw(ReTex.Vignette, screen, new Color(30, 14, 4) * (pulse * amount));

			// film grain
			Texture2D grain = ReTex.Grain;
			Vector2 off = new(Main.rand.Next(grain.Width), Main.rand.Next(grain.Height));
			for (int x = -1; x <= W / grain.Width + 1; x++) {
				for (int y = -1; y <= H / grain.Height + 1; y++)
					sb.Draw(grain, new Vector2(x * grain.Width, y * grain.Height) - off, null, new Color(220, 200, 170).Additive(0.035f * amount), 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
			}

			// the picture tears: black bands slide sideways, each rimmed with gold and filled with glyph noise
			if (tear > 0.03f) {
				int bands = 3 + (int)(tear * 6);
				int frame = (int)(time * 20f);
				for (int b = 0; b < bands; b++) {
					float hy = ReUtils.Hash01(tearSeed + b, frame);
					float hh = ReUtils.Hash01(tearSeed + b, frame, 3);
					int y = (int)(hy * H);
					int h = (int)(3 + hh * 26 * tear);
					int shift = (int)((ReUtils.Hash01(tearSeed + b, frame, 5) - 0.5f) * 120f * tear);
					sb.Draw(px, new Rectangle(0, y, W, h), Color.Black * (0.55f * tear));
					sb.Draw(px, new Rectangle(shift, y, W, 1), new Color(255, 60, 30).Additive(0.5f * tear));
					sb.Draw(px, new Rectangle(-shift, y + h, W, 1), new Color(80, 190, 255).Additive(0.3f * tear));
					for (int g = 0; g < W / 40; g++) {
						if (ReUtils.Hash01(b, g, frame) > 0.35f)
							continue;
						sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(g * 40 + shift, y + h / 2f), ReTex.GlyphFrame(g + b * 7 + frame), new Color(255, 196, 70).Additive(0.8f * tear), 0f, new Vector2(7, 9), 0.8f, SpriteEffects.None, 0f);
					}
				}
			}
		}
	}
}
