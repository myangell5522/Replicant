using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Dimension;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Tiles
{
	/// <summary>
	/// Glossy black stone of the dimension. Drawn with its own small auto-tiling (fill + glossy top cap +
	/// edge lines) instead of the vanilla frame sheet. Unbreakable.
	/// </summary>
	public class OilStone : ModTile
	{
		public override string Texture => "Replicant/Assets/Textures/Tiles/OilStone";

		public override void SetStaticDefaults()
		{
			Main.tileSolid[Type] = true;
			Main.tileBlockLight[Type] = true;
			Main.tileMergeDirt[Type] = false;
			MinPick = 10000;
			DustType = DustID.Obsidian;
			AddMapEntry(new Color(22, 16, 12));
		}

		public override bool CanExplode(int i, int j) => false;
		public override bool CanKillTile(int i, int j, ref bool blockDamaged) => false;

		public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
		{
			Texture2D tex = ModContent.Request<Texture2D>(Texture).Value;
			Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
			Vector2 pos = new Vector2(i * 16, j * 16) - Main.screenPosition + zero;
			Color light = Lighting.GetColor(i, j);
			int v = (int)(ReUtils.Hash01(i, j) * 4f) & 3;
			bool up = Solid(i, j - 1), left = Solid(i - 1, j), right = Solid(i + 1, j), down = Solid(i, j + 1);
			// in the dimension the top of the ground is a smooth skin of oil drawn over the field; the stepped
			// blocks underneath it are never shown
			if (Subworld.InDimension && (!up || !Solid(i, j - 2) || !Solid(i, j - 3)))
				return false;
			spriteBatch.Draw(tex, pos, new Rectangle(v * 16, 0, 16, 16), light);
			if (!up) {
				spriteBatch.Draw(tex, pos, new Rectangle(v * 16, 16, 16, 16), light);
				// oily gloss catches the sky light a little brighter than the tile itself
				spriteBatch.Draw(tex, pos, new Rectangle(48, 32, 16, 16), Color.Lerp(light, new Color(255, 220, 160), 0.25f) * 0.8f);
			}
			if (!left)
				spriteBatch.Draw(tex, pos, new Rectangle(0, 32, 16, 16), light);
			if (!right)
				spriteBatch.Draw(tex, pos, new Rectangle(16, 32, 16, 16), light);
			if (!down)
				spriteBatch.Draw(tex, pos, new Rectangle(32, 32, 16, 16), light);
			return false;
		}

		private bool Solid(int i, int j)
		{
			Tile t = Framing.GetTileSafely(i, j);
			return t.HasTile && Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType];
		}
	}

	/// <summary>
	/// A pale glyph lily that grows on the oil fields. Invisible in the tile layer: <see cref="DimensionSystem"/>
	/// draws every lily on screen each frame so they can sway, breathe light and ignite during the revelation.
	/// </summary>
	public class GlyphLily : ModTile
	{
		public override string Texture => "Replicant/Assets/Textures/Tiles/GlyphLily";

		public override void SetStaticDefaults()
		{
			Main.tileFrameImportant[Type] = true;
			Main.tileSolid[Type] = false;
			Main.tileLighted[Type] = true;
			Main.tileNoAttach[Type] = true;
			Main.tileCut[Type] = false;
			MinPick = 10000;
			AddMapEntry(new Color(230, 214, 170));
		}

		public override bool CanKillTile(int i, int j, ref bool blockDamaged) => false;
		public override bool CanExplode(int i, int j) => false;

		public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
		{
			float k = 0.22f + DimensionSystem.LilyGlow * 0.55f;
			float flick = 0.85f + 0.15f * MathF.Sin(Main.GlobalTimeWrappedHourly * 1.7f + i * 0.9f);
			r = 0.95f * k * flick;
			g = 0.72f * k * flick;
			b = 0.32f * k * flick;
		}

		public override bool PreDraw(int i, int j, SpriteBatch spriteBatch) => false;
	}
}
