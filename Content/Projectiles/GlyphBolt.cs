using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles
{
	/// <summary>Marker for every hostile projectile Re owns (cleared when the death cutscene starts).</summary>
	public interface IReProjectile { }

	/// <summary>
	/// A burning rune. ai[0] = mode, ai[1] = mode parameter, ai[2] = spare.
	///  Mode 0 (accelerate): ai[1] = per-tick speed multiplier.
	///  Mode 1 (mine): drifts to a stop, then after ai[1] ticks launches at the nearest player.
	/// </summary>
	public class GlyphBolt : ModProjectile, IReProjectile
	{
		public const float ModeAccelerate = 0f;
		public const float ModeMine = 1f;
		private const float MaxSpeed = 15f;

		public override string Texture => "Replicant/Assets/Textures/Projectiles/GlyphBolt";

		private ref float Mode => ref Projectile.ai[0];
		private ref float Param => ref Projectile.ai[1];
		private ref float Timer => ref Projectile.localAI[0];

		public override void SetStaticDefaults()
		{
			Main.projFrames[Type] = 8;
			ProjectileID.Sets.TrailCacheLength[Type] = 8;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

		public override void SetDefaults()
		{
			Projectile.width = 18;
			Projectile.height = 18;
			Projectile.hostile = true;
			Projectile.friendly = false;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 360;
			Projectile.alpha = 0;
		}

		public override void AI()
		{
			if (Timer == 0) {
				Projectile.frame = Projectile.identity % Main.projFrames[Type];
				if (!Main.dedServ)
					ParticleSystem.Flare(Projectile.Center, ReUtils.Gold, 0.5f, 10);
			}
			Timer++;

			if (Mode == ModeMine) {
				float delay = Param;
				if (Timer < delay) {
					Projectile.velocity *= 0.93f;
					if (Timer > delay - 15 && !Main.dedServ && Timer % 3 == 0)
						ParticleSystem.Glyph(Projectile.Center + Main.rand.NextVector2Circular(20, 20), Vector2.Zero, ReUtils.Ember, 0.6f, 12);
				}
				else if (Projectile.localAI[1] == 0f) {
					Projectile.localAI[1] = 1f;
					Player p = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
					Projectile.velocity = Projectile.Center.SafeDirectionTo(p.Center) * 2.5f;
					Projectile.netUpdate = true;
					if (!Main.dedServ) {
						ParticleSystem.Ring(Projectile.Center, ReUtils.Gold, 0.05f, 1.12f, 14);
						if (Projectile.identity % 4 == 0)
							SoundEngine.PlaySound(ReSounds.GlyphShoot with { Volume = 0.25f, Pitch = 0.4f }, Projectile.Center);
					}
				}
				else if (Projectile.velocity.Length() < MaxSpeed) {
					Projectile.velocity *= 1.07f;
				}
			}
			else {
				if (Projectile.velocity.Length() < MaxSpeed)
					Projectile.velocity *= Param <= 0f ? 1.04f : Param;
			}

			Lighting.AddLight(Projectile.Center, 0.8f, 0.55f, 0.15f);
			if (!Main.dedServ && Main.rand.NextBool(5))
				ParticleSystem.Spark(Projectile.Center, -Projectile.velocity * 0.2f + Main.rand.NextVector2Circular(1, 1), ReUtils.Gold, 0.5f, 12);
		}

		public override bool CanHitPlayer(Player target) => Mode != ModeMine || Timer > Param * 0.6f;

		public override void OnKill(int timeLeft)
		{
			if (Main.dedServ)
				return;
			for (int i = 0; i < 4; i++)
				ParticleSystem.Glyph(Projectile.Center, Main.rand.NextVector2Circular(3f, 3f), ReUtils.Gold, 0.7f, 20);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D tex = TextureAssets.Projectile[Type].Value;
			Rectangle frame = tex.VFrame(Projectile.frame, Main.projFrames[Type]);
			Vector2 origin = frame.Size() / 2f;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float pulse = 0.85f + 0.15f * MathF.Sin(Timer * 0.4f + Projectile.identity);
			float spawnFade = ReUtils.InverseLerp(0, 6, Timer);
			bool armed = Mode != ModeMine || Timer >= Param;
			Color halo = armed ? ReUtils.Gold : ReUtils.Ember;

			// thin fading trail of the same rune
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float t = i / (float)Projectile.oldPos.Length;
				Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				sb.Draw(tex, p, frame, ReUtils.DeepGold.Additive((1f - t) * 0.35f * spawnFade), 0f, origin, 1f - t * 0.5f, SpriteEffects.None, 0f);
			}
			FX.Bloom(sb, pos, halo, 26f, 0.32f * pulse * spawnFade);
			float wobble = MathF.Sin(Timer * 0.12f + Projectile.identity) * 0.12f;
			sb.Draw(tex, pos, frame, Color.White * spawnFade, wobble, origin, 1f, SpriteEffects.None, 0f);
			sb.Draw(tex, pos, frame, Color.White.Additive(0.35f * pulse * spawnFade), wobble, origin, 1f, SpriteEffects.None, 0f);
			return false;
		}
	}
}
