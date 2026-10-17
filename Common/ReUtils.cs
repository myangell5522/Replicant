using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace Replicant.Common
{
	/// <summary>Small math / drawing helpers shared by the boss, projectiles and cutscene.</summary>
	public static class ReUtils
	{
		/// <summary>Colours the mod's palette is built around (match the generated sprites).</summary>
		public static readonly Color Gold = new(255, 196, 70);
		public static readonly Color PaleGold = new(255, 236, 168);
		public static readonly Color DeepGold = new(160, 88, 12);
		public static readonly Color Ember = new(255, 120, 30);
		public static readonly Color Blood = new(220, 40, 20);
		public static readonly Color Goo = new(10, 8, 13);
		public static readonly Color EyeBlue = new(88, 160, 232);

		/// <summary>
		/// Premultiplied-alpha trick: a colour with A = 0 drawn under BlendState.AlphaBlend adds light
		/// instead of covering, so glows never need a SpriteBatch restart.
		/// </summary>
		public static Color Additive(this Color c, float opacity = 1f) => new Color(c.R, c.G, c.B, 0) * opacity;

		public static float Saturate(float x) => Math.Clamp(x, 0f, 1f);

		public static float InverseLerp(float from, float to, float x, bool clamp = true)
		{
			float t = (x - from) / (to - from);
			return clamp ? Saturate(t) : t;
		}

		public static float EaseOutCubic(float t) { t = Saturate(t); return 1f - MathF.Pow(1f - t, 3f); }
		public static float EaseInCubic(float t) { t = Saturate(t); return t * t * t; }
		public static float EaseInOutSine(float t) { t = Saturate(t); return -(MathF.Cos(MathF.PI * t) - 1f) / 2f; }
		public static float EaseOutBack(float t) { t = Saturate(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * MathF.Pow(t - 1f, 3) + c1 * MathF.Pow(t - 1f, 2); }

		/// <summary>0 → 1 → 0 bump over [0,1].</summary>
		public static float Bump(float t) => MathF.Sin(Saturate(t) * MathF.PI);

		/// <summary>Gold palette ramp: 0 = deep amber, 0.5 = gold, 1 = near white.</summary>
		public static Color GoldRamp(float t)
		{
			t = Saturate(t);
			if (t < 0.5f)
				return Color.Lerp(DeepGold, Gold, t * 2f);
			return Color.Lerp(Gold, new Color(255, 250, 230), (t - 0.5f) * 2f);
		}

		/// <summary>Hostile projectile damage helper: vanilla multiplies hostile projectile damage
		/// by 2 (normal) / 4 (expert) / 6 (master), so divide the desired hit by that.</summary>
		public static int ProjDamage(int desiredHit)
		{
			int div = Main.masterMode ? 6 : Main.expertMode ? 4 : 2;
			float bump = Main.masterMode ? 1.7f : Main.expertMode ? 1.4f : 1f;
			return Math.Max(1, (int)(desiredHit * bump / div));
		}

		/// <summary>Source rectangle of a vertical sprite sheet.</summary>
		public static Rectangle VFrame(this Texture2D tex, int frame, int frameCount, int column = 0, int columns = 1)
		{
			int w = tex.Width / columns;
			int h = tex.Height / frameCount;
			return new Rectangle(column * w, frame * h, w, h);
		}

		public static Vector2 SafeDirectionTo(this Vector2 from, Vector2 to, Vector2 fallback = default)
		{
			Vector2 d = to - from;
			return d.LengthSquared() < 0.0001f ? (fallback == default ? Vector2.UnitY : fallback) : Vector2.Normalize(d);
		}

		/// <summary>Deterministic hash → [0,1) used for stable per-character glyph corruption.</summary>
		public static float Hash01(int a, int b = 0, int c = 0)
		{
			unchecked {
				uint h = (uint)(a * 374761393 + b * 668265263 + c * 2147483647);
				h = (h ^ (h >> 13)) * 1274126177u;
				h ^= h >> 16;
				return (h & 0xFFFFFF) / (float)0x1000000;
			}
		}
	}
}
