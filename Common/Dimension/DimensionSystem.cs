using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Replicant.Content.Tiles;
using Terraria;
using Terraria.Graphics.Effects;
using Terraria.ModLoader;

namespace Replicant.Common.Dimension
{
	/// <summary>
	/// Atmosphere of phase 2. Active inside the pocket dimension, or — when the dimension can't be used
	/// (multiplayer / no Scenarios) — wherever the twins are fought (the "veil").
	/// </summary>
	public class DimensionSystem : ModSystem
	{
		/// <summary>Beyond the dimension's edges there is nothing: fade the real world out behind a void.</summary>
		private static void DrawVoidEdges()
		{
			Rectangle r = PocketDimension.Region;
			SpriteBatch sb = Main.spriteBatch;
			Texture2D px = Terraria.GameContent.TextureAssets.MagicPixel.Value;
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			Vector2 sp = Main.screenPosition;
			float left = r.Left * 16f - sp.X, right = r.Right * 16f - sp.X, top = r.Top * 16f - sp.Y, bottom = r.Bottom * 16f - sp.Y;
			int W = Main.screenWidth + 400, H = Main.screenHeight + 400;
			Color voidC = new(6, 3, 2);
			const int fade = 320, steps = 16;
			// solid void outside, a soft gradient fading in over the last tiles inside the edge
			if (left > -200)
				sb.Draw(px, new Rectangle(-200, -200, (int)left + 200, H), voidC);
			if (right < W)
				sb.Draw(px, new Rectangle((int)right, -200, W, H), voidC);
			if (bottom < H)
				sb.Draw(px, new Rectangle(-200, (int)bottom, W, H), voidC);
			for (int i = 0; i < steps; i++) {
				float k = 1f - i / (float)steps;
				int w = fade / steps;
				sb.Draw(px, new Rectangle((int)left + i * w, -200, w, H), voidC * (k * k));
				sb.Draw(px, new Rectangle((int)right - (i + 1) * w, -200, w, H), voidC * (k * k));
			}
			sb.End();
		}

		public static bool InDimension { get; private set; }
		/// <summary>Phase-2 atmosphere is on (dimension, or the in-place fallback during the twins fight).</summary>
		public static bool Veil { get; private set; }
		/// <summary>0..1 — the sky splitting open after the revelation.</summary>
		public static float Awaken;
		public static float AwakenTarget;
		/// <summary>0..1 extra lily light (the field ignites during the revelation).</summary>
		public static float LilyGlow;
		public static float LilyGlowTarget;
		private static float veilAmount;

		public override void Load()
		{
			if (!Main.dedServ)
				SkyManager.Instance[DimensionSky.Key] = new DimensionSky();
		}

		public override void OnWorldLoad()
		{
			DimensionBridge.ResetReport();
			Awaken = AwakenTarget = 0f;
			LilyGlow = LilyGlowTarget = 0f;
		}

		public override void PostUpdateEverything()
		{
			InDimension = DimensionBridge.InDimension;
			Veil = InDimension || TwinsFight.AnyTwinAlive;
			Awaken = MathHelper.Lerp(Awaken, AwakenTarget, 0.02f);
			LilyGlow = MathHelper.Lerp(LilyGlow, LilyGlowTarget, 0.03f);
			if (!TwinsFight.AnyTwinAlive) {
				AwakenTarget = 0f;
				LilyGlowTarget = 0f;
			}
			veilAmount = MathHelper.Lerp(veilAmount, Veil ? 1f : 0f, 0.03f);

			if (Main.dedServ)
				return;
			if (Veil && !SkyManager.Instance[DimensionSky.Key].IsActive())
				SkyManager.Instance.Activate(DimensionSky.Key);
			else if (!Veil && SkyManager.Instance[DimensionSky.Key].IsActive())
				SkyManager.Instance.Deactivate(DimensionSky.Key);
			if (Veil)
				SpawnAmbience();
		}

		private static void SpawnAmbience()
		{
			// slow golden motes rising through the dark, more once the sky has split
			if (Main.rand.NextFloat() < 0.35f + Awaken * 0.5f) {
				Vector2 p = Main.screenPosition + new Vector2(Main.rand.NextFloat(Main.screenWidth), Main.screenHeight + 10f);
				Particle m = ParticleSystem.Bloom(p, new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), -Main.rand.NextFloat(0.6f, 1.6f)),
					Color.Lerp(new Color(255, 190, 90), new Color(255, 120, 50), Awaken), Main.rand.NextFloat(0.04f, 0.1f), 260);
				m.Drag = 1f;
			}
			if (Main.rand.NextFloat() < 0.04f + Awaken * 0.1f) {
				Vector2 p = Main.screenPosition + new Vector2(Main.rand.NextFloat(Main.screenWidth), Main.screenHeight * Main.rand.NextFloat(0.4f, 1f));
				ParticleSystem.Glyph(p, new Vector2(0, -Main.rand.NextFloat(0.3f, 0.8f)), new Color(255, 200, 120) * 0.6f, 0.7f, 200).Drag = 1f;
			}
		}

		public override void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor)
		{
			if (veilAmount <= 0.01f)
				return;
			Color amber = Color.Lerp(new Color(120, 92, 54), new Color(140, 80, 50), Awaken);
			tileColor = Color.Lerp(tileColor, amber, veilAmount * 0.85f);
			backgroundColor = Color.Lerp(backgroundColor, new Color(40, 28, 14), veilAmount);
		}

		// ---------------------------------------------------------------- animated lilies
		public override void PostDrawTiles()
		{
			if (!InDimension)
				return;
			DrawVoidEdges();
			int lily = ModContent.TileType<GlyphLily>();
			Texture2D tex = ReTex.Lily;
			Texture2D glow = ReTex.LilyGlow;
			int fw = tex.Width / 3;
			int fh = tex.Height;
			int x0 = Math.Max(0, (int)(Main.screenPosition.X / 16f) - 2);
			int x1 = Math.Min(Main.maxTilesX - 1, (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + 2);
			int y0 = Math.Max(0, (int)(Main.screenPosition.Y / 16f) - 2);
			int y1 = Math.Min(Main.maxTilesY - 1, (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + 4);
			float t = Main.GlobalTimeWrappedHourly;
			Player p = Main.LocalPlayer;
			SpriteBatch sb = Main.spriteBatch;
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			for (int i = x0; i <= x1; i++) {
				for (int j = y0; j <= y1; j++) {
					Tile tile = Main.tile[i, j];
					if (!tile.HasTile || tile.TileType != lily)
						continue;
					int v = tile.TileFrameX / 18 % 3;
					Vector2 world = new(i * 16 + 8, j * 16 + 16);
					// sway in the wind, and bend away when the player brushes past
					float sway = MathF.Sin(t * 1.4f + i * 0.45f) * 0.08f;
					float dx = world.X - p.Center.X;
					if (Math.Abs(dx) < 40 && Math.Abs(world.Y - p.Bottom.Y) < 30)
						sway += Math.Sign(dx) * 0.35f * (1f - Math.Abs(dx) / 40f);
					Vector2 pos = world - Main.screenPosition;
					Color light = Lighting.GetColor(i, j);
					Rectangle src = new(v * fw, 0, fw, fh);
					Vector2 origin = new(fw / 2f, fh - 2);
					sb.Draw(tex, pos, src, Color.Lerp(light, Color.White, 0.25f), sway, origin, 1f, SpriteEffects.None, 0f);
					float breathe = 0.55f + 0.25f * MathF.Sin(t * 1.1f + i * 0.7f) + LilyGlow * 0.8f;
					sb.Draw(glow, pos, src, new Color(255, 225, 160).Additive(breathe), sway, origin, 1f, SpriteEffects.None, 0f);
					if (LilyGlow > 0.05f && (i + j) % 3 == 0)
						FX.Bloom(sb, pos + new Vector2(0, -24).RotatedBy(sway), new Color(255, 190, 90), 26f, 0.25f * LilyGlow);
				}
			}
			sb.End();
		}
	}
}

namespace Replicant.Common.Dimension
{
	/// <summary>Under the veil the vanilla hills and forests disappear; only the dimension's own sky remains.</summary>
	public class VoidSurfaceBackground : ModSurfaceBackgroundStyle
	{
		public const string TexturePath = "Replicant/Assets/Textures/Sky/VoidBackground";

		public override void ModifyFarFades(float[] fades, float transitionSpeed)
		{
			for (int i = 0; i < fades.Length; i++) {
				if (i == Slot)
					fades[i] = MathF.Min(1f, fades[i] + transitionSpeed);
				else
					fades[i] = MathF.Max(0f, fades[i] - transitionSpeed);
			}
		}

		private static int EmptySlot => BackgroundTextureLoader.GetBackgroundSlot(TexturePath);
		public override int ChooseFarTexture() => EmptySlot;
		public override int ChooseMiddleTexture() => EmptySlot;
		public override bool PreDrawCloseBackground(SpriteBatch spriteBatch) => false;
	}

	public class VeilScene : ModSceneEffect
	{
		public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;
		public override ModSurfaceBackgroundStyle SurfaceBackgroundStyle => ModContent.GetInstance<VoidSurfaceBackground>();
		public override bool IsSceneEffectActive(Player player) => DimensionSystem.Veil;
	}
}
