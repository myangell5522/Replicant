using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Terraria;
using Terraria.GameContent;

namespace Replicant.Content.NPCs.ReBoss
{
	public partial class Re
	{
		// Anchor offsets in stored (2x) pixels, relative to NPC.Center — see AssetSources.
		private static readonly Vector2 BodyOrigin = new(80, 88);
		private static readonly Vector2 CrownOrigin = new(96, 120);
		private static readonly Vector2 CrownOffset = new(0, -36);
		private static readonly Vector2 SkirtOrigin = new(64, 8);
		private static readonly Vector2 SkirtOffset = new(0, 40);
		private static readonly Vector2[] EyeOffsets = { new(-15, -12), new(15, -12) };

		public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
		{
			if (tentacles == null)
				InitTentacles();
			bool dummy = NPC.IsABestiaryIconDummy;
			float time = dummy ? (float)Main.timeForVisualEffects : visualTime;

			if (State == AIState.Intro && !dummy)
				DrawSummoning(spriteBatch, NPC.Center - screenPos, Timer);
			if (Vanished)
				return false;

			float reveal = dummy ? 1f : IntroRevealAmount;
			if (reveal <= 0f)
				return false;
			float op = dummy ? 1f : NPC.Opacity;
			Vector2 jitter = tremble > 0.1f ? Main.rand.NextVector2Circular(tremble, tremble) : Vector2.Zero;
			Vector2 center = NPC.Center + jitter;
			Vector2 drawPos = center - screenPos;
			float rot = NPC.rotation;
			Color light = Color.Lerp(drawColor, Color.White, 0.35f) * op;
			float glyphPulse = 0.75f + 0.25f * MathF.Sin(time * 0.08f) + glowBoost;
			if (Unraveled)
				glyphPulse += 0.25f;
			float partsFade = ReUtils.InverseLerp(0.55f, 1f, reveal);

			// ---- 1. presence: dark smog + a faint, wide aura (soft, never a disc)
			FX.Smoke(spriteBatch, drawPos + new Vector2(0, 10), Color.Black * (0.5f * op * reveal), 150f, time * 0.004f);
			FX.Smoke(spriteBatch, drawPos + new Vector2(0, 30), Color.Black * (0.35f * op * reveal), 190f, -time * 0.003f + 1f);
			FX.Bloom(spriteBatch, drawPos, Unraveled ? ReUtils.Ember : ReUtils.DeepGold, 230f, 0.16f * op * glyphPulse * reveal);

			// ---- 2. dash telegraph
			if (telegraphLine > 0.02f && !dummy)
				DrawTelegraph(spriteBatch, center - screenPos, dashDirection, telegraphLine * op, time);

			Texture2D body = TextureAssets.Npc[Type].Value;
			Texture2D bodyGlow = ReTex.BodyGlow;
			Rectangle frame = NPC.frame;
			if (frame.Height == 0)
				frame = body.VFrame(0, TotalFrames);

			// ---- 3. afterimages while moving fast
			float speed = NPC.velocity.Length();
			if (speed > 9f && !dummy) {
				int count = NPC.oldPos.Length;
				for (int i = count - 1; i >= 1; i--) {
					if (NPC.oldPos[i] == Vector2.Zero)
						continue;
					float t = i / (float)count;
					float a = (1f - t) * ReUtils.InverseLerp(9f, 22f, speed) * op;
					Vector2 p = NPC.oldPos[i] + NPC.Size / 2f - screenPos;
					spriteBatch.Draw(body, p, frame, Color.Black * a * 0.5f, rot, BodyOrigin, 1f, SpriteEffects.None, 0f);
					spriteBatch.Draw(bodyGlow, p, frame, ReUtils.GoldRamp(1f - t).Additive(a * 0.7f), rot, BodyOrigin, 1f, SpriteEffects.None, 0f);
				}
			}

			// ---- 4. tentacles
			if (!dummy && partsFade > 0f) {
				foreach (ReTentacle t in tentacles)
					t.Draw(spriteBatch, screenPos - jitter, light, time * 0.09f, op * partsFade);
			}

			// ---- 5. skirt
			Texture2D skirt = ReTex.Skirt;
			Rectangle skirtFrame = skirt.VFrame((int)(time / 6) % 8, 8);
			Vector2 skirtPos = drawPos + SkirtOffset.RotatedBy(rot);
			spriteBatch.Draw(skirt, skirtPos, skirtFrame, light * partsFade, rot, SkirtOrigin, 1f, SpriteEffects.None, 0f);
			spriteBatch.Draw(ReTex.SkirtGlow, skirtPos, skirtFrame, ReUtils.Gold.Additive(0.7f * op * glyphPulse * partsFade), rot, SkirtOrigin, 1f, SpriteEffects.None, 0f);

			// ---- 6. crown (revealed first: it sits at the top)
			float crownFade = ReUtils.InverseLerp(0.05f, 0.35f, reveal);
			Texture2D crown = ReTex.Crown;
			Rectangle crownFrame = crown.VFrame((int)(time / 8) % 6, 6);
			Vector2 crownPos = drawPos + CrownOffset.RotatedBy(rot);
			float crownRot = rot + MathF.Sin(time * 0.03f) * 0.04f;
			spriteBatch.Draw(crown, crownPos, crownFrame, light * crownFade, crownRot, CrownOrigin, 1f, SpriteEffects.None, 0f);
			spriteBatch.Draw(ReTex.CrownGlow, crownPos, crownFrame, Color.White.Additive(0.85f * op * glyphPulse * crownFade), crownRot, CrownOrigin, 1f, SpriteEffects.None, 0f);

			// ---- 7. body + glyph glow (cropped top → bottom during the entrance)
			Rectangle src = frame;
			if (reveal < 1f)
				src.Height = Math.Max(1, (int)(frame.Height * reveal));
			spriteBatch.Draw(body, drawPos, src, light, rot, BodyOrigin, 1f, SpriteEffects.None, 0f);
			Color glyphColor = Unraveled ? Color.Lerp(ReUtils.Gold, ReUtils.Ember, 0.35f + 0.25f * MathF.Sin(time * 0.15f)) : Color.White;
			spriteBatch.Draw(bodyGlow, drawPos, src, glyphColor.Additive(MathHelper.Clamp(glyphPulse, 0f, 1.5f) * op), rot, BodyOrigin, 1f, SpriteEffects.None, 0f);
			if (reveal < 1f) {
				// the condensing edge: a hot scanline of gold
				Vector2 edge = drawPos + new Vector2(0f, -BodyOrigin.Y + src.Height).RotatedBy(rot);
				FX.Bloom(spriteBatch, edge, ReUtils.Gold, 70f, 0.8f, 1.8f, 0.12f, rot);
				FX.Bloom(spriteBatch, edge, ReUtils.PaleGold, 50f, 0.9f, 1.6f, 0.04f, rot);
			}

			// ---- 8. eyes
			float eye = MathF.Max(eyeFlare, 0.25f) * op * ReUtils.InverseLerp(0.3f, 0.5f, reveal);
			foreach (Vector2 e in EyeOffsets) {
				Vector2 ep = drawPos + e.RotatedBy(rot);
				FX.Bloom(spriteBatch, ep, ReUtils.Gold, 18f + 14f * eye, 0.45f * eye);
				if (eyeFlare > 0.3f) {
					float fr = 30f * eyeFlare * (0.85f + 0.15f * MathF.Sin(time * 0.6f));
					FX.Flare(spriteBatch, ep, ReUtils.PaleGold, fr, eyeFlare * op * 0.8f, 0f, 0.35f);
				}
			}

			// ---- 9. maw glow (kept inside the mouth)
			if (mawOpen > 0.25f) {
				Vector2 mp = drawPos + new Vector2(0, 12).RotatedBy(rot);
				float m = ReUtils.InverseLerp(0.25f, 1f, mawOpen) * op;
				FX.Bloom(spriteBatch, mp, ReUtils.Ember, 34f * m + 8f, 0.6f * m);
				FX.Bloom(spriteBatch, mp, ReUtils.PaleGold, 14f * m, 0.6f * m);
			}
			return false;
		}

		/// <summary>The entrance: a tear of light, a summoning circle, a vortex of darkness.</summary>
		private void DrawSummoning(SpriteBatch sb, Vector2 pos, float t)
		{
			float tear = ReUtils.InverseLerp(IntroTear, IntroTear + 25, t) * ReUtils.InverseLerp(IntroRoar + 10, IntroRoar, t);
			float open = ReUtils.EaseInCubic(ReUtils.InverseLerp(IntroPour, IntroRoar, t));
			float flick = 0.85f + 0.15f * MathF.Sin(t * 0.7f);

			// darkness gathering at the spawn point
			float dark = ReUtils.InverseLerp(IntroTear, IntroPour + 30, t) * ReUtils.InverseLerp(IntroRoar + 30, IntroRoar, t);
			for (int i = 0; i < 4; i++)
				FX.Smoke(sb, pos, Color.Black * (0.45f * dark), 160f + i * 60f + open * 80f, t * 0.01f * (i % 2 == 0 ? 1 : -1) + i);

			// summoning circle beneath, tilted into perspective (squashed vertically)
			float circle = ReUtils.InverseLerp(IntroPour - 10, IntroPour + 30, t) * ReUtils.InverseLerp(IntroRoar + 25, IntroRoar, t);
			if (circle > 0f) {
				float burst = t > IntroRoar ? 1f + (t - IntroRoar) * 0.03f : 1f;
				Vector2 sq = new Vector2(1f, 0.32f) * 0.62f * burst;
				Vector2 cp = pos + new Vector2(0, 150);
				sb.Draw(ReTex.SigilOuterGlow, cp, null, ReUtils.DeepGold.Additive(circle * 0.7f), 0f, ReTex.SigilOuterGlow.Size() / 2f, sq, SpriteEffects.None, 0f);
				sb.Draw(ReTex.SigilOuter, cp, null, ReUtils.Gold.Additive(circle * flick), 0f, ReTex.SigilOuter.Size() / 2f, sq, SpriteEffects.None, 0f);
				sb.Draw(ReTex.SigilInner, cp, null, ReUtils.Ember.Additive(circle * 0.8f), 0f, ReTex.SigilInner.Size() / 2f, sq * 1.12f, SpriteEffects.None, 0f);
				// pillar of light rising from the circle into the tear
				FX.Bloom(sb, cp + new Vector2(0, -150), ReUtils.DeepGold, 170f, 0.25f * circle * open, 0.35f, 1.6f);
			}

			// the vertical crack
			if (tear > 0f) {
				float h = 40f + 260f * ReUtils.EaseOutCubic(tear) + open * 60f;
				float w = 0.06f + open * 0.5f;
				FX.Bloom(sb, pos, ReUtils.Gold, h, 0.55f * tear * flick, w, 1f);
				FX.Bloom(sb, pos, ReUtils.PaleGold, h * 0.8f, 0.9f * tear, w * 0.35f, 1f);
				FX.Bloom(sb, pos, Color.White, h * 0.6f, 0.8f * tear, w * 0.12f, 1f);
				// the inside of the tear is darker than night
				if (open > 0.05f)
					FX.Smoke(sb, pos, Color.Black * (0.9f * open), 40f + open * 70f, t * 0.05f, 0.6f);
			}

			// impact afterglow
			if (t >= IntroRoar && t < IntroRoar + 40) {
				float k = (t - IntroRoar) / 40f;
				FX.Bloom(sb, pos, ReUtils.PaleGold, 300f * (0.5f + k), 0.7f * (1f - k));
				FX.Flare(sb, pos, Color.White, 420f * (0.4f + k), 0.9f * (1f - k), 0f, 0.25f);
			}
		}

		private static void DrawTelegraph(SpriteBatch sb, Vector2 start, Vector2 dir, float opacity, float time)
		{
			Texture2D glyphs = ReTex.GlyphAtlasGlow;
			const float length = 1500f;
			float rot = dir.ToRotation();
			float pulse = 0.7f + 0.3f * MathF.Sin(time * 0.5f);
			FX.Beam(sb, start, rot, length, 34f, ReUtils.DeepGold, 0.45f * opacity);
			FX.Beam(sb, start, rot, length, 6f, ReUtils.Gold, 0.8f * opacity * pulse);
			for (float d = 60f + time * 6f % 46f; d < length; d += 46f) {
				float fade = 1f - d / length;
				int g = (int)(d / 46f) * 7 + 3;
				sb.Draw(glyphs, start + dir * d, ReTex.GlyphFrame(g), ReUtils.Gold.Additive(opacity * fade * 0.8f), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
		}

		public override void BossHeadRotation(ref float rotation) => rotation = 0f;
	}
}
