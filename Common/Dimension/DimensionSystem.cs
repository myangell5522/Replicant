using System.Collections.Generic;
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

		private static readonly List<(Vector2 pos, float radius, float vx, int life)> fieldPushes = new();

		/// <summary>Something huge brushes the field: the lilies around a point bow away for a moment.</summary>
		public static void PushField(Vector2 pos, float radius, float vx)
		{
			if (fieldPushes.Count < 24)
				fieldPushes.Add((pos, radius, vx, 30));
		}

		public override void PostUpdateEverything()
		{
			for (int i = fieldPushes.Count - 1; i >= 0; i--) {
				var f = fieldPushes[i];
				if (--f.life <= 0)
					fieldPushes.RemoveAt(i);
				else
					fieldPushes[i] = f;
			}
			InDimension = DimensionBridge.InDimension;
			Veil = InDimension || TwinsFight.AnyTwinAlive;
			Awaken = MathHelper.Lerp(Awaken, AwakenTarget, 0.02f);
			LilyGlow = MathHelper.Lerp(LilyGlow, LilyGlowTarget, 0.03f);
			if (!TwinsFight.AnyTwinAlive) {
				AwakenTarget = 0f;
				LilyGlowTarget = 0f;
			}
			veilAmount = MathHelper.Lerp(veilAmount, Veil ? 1f : 0f, 0.03f);
			// arriving: already lifted, so the camera doesn't drift while the burst is still standing
			camLift = InDimension ? (Main.LocalPlayer.dead ? MathHelper.Lerp(camLift, 0f, 0.05f) : MathHelper.Lerp(camLift < 0.01f ? 1f : camLift, 1f, 0.05f)) : 0f;

			if (Main.dedServ)
				return;
			if (Veil && !SkyManager.Instance[DimensionSky.Key].IsActive())
				SkyManager.Instance.Activate(DimensionSky.Key);
			else if (!Veil && SkyManager.Instance[DimensionSky.Key].IsActive())
				SkyManager.Instance.Deactivate(DimensionSky.Key);
			if (Veil)
				SpawnAmbience();
		}

		private static float camLift;

		/// <summary>
		/// The camera rides a little above the player in the dimension: more sky (and sisters), less dead ground.
		/// </summary>
		public override void ModifyScreenPosition()
		{
			if (camLift < 0.001f || Main.gameMenu)
				return;
			Main.screenPosition.Y -= camLift * Main.screenHeight * 0.17f / Main.GameViewMatrix.Zoom.Y;
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
			DrawField();
		}

		// ---------------------------------------------------------------- the lily field
		private struct FieldFlower { public Vector2 Pos; public int Variant; public float Scale, Rot, Glow; public bool Flip; }
		private static readonly System.Collections.Generic.List<FieldFlower> blooms = new(600);

		/// <summary>
		/// Every lily is its own: one of eight shapes, its own size and lean, bending away when the player or a
		/// sister passes and riding gusts that roll across the field. A low mist lies over the field and the
		/// flowers' light shines up through it.
		/// </summary>
		private static void DrawField()
		{
			int lily = ModContent.TileType<GlyphLily>();
			Texture2D tex = ReTex.LilyField;
			Texture2D glow = ReTex.LilyFieldGlow;
			int fw = tex.Width / 8, fh = tex.Height;
			int x0 = Math.Max(0, (int)(Main.screenPosition.X / 16f) - 3);
			int x1 = Math.Min(Main.maxTilesX - 1, (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + 3);
			int y0 = Math.Max(0, (int)(Main.screenPosition.Y / 16f) - 3);
			int y1 = Math.Min(Main.maxTilesY - 1, (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + 5);
			float t = Main.GlobalTimeWrappedHourly;
			Player p = Main.LocalPlayer;

			// things that push the flowers: the player and the sisters
			Span<Vector2> pushers = stackalloc Vector2[3];
			Span<Vector2> pushVel = stackalloc Vector2[3];
			int nPush = 0;
			pushers[nPush] = p.Bottom; pushVel[nPush++] = p.velocity;
			foreach (NPC n in Main.ActiveNPCs) {
				if (nPush < 3 && n.ModNPC is TwinBase)
				{ pushers[nPush] = n.Bottom; pushVel[nPush++] = n.velocity; }
			}

			blooms.Clear();
			for (int i = x0; i <= x1; i++) {
				for (int j = y0; j <= y1; j++) {
					Tile tile = Main.tile[i, j];
					if (!tile.HasTile || tile.TileType != lily)
						continue;
					float h1 = ReUtils.Hash01(i, j, 71), h2 = ReUtils.Hash01(i, j, 72), h3 = ReUtils.Hash01(i, j, 73);
					// a few spots get two flowers
					int count = h3 > 0.72f ? 2 : 1;
					for (int k = 0; k < count; k++) {
						float wx = i * 16 + 8 + (k == 1 ? (h2 - 0.5f) * 14f : (h1 - 0.5f) * 6f);
						// rooted in the drawn oil surface, sunk in a little
						Vector2 world = new(wx, DimensionTerrain.SurfaceAt(wx / 16f) * 16f + 5f);
						int variant = (int)(ReUtils.Hash01(i, j, 80 + k) * 8f) % 8;
						float scale = 0.8f + 0.45f * ReUtils.Hash01(i, j, 90 + k);
						// the wind: a slow breathing sway plus gusts rolling across the field
						float sway = MathF.Sin(t * 1.3f + i * 0.45f + k) * 0.07f;
						float front = (t * 260f) % 9000f - 1500f;
						float gust = MathF.Exp(-MathF.Pow((world.X % 9000f - front) / 260f, 2f));
						sway += gust * (0.28f + 0.08f * MathF.Sin(t * 9f + i));
						// pushed aside by whoever walks or flies through
						for (int q = 0; q < nPush; q++) {
							float dx = world.X - pushers[q].X;
							float dy = world.Y - pushers[q].Y;
							if (MathF.Abs(dx) < 56f && dy > -70f && dy < 40f) {
								float near = 1f - MathF.Abs(dx) / 56f;
								sway += MathF.Sign(dx) * 0.55f * near * near;
								sway += MathHelper.Clamp(pushVel[q].X * 0.035f, -0.4f, 0.4f) * near;
							}
						}
						// flattened by something enormous passing over
						foreach (var (pp, pr, pvx, pl) in fieldPushes) {
							float dx = world.X - pp.X;
							if (MathF.Abs(dx) < pr && MathF.Abs(world.Y - pp.Y) < 120f) {
								float near = (1f - MathF.Abs(dx) / pr) * (pl / 30f);
								sway += (MathF.Sign(pvx == 0 ? dx : pvx) * 0.8f + MathF.Sign(dx) * 0.3f) * near;
							}
						}
						blooms.Add(new FieldFlower {
							Pos = world, Variant = variant, Scale = scale, Rot = MathHelper.Clamp(sway, -0.9f, 0.9f),
							Flip = ReUtils.Hash01(i, j, 95 + k) > 0.5f,
							Glow = 0.55f + 0.3f * MathF.Sin(t * 1.1f + i * 0.7f + k) + LilyGlow * 0.8f,
						});
					}
				}
			}

			SpriteBatch sb = Main.spriteBatch;
			DrawOilSurface(sb, t);
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			Vector2 origin = new(fw / 2f, fh - 2);
			foreach (FieldFlower b in blooms) {
				Vector2 pos = b.Pos - Main.screenPosition;
				Rectangle src = new(b.Variant * fw, 0, fw, fh);
				Color light = Lighting.GetColor((int)(b.Pos.X / 16f), (int)(b.Pos.Y / 16f) - 1);
				SpriteEffects fx = b.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
				sb.Draw(tex, pos, src, Color.Lerp(light, Color.White, 0.3f), b.Rot, origin, b.Scale, fx, 0f);
				sb.Draw(glow, pos, src, new Color(255, 225, 160).Additive(b.Glow), b.Rot, origin, b.Scale, fx, 0f);
			}
			sb.End();

			// a low mist over the field (alpha: it dims), drifting
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			Texture2D fog = ReTex.Fog;
			for (int n = 0; n < blooms.Count; n += 5) {
				FieldFlower b = blooms[n];
				float drift = MathF.Sin(t * 0.25f + n * 0.37f) * 30f + (t * 12f + n * 53f) % 90f - 45f;
				Vector2 pos = b.Pos - Main.screenPosition + new Vector2(drift, -10f);
				float a = 0.2f + 0.08f * MathF.Sin(t * 0.6f + n);
				sb.Draw(fog, pos, null, new Color(70, 48, 26) * a, 0f, fog.Size() / 2f, new Vector2(0.55f, 0.16f), SpriteEffects.None, 0f);
			}
			// the flowers' light, shining up through the mist
			foreach (FieldFlower b in blooms) {
				Vector2 head = b.Pos - Main.screenPosition + new Vector2(0, -36f * b.Scale).RotatedBy(b.Rot);
				FX.Bloom(sb, head, new Color(255, 196, 110), 22f * b.Scale, 0.1f * b.Glow);
			}
			for (int n = 0; n < blooms.Count; n += 9) {
				FieldFlower b = blooms[n];
				Vector2 head = b.Pos - Main.screenPosition + new Vector2(0, -30f);
				FX.Bloom(sb, head, new Color(255, 170, 80), 70f, 0.06f * b.Glow, 1.6f, 0.6f);
			}
			DrawGroundHaze(sb, t);
			sb.End();
		}

		/// <summary>
		/// The ground is not drawn as blocks: a smooth, glossy skin of black oil follows the terrain curve,
		/// with a warm sheen that slides along it and glints where it catches the light.
		/// </summary>
		private static void DrawOilSurface(SpriteBatch sb, float t)
		{
			Texture2D px = Terraria.GameContent.TextureAssets.MagicPixel.Value;
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			const int step = 4;
			float sx0 = Main.screenPosition.X;
			int width = (int)(Main.screenWidth / Main.GameViewMatrix.Zoom.X) + 2 * step + 64;
			for (int sx = -32; sx < width; sx += step) {
				float wx = sx0 + sx;
				float h = DimensionTerrain.SurfaceAt(wx / 16f) * 16f;
				float y = h - Main.screenPosition.Y;
				float slope = (DimensionTerrain.SurfaceAt((wx + 8f) / 16f) - DimensionTerrain.SurfaceAt((wx - 8f) / 16f)) * 16f / 16f;
				// the body of the oil, deep enough to hide the half-tile steps underneath
				sb.Draw(px, new Rectangle(sx, (int)y, step, 70), new Color(7, 5, 6));
				sb.Draw(px, new Rectangle(sx, (int)y + 3, step, 14), new Color(20, 14, 13) * 0.8f);
				sb.Draw(px, new Rectangle(sx, (int)y + 17, step, 18), new Color(13, 9, 9) * 0.8f);
				// the sheen: brighter where the surface faces the light, sliding slowly along it
				float lightK = MathHelper.Clamp(0.55f - slope * 2.5f, 0.15f, 1f);
				float shimmer = 0.5f + 0.5f * MathF.Sin(wx * 0.021f - t * 1.4f) * MathF.Sin(wx * 0.0063f + t * 0.35f);
				Color sheen = Color.Lerp(new Color(70, 44, 18), new Color(255, 214, 140), lightK * shimmer);
				sb.Draw(px, new Rectangle(sx, (int)y, step, 2), sheen);
				sb.Draw(px, new Rectangle(sx, (int)y + 2, step, 1), sheen * 0.35f);
				// a faint second reflection a little lower, like a lacquer coat
				if (((int)(wx / step) & 3) == 0)
					sb.Draw(px, new Rectangle(sx, (int)y + 6, step * 2, 1), new Color(255, 190, 110) * (0.12f * shimmer));
				// glints
				if (shimmer > 0.93f && ((int)(wx / step) % 7) == 0)
					FX.Flare(sb, new Vector2(sx + 2, y + 1), new Color(255, 225, 170), 10f, (shimmer - 0.93f) * 10f, 0f, 0.3f);
			}
			sb.End();
		}

		/// <summary>A light haze lying on the ground, drifting; brighter where the flowers light it from inside.</summary>
		private static void DrawGroundHaze(SpriteBatch sb, float t)
		{
			Texture2D fog = ReTex.Fog;
			float sx0 = Main.screenPosition.X;
			int width = (int)(Main.screenWidth / Main.GameViewMatrix.Zoom.X) + 400;
			for (int layer = 0; layer < 3; layer++) {
				float spacing = 150f + layer * 60f;
				float speed = 9f + layer * 6f;
				float off = (t * speed + layer * 77f) % spacing;
				float baseX = MathF.Floor(sx0 / spacing) * spacing;
				for (float x = baseX - 200f; x < sx0 + width; x += spacing) {
					float wx = x + off;
					float h = DimensionTerrain.SurfaceAt(wx / 16f) * 16f;
					Vector2 pos = new Vector2(wx, h - 14f - layer * 16f) - Main.screenPosition;
					float a = (0.13f - layer * 0.03f) * (0.75f + 0.25f * MathF.Sin(t * 0.5f + wx * 0.01f));
					Color c = Color.Lerp(new Color(130, 96, 60), new Color(170, 120, 70), LilyGlow * 0.5f);
					sb.Draw(fog, pos, null, c * a, 0f, fog.Size() / 2f, new Vector2(1.1f + layer * 0.3f, 0.22f + layer * 0.05f), SpriteEffects.None, 0f);
				}
			}
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
