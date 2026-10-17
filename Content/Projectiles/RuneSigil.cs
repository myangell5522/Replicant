using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles
{
	/// <summary>
	/// A sigil drawn around the player. After ai[0] ticks, ai[1] glyph bolts erupt from its rim and
	/// converge on the centre (ai[2] = angular offset). Deals no damage itself.
	/// </summary>
	public class RuneSigil : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/Projectiles/SigilOuter";
		public const float Radius = 340f;

		private ref float Delay => ref Projectile.ai[0];
		private ref float Bolts => ref Projectile.ai[1];
		private ref float Offset => ref Projectile.ai[2];
		private ref float Timer => ref Projectile.localAI[0];

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = false;
			Projectile.friendly = false;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 400;
		}

		public override bool? CanDamage() => false;

		public override void AI()
		{
			if (Timer == 0)
				Projectile.timeLeft = (int)Delay + 45;
			Timer++;
			Projectile.velocity = Vector2.Zero;
			int n = Math.Max(3, (int)Bolts);

			// telegraph: sparks gather at the bolt spawn points
			if (!Main.dedServ && Timer > Delay - 30 && Timer < Delay && Timer % 3 == 0) {
				for (int i = 0; i < n; i++) {
					Vector2 p = Projectile.Center + (Offset + MathHelper.TwoPi * i / n).ToRotationVector2() * Radius;
					ParticleSystem.Bloom(p, Vector2.Zero, ReUtils.Ember, 0.25f, 8);
				}
			}

			if (Timer >= Delay && Projectile.localAI[1] == 0f) {
				Projectile.localAI[1] = 1f;
				if (Main.netMode != NetmodeID.MultiplayerClient) {
					for (int i = 0; i < n; i++) {
						float a = Offset + MathHelper.TwoPi * i / n;
						Vector2 p = Projectile.Center + a.ToRotationVector2() * Radius;
						Vector2 v = -a.ToRotationVector2() * 1.2f;
						Projectile.NewProjectile(Projectile.GetSource_FromThis(), p, v, ModContent.ProjectileType<GlyphBolt>(), Projectile.damage, 0f, Main.myPlayer, GlyphBolt.ModeAccelerate, 1.06f);
					}
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(ReSounds.GlyphShoot with { Pitch = -0.2f }, Projectile.Center);
					ParticleSystem.Ring(Projectile.Center, ReUtils.Gold, Radius * 2f / 256f * 1.02f, 0.97f, 22);
				}
			}
		}

		private static readonly List<Vector2> ringPts = new(100);

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float appear = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 26, Timer));
			float vanish = ReUtils.InverseLerp(Delay + 45, Delay + 12, Timer);
			float charge = ReUtils.InverseLerp(10, Delay, Timer);
			float release = ReUtils.InverseLerp(Delay + 20, Delay, Timer) * (Timer >= Delay ? 1f : 0f);
			float a = vanish * MathHelper.Clamp(appear, 0f, 1f);
			float scale = MathHelper.Max(appear, 0.01f);
			float spinOuter = Timer * 0.006f + Offset;
			float spinInner = -Timer * 0.013f - Offset;
			float flicker = 0.9f + 0.1f * MathF.Sin(Timer * 0.9f);
			Color dim = ReUtils.DeepGold;
			Color hot = Color.Lerp(ReUtils.Gold, ReUtils.PaleGold, charge * charge);

			// 1. darkened ground: the sigil eats the light around it
			for (int i = 0; i < 3; i++)
				FX.Smoke(sb, pos, Color.Black * (0.32f * a), Radius * 1.15f * scale, Timer * 0.004f * (i + 1) + i * 2f);

			// 2. flowing energy rings (textured primitives scrolling around the circle)
			Primitives.WithWorldBatchPaused(sb, () => {
				DrawEnergyRing(Projectile.Center, Radius * 0.985f * scale, 30f, Timer * 0.012f, Color.Lerp(dim, hot, charge) * (0.55f * a));
				DrawEnergyRing(Projectile.Center, Radius * 0.7f * scale, 18f, -Timer * 0.02f, Color.Lerp(dim, ReUtils.Ember, charge) * (0.45f * a * (0.4f + 0.6f * charge)));
			});

			// 3. soft under-glow of each layer
			float glowA = a * (0.35f + 0.65f * charge) * flicker;
			sb.Draw(ReTex.SigilOuterGlow, pos, null, dim.Additive(glowA * 0.8f), spinOuter, ReTex.SigilOuterGlow.Size() / 2f, scale, SpriteEffects.None, 0f);
			sb.Draw(ReTex.SigilInnerGlow, pos, null, Color.Lerp(dim, ReUtils.Ember, charge).Additive(glowA * 0.7f), spinInner, ReTex.SigilInnerGlow.Size() / 2f, scale, SpriteEffects.None, 0f);
			sb.Draw(ReTex.SigilCoreGlow, pos, null, ReUtils.Ember.Additive(glowA), 0f, ReTex.SigilCoreGlow.Size() / 2f, scale, SpriteEffects.None, 0f);

			// 4. crisp line work
			Color lineCol = Color.Lerp(dim, hot, 0.35f + 0.65f * charge);
			sb.Draw(ReTex.SigilOuter, pos, null, lineCol.Additive(a), spinOuter, ReTex.SigilOuter.Size() / 2f, scale, SpriteEffects.None, 0f);
			sb.Draw(ReTex.SigilInner, pos, null, lineCol.Additive(a * 0.9f), spinInner, ReTex.SigilInner.Size() / 2f, scale, SpriteEffects.None, 0f);
			float eye = 0.6f + 0.4f * MathF.Sin(Timer * 0.15f);
			sb.Draw(ReTex.SigilCore, pos, null, Color.Lerp(ReUtils.Gold, ReUtils.Blood, charge).Additive(a * eye), 0f, ReTex.SigilCore.Size() / 2f, scale, SpriteEffects.None, 0f);

			// 5. ring of glyphs that ignite one by one as the sigil charges
			Texture2D atlas = ReTex.GlyphAtlasGlow;
			const int glyphs = 28;
			for (int i = 0; i < glyphs; i++) {
				float ang = spinOuter + MathHelper.TwoPi * i / glyphs;
				Vector2 gp = pos + ang.ToRotationVector2() * 306f * scale;
				float lit = ReUtils.Saturate(charge * glyphs - i);
				float f = lit > 0f ? 0.75f + 0.25f * MathF.Sin(Timer * 0.5f + i) : 0.25f;
				Color gc = lit > 0f ? Color.Lerp(ReUtils.Gold, ReUtils.PaleGold, lit * charge) : ReUtils.DeepGold;
				sb.Draw(atlas, gp, ReTex.GlyphFrame(i * 7 + (int)Offset), gc.Additive(a * f), ang + MathHelper.PiOver2, new Vector2(7, 9), 1.4f * scale, SpriteEffects.None, 0f);
				if (lit > 0.5f)
					FX.Bloom(sb, gp, ReUtils.Gold, 22f, 0.12f * a * f);
			}

			// 6. bolt spawn points flare before release
			int n = Math.Max(3, (int)Bolts);
			float mark = ReUtils.InverseLerp(Delay - 35, Delay, Timer) * a;
			if (mark > 0f && Timer < Delay) {
				for (int i = 0; i < n; i++) {
					Vector2 p = pos + (Offset + MathHelper.TwoPi * i / n).ToRotationVector2() * Radius;
					FX.Flare(sb, p, ReUtils.Ember, 26f * mark, mark * 0.9f, Timer * 0.05f);
				}
			}

			// 7. release flash
			if (release > 0f) {
				FX.Bloom(sb, pos, ReUtils.PaleGold, Radius * 1.1f, 0.25f * release);
				FX.Ring(sb, pos, ReUtils.Gold, Radius * (1f + (1f - release) * 0.15f), release);
			}
			return false;
		}

		private static void DrawEnergyRing(Vector2 center, float radius, float width, float scroll, Color color)
		{
			ringPts.Clear();
			const int seg = 96;
			for (int i = 0; i <= seg; i++)
				ringPts.Add(center + (MathHelper.TwoPi * i / seg).ToRotationVector2() * radius);
			Primitives.DrawStrip(ringPts, ReTex.Trail, _ => width, _ => color, scroll, 6f);
			Primitives.DrawStrip(ringPts, ReTex.Thread, _ => width * 0.25f, _ => color * 1.4f, -scroll * 1.7f, 3f);
		}
	}
}
