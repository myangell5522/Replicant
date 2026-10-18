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

		// ---- the Aria's rift is the sky's own wound: SkyRift publishes where (screen Y) and how open, each frame
		public static float RiftOpen, RiftWide, RiftScreenY;
		private static int riftHold;

		public static void HoldRift(float open, float wide, float screenY)
		{
			RiftOpen = open;
			RiftWide = wide;
			RiftScreenY = screenY;
			riftHold = 3;
		}

		// ---- the eyes in the sky (the devour): held each tick by a cutscene, they fade when let go
		public static float Eyes, Dark;
		private static float eyesTarget, darkTarget;
		private static int eyesHold, eyesRow, eyesLevel;

		/// <param name="row">0 closed, 1 opening, 2 open.</param>
		/// <param name="level">0 calm, 1 stirring, 2 furious.</param>
		public static void HoldEyes(float open, float dark, int row, int level)
		{
			eyesTarget = open;
			darkTarget = dark;
			eyesRow = row;
			eyesLevel = level;
			eyesHold = 3;
		}

		public override void Update(GameTime gameTime)
		{
			intensity = MathHelper.Clamp(intensity + (active ? 0.02f : -0.02f), 0f, 1f);
			if (riftHold > 0)
				riftHold--;
			else {
				RiftOpen = MathHelper.Lerp(RiftOpen, 0f, 0.08f);
				RiftWide = MathHelper.Lerp(RiftWide, 0f, 0.08f);
			}
			if (eyesHold > 0)
				eyesHold--;
			else
				eyesTarget = darkTarget = 0f;
			Eyes = MathHelper.Lerp(Eyes, eyesTarget, 0.05f);
			Dark = MathHelper.Lerp(Dark, darkTarget, 0.04f);
		}

		public override Color OnTileColor(Color inColor) => Color.Lerp(Color.Lerp(inColor, new Color(150, 110, 60), 0.6f * intensity), new Color(40, 30, 34), 0.7f * Dark);

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
			// in the dimension the horizon sits on the real ground (so the far field and the fountain rise from it);
			// elsewhere it follows the camera loosely
			float horizon = DimensionSystem.InDimension
				? MathF.Max(H * 0.28f, GroundScreenY(W / 2f) - 26f)
				: H * MathHelper.Clamp(0.72f - (Main.screenPosition.Y / 16f - DimensionTerrain.Ground + 40) * 0.002f, 0.45f, 0.95f);
			Color top = Color.Lerp(new Color(24, 15, 9), new Color(30, 10, 6), aw);
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

			// ---- something is darkening the sky
			if (Dark > 0.01f)
				spriteBatch.Draw(px, new Rectangle(0, 0, W, H), new Color(3, 2, 6) * (0.78f * Dark * intensity));

			// ---- stars (faint, warm)
			Texture2D bloom = ReTex.BloomSharp;
			for (int i = 0; i < stars.Length; i++) {
				Vector2 p = new(stars[i].X * W, stars[i].Y * horizon);
				float tw = 0.4f + 0.6f * MathF.Sin(t * 1.3f + starPhase[i]);
				float a = (0.25f + 0.35f * tw) * intensity * (1f - p.Y / horizon * 0.8f);
				spriteBatch.Draw(px, new Rectangle((int)p.X, (int)p.Y, i % 9 == 0 ? 2 : 1, i % 9 == 0 ? 2 : 1), new Color(255, 230, 190) * a);
			}

			// ---- high above: slow veils of light and drifting constellations of script, so the zenith is never a dead black
			if (DimensionSystem.InDimension)
				DrawZenith(spriteBatch, W, horizon, intensity, aw, t);

			// ---- the remnant of Re's burst on the horizon: a living fountain of glyphs
			// periodic with the loop, so the horizon never jumps when the field wraps around
			float parallaxX = MathF.Sin(DimensionTerrain.LoopX(Main.screenPosition.X / 16f) / DimensionTerrain.LoopWidth * MathHelper.TwoPi) * 140f;
			if (!DimensionSystem.InDimension)
				parallaxX = 0f;
			Vector2 treePos = new(W * 0.5f + parallaxX, horizon + 20f);
			float treeScale = H / 640f * 0.95f;
			DrawFountain(spriteBatch, treePos, treeScale, intensity, aw, t);
			// ---- the sky splits during the revelation: a torn wound across the zenith, black inside, burning at the lips
			// (during the Aria this same wound is the rift: it sinks to where the attack is poured from and widens)
			float woundA = MathF.Max(aw, RiftOpen);
			if (woundA > 0.01f)
				DrawSkyWound(spriteBatch, W, H, woundA * intensity, t);

			// ---- the eyes, enormous and far away, looking down through the sky
			if (Eyes > 0.01f)
				DrawSkyEyes(spriteBatch, W, H, Eyes * intensity, t);

			// ---- low haze banks drifting across the horizon (alpha-blended: they dim, they don't glow)
			Texture2D fog = ReTex.Fog;
			for (int i = 0; i < 7; i++) {
				float speed = 8f + i * 4f;
				float x = ((t * speed + i * 410f - CamPhase * (W + 1400f) * (i % 3 + 1)) % (W + 1400f) + W + 1400f) % (W + 1400f) - 700f;
				float y = horizon - 30f + i * 18f;
				Color c = Color.Lerp(new Color(62, 42, 20), new Color(26, 18, 10), i / 7f);
				spriteBatch.Draw(fog, new Vector2(x, y), null, c * (0.55f * intensity), 0f, fog.Size() / 2f, new Vector2(3.4f, 0.9f) * (H / 1080f + 0.3f), SpriteEffects.None, 0f);
			}

			// ---- the field goes on forever: rows of distant lilies, dimmer and smaller with distance
			if (DimensionSystem.InDimension)
				DrawDistantField(spriteBatch, horizon, intensity, t);
		}

		private static void DrawSkyEyes(SpriteBatch sb, int W, int H, float a, float t)
		{
			Texture2D eyes = ModContent.Request<Texture2D>("Replicant/Assets/Textures/Sky/SkyEyes", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			Texture2D glow = ModContent.Request<Texture2D>("Replicant/Assets/Textures/Sky/SkyEyes_Glow", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			int fw = eyes.Width / 3, fh = eyes.Height / 3;
			Rectangle src = new(eyesLevel * fw, eyesRow * fh, fw, fh);
			float s = H / 1080f * 1.75f;
			// a slow parallax with the camera, and a breath
			Vector2 c = new(W * 0.5f - CamPhaseSin * 60f, H * 0.24f + MathF.Sin(t * 0.5f) * 6f);
			// the sky around them parts first: a dark lens
			FX.Smoke(sb, c, new Color(2, 1, 4) * (0.9f * a), fw * s * 0.75f, 0f, 1.9f);
			FX.Bloom(sb, c, ReUtils.EyeBlue, fw * s * 0.6f, 0.14f * a, 2.2f, 0.5f);
			float tremble = eyesLevel == 2 ? Main.rand.NextFloat(-3f, 3f) : 0f;
			sb.Draw(eyes, c + new Vector2(tremble, 0f), src, new Color(200, 195, 205) * a, 0f, new Vector2(fw / 2f, fh / 2f), s, SpriteEffects.None, 0f);
			sb.Draw(glow, c + new Vector2(tremble, 0f), src, Color.White.Additive(0.8f * a), 0f, new Vector2(fw / 2f, fh / 2f), s, SpriteEffects.None, 0f);
			// faint ember drift falling out of them
			for (int i = 0; i < 18; i++) {
				float h1 = ReUtils.Hash01(i, 811), h2 = ReUtils.Hash01(i, 812);
				float y = (t * (20f + h2 * 30f) + h1 * 400f) % 400f;
				Vector2 p = c + new Vector2((h1 - 0.5f) * fw * s * 0.9f, fh * s * 0.3f + y);
				sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(i * 3), ReUtils.EyeBlue.Additive(0.4f * a * (1f - y / 400f)), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
		}

		private static float CamPhaseSin => MathF.Sin(CamPhase * MathHelper.TwoPi);

		private static void DrawZenith(SpriteBatch sb, int W, float horizon, float intensity, float aw, float t)
		{
			Texture2D fog = ReTex.Fog;
			float camY = Main.screenPosition.Y / 16f;
			// the higher the camera, the more of this layer shows
			float high = MathHelper.Clamp((DimensionTerrain.Ground - 25 - camY) / 60f, 0.25f, 1f);
			for (int i = 0; i < 5; i++) {
				float x = ((t * (5f + i * 2f) + i * 530f - CamPhase * (W + 1600f) * (i % 2 + 1)) % (W + 1600f) + W + 1600f) % (W + 1600f) - 800f;
				float y = horizon * (0.12f + i * 0.13f);
				Color c = Color.Lerp(new Color(120, 80, 36), new Color(150, 60, 30), aw);
				sb.Draw(fog, new Vector2(x, y), null, c.Additive(0.07f * intensity * high), 0f, fog.Size() / 2f, new Vector2(5f, 1.1f) * (Main.screenHeight / 1080f + 0.3f), SpriteEffects.None, 0f);
			}
			// constellations: little clusters of glyphs that breathe in and out of light
			for (int i = 0; i < 18; i++) {
				float hx = ReUtils.Hash01(i, 401), hy = ReUtils.Hash01(i, 402);
				float x = ((hx * (W + 400f) - CamPhase * (W + 400f)) % (W + 400f) + W + 400f) % (W + 400f) - 200f;
				float y = hy * horizon * 0.65f + 20f;
				float breath = MathF.Max(0f, MathF.Sin(t * (0.4f + hx * 0.5f) + i * 2.1f));
				for (int g = 0; g < 3; g++) {
					Vector2 p = new Vector2(x, y) + new Vector2(ReUtils.Hash01(i, g, 403) - 0.5f, ReUtils.Hash01(i, g, 404) - 0.5f) * 46f;
					sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(i * 5 + g), ReUtils.Gold.Additive(0.32f * breath * intensity * high), 0f, new Vector2(7, 9), 0.9f, SpriteEffects.None, 0f);
				}
				if (breath > 0.6f)
					FX.Bloom(sb, new Vector2(x, y), ReUtils.Gold, 50f, 0.06f * breath * intensity * high);
			}
		}

		/// <summary>
		/// The wound in the sky: a jagged tear that opens from the middle outward. Its edges quiver, its lips burn
		/// gold, script drips out of it, hairline cracks branch away, and light breathes from inside.
		/// </summary>
		private static void DrawSkyWound(SpriteBatch sb, int W, int H, float a, float t)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			Texture2D th = ReTex.Thread;
			float open = ReUtils.EaseOutCubic(a);
			float riftK = ReUtils.EaseInOutSine(MathHelper.Clamp(RiftOpen * 1.5f, 0f, 1f));
			float baseY = MathHelper.Lerp(H * 0.17f, MathHelper.Clamp(RiftScreenY, H * 0.06f, H * 0.4f), riftK);
			float widen = 1f + RiftOpen * 0.7f + RiftWide * 2.6f;
			const int col = 5;
			float reach = open;                                // grows outward from the centre
			for (int x = 0; x < W; x += col) {
				float u = x / (float)W;
				float fromMid = MathF.Abs(u - 0.5f) * 2f;
				if (fromMid > reach)
					continue;
				// the tear's path: two octaves of jag, slowly shifting
				float path = MathF.Sin(u * 9f + 1f) * 22f + MathF.Sin(u * 31f + t * 0.3f) * 7f + (ReUtils.Hash01(x / 15, 3) - 0.5f) * 10f;
				float env = MathF.Pow(1f - fromMid, 0.55f);
				float jag = 0.55f + 0.45f * ReUtils.Hash01(x / col, 7) + 0.15f * MathF.Sin(t * 4f + x * 0.11f);
				float half = (2f + 26f * env * jag) * open * (H / 1080f + 0.2f) * widen;
				float y = baseY + path;
				sb.Draw(px, new Rectangle(x, (int)(y - half), col, (int)(half * 2f + 1)), new Color(5, 2, 2) * a);
				Color lip = Color.Lerp(new Color(255, 170, 70), Color.White, 0.35f + 0.35f * MathF.Sin(t * 5f + x * 0.07f));
				sb.Draw(px, new Rectangle(x, (int)(y - half - 2), col, 3), lip.Additive(0.95f * a));
				sb.Draw(px, new Rectangle(x, (int)(y + half - 1), col, 3), lip.Additive(0.95f * a));
				// script falling out of the wound
				if (x % 30 == 0) {
					float fall = (t * 40f + ReUtils.Hash01(x, 9) * 300f) % 160f;
					sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x, y + half + fall), ReTex.GlyphFrame(x / 30 + (int)(t * 2f)), new Color(255, 196, 90).Additive(a * (1f - fall / 160f) * 0.8f), 0f, new Vector2(7, 9), 0.9f, SpriteEffects.None, 0f);
				}
			}
			// light breathing out of it
			FX.Bloom(sb, new Vector2(W / 2f, baseY), new Color(255, 160, 70), W * 0.55f * open, (0.22f + RiftOpen * 0.15f + RiftWide * 0.3f) * a * (0.85f + 0.15f * MathF.Sin(t * 1.7f)), 1f, 0.16f + RiftWide * 0.2f);
			// hairline cracks branching across the sky
			for (int k = 0; k < 22; k++) {
				float u = ReUtils.Hash01(k, 31);
				if (MathF.Abs(u - 0.5f) * 2f > reach)
					continue;
				float x = u * W;
				Vector2 p = new(x, baseY + MathF.Sin(u * 9f + 1f) * 22f);
				float ang = (ReUtils.Hash01(k, 32) < 0.5f ? -1f : 1f) * MathHelper.PiOver2 + (ReUtils.Hash01(k, 33) - 0.5f) * 1.8f;
				float len = (40f + ReUtils.Hash01(k, 34) * 160f) * open * (widen * 0.8f + 0.2f);
				for (int s = 0; s < 4; s++) {
					ang += (ReUtils.Hash01(k, 40 + s) - 0.5f) * 0.9f;
					Vector2 q = p + ang.ToRotationVector2() * len / 4f;
					sb.Draw(th, p, null, new Color(255, 214, 150).Additive(0.55f * a * (1f - s / 4f)), (q - p).ToRotation(), new Vector2(0, th.Height / 2f), new Vector2((q - p).Length() / th.Width, 1.5f / th.Height), SpriteEffects.None, 0f);
					p = q;
				}
			}
		}

		/// <summary>0..1 position of the camera around the loop (parallax that never jumps at the seam).</summary>
		private static float CamPhase => DimensionTerrain.LoopX(Main.screenPosition.X / 16f) / DimensionTerrain.LoopWidth;

		/// <summary>Screen Y of the real ground surface under a screen X (follows hills, zoom and the loop).</summary>
		private static float GroundScreenY(float sx)
		{
			Matrix m = Main.GameViewMatrix.TransformationMatrix;
			float wx = Main.screenPosition.X + (sx - m.M41) / m.M11;
			float wy = DimensionTerrain.SurfaceAt(wx / 16f) * 16f;
			return (wy - Main.screenPosition.Y) * m.M22 + m.M42;
		}

		/// <summary>
		/// The field goes on forever: rows of distant lilies rising just behind the real ground line, smaller and
		/// dimmer with distance. Anchored to the actual surface under each column, so they never float in the
		/// sky when you fly, and never sink beneath the blocks when you land.
		/// </summary>
		private static void DrawDistantField(SpriteBatch sb, float horizon, float intensity, float t)
		{
			int W = Main.screenWidth, H = Main.screenHeight;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float zoom = Main.GameViewMatrix.Zoom.X;
			float Q = W + 240f;
			// ground line per column (cached for this frame)
			const int Step = 8;
			int cols = W / Step + 2;
			if (groundCache.Length < cols)
				groundCache = new float[cols];
			bool any = false;
			for (int c = 0; c < cols; c++) {
				groundCache[c] = GroundScreenY(c * Step);
				if (groundCache[c] > -60f && groundCache[c] < H + 60f)
					any = true;
			}
			if (!any)
				return;
			float GroundAt(float x) => groundCache[Math.Clamp((int)(x / Step), 0, cols - 1)];

			// a soft dark rise of the far field behind the real one
			for (int c = 0; c < cols; c++) {
				float gy = groundCache[c];
				int top = (int)(gy - 54f * zoom);
				sb.Draw(px, new Rectangle(c * Step, top, Step, (int)(gy - top) + 4), new Color(12, 8, 6) * (0.45f * intensity));
			}
			for (int layer = 0; layer < 4; layer++) {
				float depth = layer / 3f;                         // 0 far .. 1 near
				int count = 150 - layer * 25;
				float scroll = CamPhase * Q * (layer + 1);
				float lift = (46f - layer * 11f) * zoom;            // far rows stand higher above the line
				float size = (1f + depth * 2.2f) * zoom;
				float glow = (0.22f + depth * 0.3f) * intensity * (0.8f + DimensionSystem.LilyGlow * 0.4f);
				for (int j = 0; j < count; j++) {
					float h1 = ReUtils.Hash01(j, layer, 3), h2 = ReUtils.Hash01(j, layer, 4);
					float x = ((j + h1 * 0.8f) * Q / count - scroll) % Q;
					if (x < 0)
						x += Q;
					x -= 120f;
					if (x < -10f || x > W + 10f)
						continue;
					float sway = MathF.Sin(t * 1.2f + j * 0.7f + layer) * size * 0.6f;
					float stem = (4f + h2 * 6f) * size;
					float y = GroundAt(x) - lift + MathF.Sin(j * 0.9f + layer * 2f) * 3f * size;
					if (y < -20f || y > H + 20f)
						continue;
					sb.Draw(px, new Rectangle((int)x, (int)(y - stem), Math.Max(1, (int)(size * 0.6f)), (int)stem), new Color(14, 12, 9) * intensity);
					Vector2 head = new(x + sway, y - stem);
					Color petal = Color.Lerp(new Color(150, 120, 90), new Color(250, 236, 210), depth * 0.6f + h1 * 0.3f);
					sb.Draw(px, new Rectangle((int)(head.X - size), (int)(head.Y - size), (int)(size * 2f + 1), (int)(size * 1.6f + 1)), petal * (0.5f + depth * 0.5f) * intensity);
					if (j % 2 == 0)
						FX.Bloom(sb, head, new Color(255, 196, 110), 6f + size * 5f, glow * (0.6f + 0.4f * MathF.Sin(t * 1.1f + j)));
				}
			}
		}

		private static float[] groundCache = new float[0];

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
