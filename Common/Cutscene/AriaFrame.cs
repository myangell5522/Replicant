using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles.Twins;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// While the sky rift is open the screen becomes a stage: ornate gold rails slide in from the edges,
	/// corner brackets made of script lock into place, and the space outside the walkable band darkens —
	/// so the player always sees exactly how much room the Aria leaves them.
	/// </summary>
	public class AriaFrame : ModSystem
	{
		private static float show;

		public override void PostUpdateEverything()
		{
			if (Main.dedServ)
				return;
			Projectile rift = SkyRift.Find();
			float target = rift != null ? rift.ModProjectile is SkyRift sr ? sr.Open : 0f : 0f;
			show = MathHelper.Lerp(show, target, target > show ? 0.08f : 0.05f);
			if (show < 0.004f)
				show = 0f;
		}

		public override void OnWorldUnload() => show = 0f;

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (show <= 0f)
				return;
			int i = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
			if (i < 0)
				i = 0;
			layers.Insert(i, new LegacyGameInterfaceLayer("Replicant: Aria Frame", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			float a = ReUtils.EaseOutCubic(show);
			float t = Main.GlobalTimeWrappedHourly;
			int W = Main.screenWidth, H = Main.screenHeight;
			float s = MathHelper.Clamp(H / 1080f, 0.7f, 1.6f);
			int m = (int)(34 * s * a);       // rails slide in from off screen
			int bar = (int)(6 * s);

			// vignette outside the rails
			sb.Draw(px, new Rectangle(0, 0, W, m), Color.Black * (0.55f * a));
			sb.Draw(px, new Rectangle(0, H - m, W, m), Color.Black * (0.55f * a));
			sb.Draw(px, new Rectangle(0, m, m, H - 2 * m), Color.Black * (0.55f * a));
			sb.Draw(px, new Rectangle(W - m, m, m, H - 2 * m), Color.Black * (0.55f * a));

			Color gold = new Color(255, 196, 92);
			Color dark = new Color(70, 40, 12);
			// double rail: a dark bed and a bright inner line, with a light travelling round the frame
			void Rail(Rectangle r, bool horizontal)
			{
				sb.Draw(px, r, dark * (0.9f * a));
				Rectangle inner = horizontal ? new Rectangle(r.X, r.Y + r.Height / 2 - 1, r.Width, 2) : new Rectangle(r.X + r.Width / 2 - 1, r.Y, 2, r.Height);
				sb.Draw(px, inner, gold.Additive(0.75f * a));
			}
			Rail(new Rectangle(m, m - bar, W - 2 * m, bar), true);
			Rail(new Rectangle(m, H - m, W - 2 * m, bar), true);
			Rail(new Rectangle(m - bar, m, bar, H - 2 * m), false);
			Rail(new Rectangle(W - m, m, bar, H - 2 * m), false);

			// pixel tick marks along the rails like a score's staff, scrolling
			int step = (int)(48 * s);
			int scroll = (int)(t * 40f * s) % step;
			for (int x = m + scroll; x < W - m; x += step) {
				sb.Draw(px, new Rectangle(x, m - bar - (int)(5 * s), (int)(2 * s), (int)(4 * s)), gold.Additive(0.5f * a));
				sb.Draw(px, new Rectangle(W - x, H - m + bar + (int)(1 * s), (int)(2 * s), (int)(4 * s)), gold.Additive(0.5f * a));
			}

			// a spark circling the frame
			float per = 2f * (W + H - 4 * m);
			float d = (t * 900f * s) % per;
			Vector2 sp;
			if (d < W - 2 * m) sp = new Vector2(m + d, m - bar / 2f);
			else if ((d -= W - 2 * m) < H - 2 * m) sp = new Vector2(W - m + bar / 2f, m + d);
			else if ((d -= H - 2 * m) < W - 2 * m) sp = new Vector2(W - m - d, H - m + bar / 2f);
			else { d -= W - 2 * m; sp = new Vector2(m - bar / 2f, H - m - d); }
			FX.Bloom(sb, sp, gold, 40f * s, 0.6f * a, 1f, 0.2f);

			// corner brackets: an L of glyphs that locks into place
			for (int c = 0; c < 4; c++) {
				int sx = c % 2 == 0 ? 1 : -1, sy = c < 2 ? 1 : -1;
				Vector2 corner = new(c % 2 == 0 ? m : W - m, c < 2 ? m : H - m);
				float lockIn = ReUtils.EaseOutBack(ReUtils.InverseLerp(0.3f, 1f, show));
				int len = (int)(120 * s * lockIn);
				sb.Draw(px, new Rectangle((int)(sx > 0 ? corner.X : corner.X - len), (int)corner.Y - (int)(sy > 0 ? 0 : 3 * s), len, (int)(3 * s)), gold.Additive(0.9f * a));
				sb.Draw(px, new Rectangle((int)corner.X - (int)(sx > 0 ? 0 : 3 * s), (int)(sy > 0 ? corner.Y : corner.Y - len), (int)(3 * s), len), gold.Additive(0.9f * a));
				for (int g = 0; g < 4; g++) {
					Vector2 gp = corner + new Vector2(sx * (18 + g * 22) * s * lockIn, sy * 16 * s);
					sb.Draw(ReTex.GlyphAtlasGlow, gp, ReTex.GlyphFrame(c * 7 + g + (int)(t * 3f)), ReUtils.PaleGold.Additive(0.7f * a * (0.6f + 0.4f * MathF.Sin(t * 5f + g))), 0f, new Vector2(7, 9), s, SpriteEffects.None, 0f);
				}
				FX.Bloom(sb, corner, gold, 70f * s, 0.35f * a, 1f, 0.1f);
			}
		}
	}
}
