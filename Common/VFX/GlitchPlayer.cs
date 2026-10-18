using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.Dimension;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.VFX
{
	/// <summary>
	/// The player's body glitches while it is "being rewritten": after a lost intrusion, or whenever the
	/// sisters' disorientation (Confused / Darkness) holds them in the Song's dimension. The sprite jumps
	/// between frames, and black/gold/ember slices tear across it with shards of script.
	/// </summary>
	public class GlitchPlayer : ModPlayer
	{
		public int Glitch;

		public static void Apply(Player p, int ticks)
		{
			GlitchPlayer g = p.GetModPlayer<GlitchPlayer>();
			g.Glitch = Math.Max(g.Glitch, ticks);
		}

		public float Strength {
			get {
				float k = MathHelper.Clamp(Glitch / 60f, 0f, 1f);
				if (Subworld.InDimension && (Player.HasBuff(BuffID.Confused) || Player.HasBuff(BuffID.Darkness)))
					k = MathF.Max(k, 0.6f);
				return k;
			}
		}

		public override void PostUpdate()
		{
			if (Glitch > 0)
				Glitch--;
		}

		public override void ModifyDrawInfo(ref PlayerDrawSet drawInfo)
		{
			float k = Strength;
			if (k <= 0f || drawInfo.shadow != 0f)
				return;
			int frame = (int)(Main.GlobalTimeWrappedHourly * 24f);
			if (ReUtils.Hash01(frame, Player.whoAmI, 3) < 0.25f * k)
				drawInfo.Position.X += (ReUtils.Hash01(frame, 7) - 0.5f) * 14f * k;
		}

		public override void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g, ref float b, ref float a, ref bool fullBright)
		{
			float k = Strength;
			if (k <= 0f)
				return;
			// warm the body toward the mod's gold
			r = MathHelper.Lerp(r, 1.2f, 0.3f * k);
			g = MathHelper.Lerp(g, 0.85f, 0.3f * k);
			b = MathHelper.Lerp(b, 0.4f, 0.4f * k);
		}
	}

	public class GlitchOverlay : ModSystem
	{
		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int i = layers.FindIndex(l => l.Name == "Vanilla: Entity Health Bars");
			if (i < 0)
				i = 0;
			layers.Insert(i, new LegacyGameInterfaceLayer("Replicant: Glitch", () => {
				foreach (Player p in Main.ActivePlayers) {
					if (!p.dead && p.GetModPlayer<GlitchPlayer>().Strength > 0f)
						Draw(Main.spriteBatch, p, p.GetModPlayer<GlitchPlayer>().Strength);
				}
				return true;
			}, InterfaceScaleType.Game));
		}

		private static void Draw(SpriteBatch sb, Player p, float k)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			Vector2 c = p.Center - Main.screenPosition;
			int frame = (int)(Main.GlobalTimeWrappedHourly * 20f);
			int slices = (int)(3 + 7 * k);
			for (int i = 0; i < slices; i++) {
				float h = ReUtils.Hash01(i, frame, p.whoAmI);
				if (h > 0.35f + 0.5f * k)
					continue;
				float y = (ReUtils.Hash01(i, frame, 11) - 0.5f) * 52f;
				float w = 14f + ReUtils.Hash01(i, frame, 12) * 40f * k;
				float x = (ReUtils.Hash01(i, frame, 13) - 0.5f) * 30f - w / 2f;
				float hh = 1f + ReUtils.Hash01(i, frame, 14) * 4f;
				float cc = ReUtils.Hash01(i, frame, 15);
				Color col = cc < 0.4f ? Color.Black * 0.8f : cc < 0.8f ? ReUtils.Gold.Additive(0.8f) : new Color(255, 80, 30).Additive(0.8f);
				sb.Draw(px, new Rectangle((int)(c.X + x), (int)(c.Y + y), (int)w, (int)hh), col * k);
			}
			if (ReUtils.Hash01(frame, p.whoAmI, 21) < 0.4f * k) {
				Vector2 at = c + new Vector2((ReUtils.Hash01(frame, 22) - 0.5f) * 40f, (ReUtils.Hash01(frame, 23) - 0.5f) * 50f);
				sb.Draw(ReTex.GlyphAtlasGlow, at, ReTex.GlyphFrame(frame), ReUtils.PaleGold.Additive(0.9f * k), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
			// thin offset outlines of the hitbox, like a misaligned frame buffer
			float o = 3f * k * (ReUtils.Hash01(frame, 31) > 0.5f ? 1f : -1f);
			Rectangle hb = new((int)(p.position.X - Main.screenPosition.X + o), (int)(p.position.Y - Main.screenPosition.Y), p.width, p.height);
			sb.Draw(px, new Rectangle(hb.X - 2, hb.Y, 1, hb.Height), new Color(255, 120, 40).Additive(0.5f * k));
			sb.Draw(px, new Rectangle(hb.Right + 1, hb.Y, 1, hb.Height), ReUtils.Gold.Additive(0.5f * k));
		}
	}
}
