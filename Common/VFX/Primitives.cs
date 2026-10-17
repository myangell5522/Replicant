using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Replicant.Common.VFX
{
	/// <summary>
	/// Textured triangle-strip renderer (no custom shaders needed — uses BasicEffect),
	/// used for dash trails, lance streaks and the cutscene's glyph streams.
	/// </summary>
	public static class Primitives
	{
		private static BasicEffect effect;

		/// <summary>Pure additive for premultiplied textures (BlendState.Additive would square the alpha).</summary>
		public static readonly BlendState AddPremultiplied = new() {
			ColorSourceBlend = Blend.One,
			ColorDestinationBlend = Blend.One,
			AlphaSourceBlend = Blend.One,
			AlphaDestinationBlend = Blend.One,
		};
		private static VertexPositionColorTexture[] buffer = new VertexPositionColorTexture[1024];

		public delegate float WidthFunc(float t);
		public delegate Color ColorFunc(float t);

		private static BasicEffect Effect {
			get {
				if (effect == null || effect.IsDisposed) {
					effect = new BasicEffect(Main.instance.GraphicsDevice) {
						TextureEnabled = true,
						VertexColorEnabled = true,
						LightingEnabled = false,
					};
				}
				return effect;
			}
		}

		/// <summary>
		/// Draws a strip along <paramref name="points"/>.
		/// </summary>
		/// <param name="points">Positions in world space (worldSpace = true) or screen pixels.</param>
		/// <param name="uStart">Texture U at the first point (scroll by animating this).</param>
		/// <param name="uRepeat">How many times the texture repeats along the whole strip.</param>
		/// <param name="worldSpace">World strips use the game zoom matrix; screen strips use identity.</param>
		/// <remarks>SpriteBatch must NOT be active when calling this.</remarks>
		public static void DrawStrip(IList<Vector2> points, Texture2D texture, WidthFunc width, ColorFunc color,
			float uStart = 0f, float uRepeat = 1f, bool worldSpace = true, BlendState blend = null)
		{
			int n = points.Count;
			if (n < 2)
				return;
			if (buffer.Length < n * 2)
				Array.Resize(ref buffer, n * 2 + 64);

			Vector2 offset = worldSpace ? Main.screenPosition : Vector2.Zero;
			for (int i = 0; i < n; i++) {
				float t = i / (float)(n - 1);
				Vector2 dir;
				if (i == 0)
					dir = points[1] - points[0];
				else if (i == n - 1)
					dir = points[n - 1] - points[n - 2];
				else
					dir = points[i + 1] - points[i - 1];
				if (dir.LengthSquared() < 0.0001f)
					dir = Vector2.UnitX;
				dir.Normalize();
				Vector2 normal = new(-dir.Y, dir.X);
				float w = width(t) * 0.5f;
				Color c = color(t);
				Vector2 p = points[i] - offset;
				float u = uStart + t * uRepeat;
				buffer[i * 2] = new VertexPositionColorTexture(new Vector3(p + normal * w, 0f), c, new Vector2(u, 0f));
				buffer[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(p - normal * w, 0f), c, new Vector2(u, 1f));
			}

			GraphicsDevice gd = Main.instance.GraphicsDevice;
			BasicEffect fx = Effect;
			fx.Texture = texture;
			fx.World = Matrix.Identity;
			fx.View = worldSpace ? Main.GameViewMatrix.TransformationMatrix : Matrix.Identity;
			fx.Projection = Matrix.CreateOrthographicOffCenter(0, Main.screenWidth, Main.screenHeight, 0, -1f, 1f);

			gd.BlendState = blend ?? AddPremultiplied;
			gd.SamplerStates[0] = SamplerState.LinearWrap;
			gd.RasterizerState = RasterizerState.CullNone;
			gd.DepthStencilState = DepthStencilState.None;
			foreach (EffectPass pass in fx.CurrentTechnique.Passes) {
				pass.Apply();
				gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, buffer, 0, n * 2 - 2);
			}
		}

		/// <summary>Restart the main SpriteBatch around a primitive call inside world drawing (NPC/projectile PreDraw).</summary>
		public static void WithWorldBatchPaused(SpriteBatch sb, Action draw)
		{
			sb.End();
			draw();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
		}
	}
}
