using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Terraria;

namespace Replicant.Content.NPCs.ReBoss
{
	/// <summary>
	/// One of Re's tendrils: a follow-the-leader chain that is pulled toward a swaying rest pose,
	/// so it trails behind dashes and flares outward during attacks. Purely visual (client side).
	/// </summary>
	public class ReTentacle
	{
		public readonly Vector2[] Points;
		public readonly Vector2 Anchor;        // offset from NPC centre (stored pixels)
		public readonly float BaseAngle;       // rest direction
		public readonly float SegmentLength;
		public readonly float Phase;
		public readonly float Thickness;
		public float Flare;                    // 0..1: spread outward (attacks)
		public float Curl;                     // extra curl amount (idle variety)

		public ReTentacle(Vector2 anchor, float baseAngle, int segments, float segmentLength, float phase, float thickness)
		{
			Anchor = anchor;
			BaseAngle = baseAngle;
			SegmentLength = segmentLength;
			Phase = phase;
			Thickness = thickness;
			Points = new Vector2[segments + 1];
		}

		public void Reset(Vector2 center)
		{
			Vector2 dir = BaseAngle.ToRotationVector2();
			for (int i = 0; i < Points.Length; i++)
				Points[i] = center + Anchor + dir * SegmentLength * i;
		}

		public void Update(Vector2 center, float time, float facing)
		{
			Vector2 root = center + new Vector2(Anchor.X, Anchor.Y);
			Points[0] = root;
			float side = MathF.Sign(Anchor.X == 0 ? 1 : Anchor.X);
			for (int i = 1; i < Points.Length; i++) {
				float t = i / (float)(Points.Length - 1);
				// Rest pose: base angle, flared outward, with a travelling wave that grows toward the tip.
				float wave = MathF.Sin(time * 0.06f + Phase - t * 3.2f) * (0.25f + 0.55f * t);
				float angle = BaseAngle + side * (Flare * 0.55f * t) + wave * (1f - Flare * 0.5f) + side * Curl * t * t;
				Vector2 desired = Points[i - 1] + angle.ToRotationVector2() * SegmentLength;
				// Follow (keeps momentum so dashes drag the tendrils) blended toward the pose.
				Vector2 follow = Points[i - 1] + Points[i - 1].SafeDirectionTo(Points[i], angle.ToRotationVector2()) * SegmentLength;
				float stiffness = MathHelper.Lerp(0.32f, 0.12f, t) + Flare * 0.15f;
				Points[i] = Vector2.Lerp(follow, desired, stiffness);
				// hard constraint
				Points[i] = Points[i - 1] + Points[i - 1].SafeDirectionTo(Points[i]) * SegmentLength;
			}
		}

		/// <summary>Draw dark segments that crossfade to translucent gold toward the tip (like the reference shades).</summary>
		public void Draw(SpriteBatch sb, Vector2 screenPos, Color light, float glowPulse, float opacity)
		{
			Texture2D tex = ReTex.Tentacle;
			Texture2D glow = ReTex.TentacleGlow;
			int n = Points.Length;
			// draw from tip to root so the root overlaps
			for (int i = n - 1; i >= 1; i--) {
				float t = (i - 1) / (float)(n - 2);
				Vector2 a = Points[i - 1], b = Points[i];
				Vector2 mid = (a + b) * 0.5f;
				float rot = (b - a).ToRotation() - MathHelper.PiOver2;
				bool tip = i == n - 1;
				float scale = MathHelper.Lerp(1.15f, 0.55f, t) * Thickness;
				float gold = ReUtils.InverseLerp(0.4f, 0.95f, t);
				Rectangle darkSrc = tex.VFrame(tip ? 6 : i % 3, 8);
				Rectangle goldSrc = tex.VFrame(tip ? 7 : 3 + i % 3, 8);
				Vector2 origin = new(darkSrc.Width / 2f, tip ? 10f : darkSrc.Height / 2f);
				Vector2 pos = (tip ? a : mid) - screenPos;
				// segments are 48 px tall in the sheet; stretch slightly to close gaps
				Vector2 sc = new(scale, scale * MathHelper.Clamp(SegmentLength / 30f, 0.6f, 1.4f));
				if (tip)
					sc.Y = scale;

				Color darkCol = light * (1f - gold) * opacity;
				Color goldLit = Color.Lerp(light, Color.White, 0.6f) * gold * opacity;
				sb.Draw(tex, pos, darkSrc, darkCol, rot, origin, sc, SpriteEffects.None, 0f);
				sb.Draw(tex, pos, goldSrc, goldLit, rot, origin, sc, SpriteEffects.None, 0f);

				float pulse = 0.55f + 0.45f * MathF.Sin(glowPulse - t * 5f);
				sb.Draw(glow, pos, gold > 0.5f ? goldSrc : darkSrc, ReUtils.Gold.Additive(pulse * opacity * (0.6f + gold * 0.6f)), rot, origin, sc, SpriteEffects.None, 0f);
				if (tip) {
					Vector2 tipPos = b - screenPos;
					Common.VFX.FX.Bloom(sb, tipPos, ReUtils.Gold, 24f * Thickness, 0.28f * opacity * pulse);
				}
			}
		}
	}
}
