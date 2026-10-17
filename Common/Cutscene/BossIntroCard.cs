using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>Calamity-style boss title card: the name decodes out of glyphs, then dissolves back into them.</summary>
	public class BossIntroCard : ModSystem
	{
		private const int Duration = 270;
		private static int timer = -1;

		public static void Show(int npcIndex)
		{
			if (Main.dedServ)
				return;
			timer = 0;
		}

		public override void PostUpdateEverything()
		{
			if (timer >= 0 && ++timer > Duration)
				timer = -1;
		}

		public override void OnWorldUnload() => timer = -1;

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (timer < 0)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Boss Title", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.UI));
		}

		private static void Draw(SpriteBatch sb)
		{
			float t = timer;
			float fadeIn = ReUtils.InverseLerp(0, 25, t);
			float fadeOut = ReUtils.InverseLerp(Duration, Duration - 50, t);
			float alpha = fadeIn * fadeOut;
			DynamicSpriteFont font = FontAssets.DeathText.Value;
			Texture2D atlas = ReTex.GlyphAtlasGlow;
			Texture2D div = ReTex.Divider;

			float screenW = Main.screenWidth / Main.UIScale;
			float screenH = Main.screenHeight / Main.UIScale;
			Vector2 c = new(screenW / 2f, screenH * 0.24f);

			string title = Language.GetTextValue("Mods.Replicant.NPCs.Re.DisplayName").ToUpperInvariant();
			string subtitle = Language.GetTextValue("Mods.Replicant.TitleCard.Subtitle");

			VFX.FX.Bloom(sb, c, ReUtils.DeepGold, 420f, 0.22f * alpha, 1.6f, 0.35f);

			// title: wide-tracked letters, each resolving from a glyph at its own time
			float scale = 1.25f;
			float spacing = 26f;
			float total = 0f;
			foreach (char ch in title)
				total += font.MeasureString(ch.ToString()).X * scale + spacing;
			total -= spacing;
			float x = c.X - total / 2f;
			for (int i = 0; i < title.Length; i++) {
				char ch = title[i];
				float w = font.MeasureString(ch.ToString()).X * scale;
				float resolveAt = 30 + i * 12;
				float dissolveAt = Duration - 70 + i * 6;
				bool asGlyph = t < resolveAt || t > dissolveAt || (t < resolveAt + 8 && (int)t % 4 < 2);
				Vector2 pos = new(x, c.Y - font.LineSpacing * scale / 2f);
				if (asGlyph) {
					Rectangle g = ReTex.GlyphFrame((int)(ReUtils.Hash01(i, (int)(t / 3)) * ReTex.GlyphCount));
					sb.Draw(atlas, new Vector2(x + w / 2f, c.Y), g, ReUtils.Gold.Additive(alpha), 0f, new Vector2(7, 9), 3f, SpriteEffects.None, 0f);
				}
				else {
					float glow = ReUtils.InverseLerp(resolveAt + 20, resolveAt, t);
					sb.DrawString(font, ch.ToString(), pos + new Vector2(3, 3), Color.Black * alpha * 0.8f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
					sb.DrawString(font, ch.ToString(), pos, Color.Lerp(new Color(240, 234, 220), ReUtils.PaleGold, glow) * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
					if (glow > 0f)
						sb.DrawString(font, ch.ToString(), pos, ReUtils.Gold.Additive(glow * alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				}
				x += w + spacing;
			}

			// divider + subtitle
			float lineIn = ReUtils.EaseOutCubic(ReUtils.InverseLerp(40, 90, t));
			sb.Draw(div, c + new Vector2(0, 46), null, ReUtils.Gold * alpha, 0f, div.Size() / 2f, new Vector2(3.2f * lineIn, 2f), SpriteEffects.None, 0f);
			float subA = ReUtils.InverseLerp(80, 110, t) * fadeOut;
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			Vector2 ss = small.MeasureString(subtitle) * 1.1f;
			Vector2 sp = c + new Vector2(-ss.X / 2f, 62);
			sb.DrawString(small, subtitle, sp + new Vector2(2, 2), Color.Black * subA, 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
			sb.DrawString(small, subtitle, sp, new Color(210, 200, 180) * subA, 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
		}
	}
}
