using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Replicant.Common.VFX
{
	/// <summary>
	/// Size-based drawing helpers for the soft glow textures. All glow textures are large and smooth and
	/// premultiplied, and are always drawn scaled DOWN, so they stay soft under Terraria's point sampler.
	/// Additive glows use the A = 0 trick (works inside any AlphaBlend SpriteBatch).
	/// </summary>
	public static class FX
	{
		/// <summary>Soft glow disc whose visible falloff reaches roughly <paramref name="radius"/> pixels.</summary>
		public static void Bloom(SpriteBatch sb, Vector2 pos, Color color, float radius, float opacity = 1f, float stretchX = 1f, float stretchY = 1f, float rotation = 0f)
		{
			if (opacity <= 0.003f || radius <= 0.5f)
				return;
			Texture2D t = ReTex.Bloom;
			float s = radius * 2f / t.Width;
			sb.Draw(t, pos, null, color.Additive(opacity), rotation, t.Size() / 2f, new Vector2(s * stretchX, s * stretchY), SpriteEffects.None, 0f);
		}

		/// <summary>4-point star flare, <paramref name="size"/> = half-length of the spikes in pixels.</summary>
		public static void Flare(SpriteBatch sb, Vector2 pos, Color color, float size, float opacity = 1f, float rotation = 0f, float squashY = 1f)
		{
			if (opacity <= 0.003f)
				return;
			Texture2D t = ReTex.Flare;
			float s = size * 2f / t.Width;
			sb.Draw(t, pos, null, color.Additive(opacity), rotation, t.Size() / 2f, new Vector2(s, s * squashY), SpriteEffects.None, 0f);
			sb.Draw(t, pos, null, Color.White.Additive(opacity * 0.7f), rotation, t.Size() / 2f, new Vector2(s, s * squashY) * 0.45f, SpriteEffects.None, 0f);
		}

		/// <summary>Shockwave ring with outer radius in pixels.</summary>
		public static void Ring(SpriteBatch sb, Vector2 pos, Color color, float radius, float opacity = 1f)
		{
			Texture2D t = ReTex.Ring;
			float s = radius * 2f / (t.Width * 0.9f);
			sb.Draw(t, pos, null, color.Additive(opacity), 0f, t.Size() / 2f, s, SpriteEffects.None, 0f);
		}

		/// <summary>A beam/line starting at <paramref name="start"/>, drawn with a horizontally tileable texture.</summary>
		public static void Beam(SpriteBatch sb, Vector2 start, float rotation, float length, float thickness, Color color, float opacity = 1f, Texture2D tex = null)
		{
			tex ??= ReTex.Trail;
			sb.Draw(tex, start, null, color.Additive(opacity), rotation, new Vector2(0f, tex.Height / 2f), new Vector2(length / tex.Width, thickness / tex.Height), SpriteEffects.None, 0f);
		}

		/// <summary>Alpha-blended soft smoke puff (dark presence, fog), radius in pixels.</summary>
		public static void Smoke(SpriteBatch sb, Vector2 pos, Color color, float radius, float rotation = 0f, float stretchX = 1f)
		{
			Texture2D t = ReTex.Smoke;
			float s = radius * 2f / t.Width;
			sb.Draw(t, pos, null, color, rotation, t.Size() / 2f, new Vector2(s * stretchX, s), SpriteEffects.None, 0f);
		}
	}
}
