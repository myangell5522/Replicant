using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.VFX;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Effects;
using Terraria.ModLoader;

namespace Replicant.Common.Dimension
{
	/// <summary>
	/// The dimension's sky: a dark amber dusk, faint stars, banks of brown haze, and the remnant of Re's
	/// glyph burst standing on the horizon like a burning tree. During the revelation the sky splits open.
	/// </summary>
	public class DimensionSky : CustomSky
	{
		public const string Key = "Replicant:Dimension";
		private bool active;
		private float intensity;
		private static readonly Vector2[] stars = new Vector2[220];
		private static readonly float[] starPhase = new float[220];

		public override void OnLoad()
		{
			var r = new Random(7);
			for (int i = 0; i < stars.Length; i++) {
				stars[i] = new Vector2((float)r.NextDouble(), (float)r.NextDouble() * 0.6f);
				starPhase[i] = (float)r.NextDouble() * 6.28f;
			}
		}

		public override void Update(GameTime gameTime)
		{
			intensity = MathHelper.Clamp(intensity + (active ? 0.02f : -0.02f), 0f, 1f);
		}

		public override Color OnTileColor(Color inColor) => Color.Lerp(inColor, new Color(150, 110, 60), 0.6f * intensity);

		public override float GetCloudAlpha() => 1f - intensity;

		public override void Draw(SpriteBatch spriteBatch, float minDepth, float maxDepth)
		{
			if (intensity <= 0.001f)
				return;
			if (!(maxDepth >= float.MaxValue && minDepth < float.MaxValue))
				return;

			int W = Main.screenWidth, H = Main.screenHeight;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float aw = DimensionSystem.Awaken;
			float t = Main.GlobalTimeWrappedHourly;

			// ---- gradient: near-black zenith → umber → burnt-gold horizon (horizon follows the camera height a little)
			float horizon = H * MathHelper.Clamp(0.72f - (Main.screenPosition.Y / 16f - PocketDimension.GroundY + 40) * 0.002f, 0.45f, 0.95f);
			Color top = Color.Lerp(new Color(8, 6, 5), new Color(14, 6, 4), aw);
			Color mid = Color.Lerp(new Color(42, 27, 12), new Color(60, 22, 10), aw);
			Color hor = Color.Lerp(new Color(128, 84, 28), new Color(170, 80, 26), aw);
			const int bands = 48;
			for (int i = 0; i < bands; i++) {
				float y0 = H * i / (float)bands;
				float k = (y0 + H / (float)bands * 0.5f) / horizon;
				Color c = k < 0.7f ? Color.Lerp(top, mid, k / 0.7f) : Color.Lerp(mid, hor, MathHelper.Clamp((k - 0.7f) / 0.3f, 0f, 1f));
				if (k > 1f)
					c = Color.Lerp(hor, new Color(30, 20, 10), MathHelper.Clamp((k - 1f) * 3f, 0f, 1f));
				spriteBatch.Draw(px, new Rectangle(0, (int)y0, W, H / bands + 2), c * intensity);
			}

			// ---- stars (faint, warm)
			Texture2D bloom = ReTex.BloomSharp;
			for (int i = 0; i < stars.Length; i++) {
				Vector2 p = new(stars[i].X * W, stars[i].Y * horizon);
				float tw = 0.4f + 0.6f * MathF.Sin(t * 1.3f + starPhase[i]);
				float a = (0.25f + 0.35f * tw) * intensity * (1f - p.Y / horizon * 0.8f);
				spriteBatch.Draw(px, new Rectangle((int)p.X, (int)p.Y, i % 9 == 0 ? 2 : 1, i % 9 == 0 ? 2 : 1), new Color(255, 230, 190) * a);
			}

			// ---- the remnant of Re's burst on the horizon: a living fountain of glyphs
			float parallaxX = -(Main.screenPosition.X / 16f - PocketDimension.CenterX) * 0.6f;
			if (!DimensionSystem.InDimension)
				parallaxX = 0f;
			Vector2 treePos = new(W * 0.5f + parallaxX, horizon + 20f);
			float treeScale = H / 640f * 0.95f;
			DrawFountain(spriteBatch, treePos, treeScale, intensity, aw, t);
			// ---- the sky splits during the revelation: a jagged golden wound across the zenith
			if (aw > 0.01f) {
				Texture2D streak = ReTex.Streak;
				Vector2 prev = new(-40, H * 0.16f);
				var r = new Random(23);
				for (int i = 1; i <= 14; i++) {
					Vector2 next = new(W * i / 13f, H * (0.14f + (float)r.NextDouble() * 0.09f));
					Vector2 d = next - prev;
					float flick = 0.75f + 0.25f * MathF.Sin(t * 6f + i);
					spriteBatch.Draw(streak, prev, null, new Color(255, 200, 110).Additive(aw * flick * intensity), d.ToRotation(), new Vector2(0, streak.Height / 2f), new Vector2(d.Length() / streak.Width * 1.15f, 0.25f), SpriteEffects.None, 0f);
					spriteBatch.Draw(streak, prev, null, Color.White.Additive(aw * flick * intensity), d.ToRotation(), new Vector2(0, streak.Height / 2f), new Vector2(d.Length() / streak.Width * 1.15f, 0.06f), SpriteEffects.None, 0f);
					FX.Bloom(spriteBatch, prev, new Color(255, 170, 80), 70f, 0.2f * aw * intensity);
					prev = next;
				}
			}

			// ---- low haze banks drifting across the horizon (alpha-blended: they dim, they don't glow)
			Texture2D fog = ReTex.Fog;
			for (int i = 0; i < 7; i++) {
				float speed = 8f + i * 4f;
				float x = ((t * speed + i * 410f - Main.screenPosition.X * 0.05f * (i + 1)) % (W + 1400f) + W + 1400f) % (W + 1400f) - 700f;
				float y = horizon - 30f + i * 18f;
				Color c = Color.Lerp(new Color(62, 42, 20), new Color(26, 18, 10), i / 7f);
				spriteBatch.Draw(fog, new Vector2(x, y), null, c * (0.55f * intensity), 0f, fog.Size() / 2f, new Vector2(3.4f, 0.9f) * (H / 1080f + 0.3f), SpriteEffects.None, 0f);
			}
		}

		// ------------------------------------------------------------------ the fountain
		private const int Rays = 38;
		private static readonly float[] rayAngle = new float[Rays], rayLen = new float[Rays], raySpeed = new float[Rays], rayPhase = new float[Rays];
		private static bool raysBuilt;

		private static void BuildRays()
		{
			var r = new Random(1337);
			for (int i = 0; i < Rays; i++) {
				float u = (i + 0.5f) / Rays * 2f - 1f;                     // -1..1 across the fan
				rayAngle[i] = u * 1.32f + (float)(r.NextDouble() - 0.5) * 0.06f;
				rayLen[i] = (0.55f + 0.45f * (1f - MathF.Abs(u)) * (0.6f + 0.4f * (float)r.NextDouble()));
				raySpeed[i] = 22f + (float)r.NextDouble() * 26f;
				rayPhase[i] = (float)r.NextDouble() * 100f;
			}
			raysBuilt = true;
		}

		/// <summary>Point on ray i at distance d (pixels): rises, then the outer rays droop like a fountain.</summary>
		private static Vector2 RayPoint(int i, Vector2 basePos, float d, float L)
		{
			float a = rayAngle[i];
			float droop = 0.55f * MathF.Abs(a) / 1.32f;
			return basePos + new Vector2(MathF.Sin(a) * d, -MathF.Cos(a) * d + droop * d * d / L);
		}

		private static void DrawFountain(SpriteBatch sb, Vector2 basePos, float scale, float intensity, float aw, float t)
		{
			if (!raysBuilt)
				BuildRays();
			float H = Main.screenHeight;
			Texture2D atlas = ReTex.GlyphAtlasGlow;
			Color tint = Color.Lerp(new Color(255, 206, 120), new Color(255, 140, 64), aw);
			float breath = 0.82f + 0.18f * MathF.Sin(t * 0.9f);
			// soft body of light behind the streams, and the faint baked remnant for density
			FX.Bloom(sb, basePos + new Vector2(0, -H * 0.22f), tint, H * 0.6f, 0.13f * intensity * (1f + aw) * breath, 1.5f, 1f);
			Texture2D tree = ReTex.GlyphTree;
			sb.Draw(tree, basePos, null, tint.Additive(0.22f * intensity * breath), 0f, new Vector2(tree.Width / 2f, tree.Height - 40f), scale, SpriteEffects.None, 0f);

			float spacing = 15f * MathF.Max(0.7f, scale);
			float gScale = 0.85f * MathF.Max(0.7f, scale);
			for (int i = 0; i < Rays; i++) {
				float L = rayLen[i] * H * 0.62f * (1f + aw * 0.15f);
				float flow = (t * raySpeed[i] * (1f + aw * 0.6f) + rayPhase[i] * spacing);
				float offset = flow % spacing;
				int step = (int)(flow / spacing);
				// a brightness wave travelling up each stream
				for (float d = offset; d < L; d += spacing) {
					float k = d / L;
					int j = (int)((d - offset) / spacing);
					Vector2 p = RayPoint(i, basePos, d, L);
					float wave = 0.45f + 0.55f * MathF.Max(0f, MathF.Sin(k * 9f - t * (2.4f + i % 5 * 0.3f) + rayPhase[i]));
					float fade = MathF.Pow(1f - k, 1.4f) * MathHelper.Clamp(k * 12f, 0f, 1f);
					// glyphs flicker and rewrite themselves as they travel
					float flick = ReUtils.Hash01(i, j - step, (int)(t * 8f)) > 0.93f ? 0.25f : 1f;
					int glyph = (int)(ReUtils.Hash01(i * 31 + j - step, (int)(t * 3f + i) / 4) * ReTex.GlyphCount);
					Color c = Color.Lerp(Color.White, tint, MathHelper.Clamp(k * 2.2f, 0f, 1f));
					Vector2 dir = RayPoint(i, basePos, d + 2f, L) - p;
					float rot = dir.ToRotation() + MathHelper.PiOver2;
					sb.Draw(atlas, p, ReTex.GlyphFrame(glyph), c.Additive(0.62f * fade * wave * flick * intensity), rot, new Vector2(7, 9), gScale * (1f - k * 0.35f), SpriteEffects.None, 0f);
				}
				// embers leaving the tip of each stream
				for (int e = 0; e < 3; e++) {
					float life = (t * 0.35f + rayPhase[i] * 0.37f + e * 0.333f) % 1f;
					Vector2 tip = RayPoint(i, basePos, L * (0.7f + 0.3f * ReUtils.Hash01(i, e)), L);
					Vector2 ep = tip + new Vector2(MathF.Sin(t + i + e) * 12f, -life * 90f * scale);
					float ea = MathF.Sin(life * MathHelper.Pi) * 0.5f * intensity;
					sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)ep.X, (int)ep.Y, 2, 2), tint.Additive(ea));
				}
			}
			// the white-hot heart at the base, breathing
			float heart = 0.75f + 0.25f * MathF.Sin(t * 2.3f) + 0.1f * MathF.Sin(t * 7.1f);
			FX.Bloom(sb, basePos, tint, 140f * scale, 0.35f * intensity * heart, 2.4f, 0.7f);
			FX.Bloom(sb, basePos, Color.White, 50f * scale + 20f, 0.7f * intensity * heart, 3f, 0.5f);
			FX.Flare(sb, basePos, tint, 160f * scale * heart, 0.45f * intensity, 0f, 0.12f);
		}

		public override void Activate(Vector2 position, params object[] args) => active = true;
		public override void Deactivate(params object[] args) => active = false;
		public override void Reset() => active = false;
		public override bool IsActive() => active || intensity > 0.001f;
	}
}
