using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles
{
	/// <summary>
	/// Obsidian spear inscribed with glyphs. Spawns with velocity = unit direction,
	/// shows a telegraph line for ai[0] ticks, then fires at ai[1] speed.
	/// </summary>
	public class GlyphLance : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/Projectiles/GlyphLance";

		private ref float TelegraphTime => ref Projectile.ai[0];
		private ref float Speed => ref Projectile.ai[1];
		private ref float Timer => ref Projectile.localAI[0];
		private const float Length = 140f;

		private Vector2 Direction => Projectile.rotation.ToRotationVector2();
		private bool Launched => Timer >= TelegraphTime;

		public override void SetDefaults()
		{
			Projectile.width = 20;
			Projectile.height = 20;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 600;
		}

		public override void AI()
		{
			if (Timer == 0) {
				Projectile.rotation = Projectile.velocity.ToRotation();
				Projectile.timeLeft = (int)TelegraphTime + 110;
			}
			Timer++;
			if (!Launched) {
				Projectile.velocity = Vector2.Zero;
				if (!Main.dedServ && Timer % 4 == 0)
					ParticleSystem.Glyph(Projectile.Center + Main.rand.NextVector2Circular(30, 30), Direction * 1.5f, ReUtils.Gold, 0.6f, 18);
			}
			else {
				if (Projectile.localAI[1] == 0f) {
					Projectile.localAI[1] = 1f;
					Projectile.velocity = Direction * (Speed <= 0 ? 26f : Speed);
					if (!Main.dedServ) {
						Player local = Main.LocalPlayer;
						if (local.Distance(Projectile.Center) < 1600f)
							SoundEngine.PlaySound(ReSounds.LanceFire, Projectile.Center);
						ParticleSystem.Flare(Projectile.Center, ReUtils.PaleGold, 1.2f, 12);
						ParticleSystem.Ring(Projectile.Center, ReUtils.Gold, 0.05f, 1.15f, 14);
					}
				}
				if (!Main.dedServ && Main.rand.NextBool(2))
					ParticleSystem.Spark(Projectile.Center - Direction * Main.rand.NextFloat(Length), -Projectile.velocity * 0.15f, ReUtils.Gold, 0.7f, 14);
			}
			Lighting.AddLight(Projectile.Center, 0.9f, 0.6f, 0.2f);
		}

		public override bool CanHitPlayer(Player target) => Launched;

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
		{
			Vector2 tip = Projectile.Center + Direction * 20f;
			Vector2 tail = Projectile.Center - Direction * (Length - 20f);
			float _ = 0f;
			return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), tail, tip, 14f, ref _);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D tex = TextureAssets.Projectile[Type].Value;
			Texture2D glow = ReTex.GlyphLanceGlow;
			Texture2D trail = ReTex.Trail;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float rot = Projectile.rotation + MathHelper.PiOver2; // sprite points up
			// origin near the tip (sprite is 24x144, tip at the top)
			Vector2 origin = new(tex.Width / 2f, 20f);

			if (!Launched) {
				float t = Timer / TelegraphTime;
				float lineOpacity = ReUtils.InverseLerp(0f, 0.3f, t) * (0.55f + 0.45f * MathF.Sin(Timer * 0.6f * (0.5f + t)));
				Vector2 lineOrigin = new(0f, trail.Height / 2f);
				sb.Draw(trail, pos, null, ReUtils.DeepGold.Additive(0.4f * lineOpacity), Projectile.rotation, lineOrigin, new Vector2(2400f / trail.Width, 0.35f + t * 0.3f), SpriteEffects.None, 0f);
				sb.Draw(trail, pos, null, ReUtils.Gold.Additive(0.9f * lineOpacity), Projectile.rotation, lineOrigin, new Vector2(2400f / trail.Width, 0.05f + t * 0.05f), SpriteEffects.None, 0f);
				// lance materialising, shaking
				float fade = ReUtils.InverseLerp(0.2f, 0.9f, t);
				Vector2 shake = Main.rand.NextVector2Circular(2f, 2f) * t;
				FX.Bloom(sb, pos, ReUtils.Gold, 40f, 0.35f * fade);
				sb.Draw(tex, pos + shake, null, Color.White * fade, rot, origin, 1f, SpriteEffects.None, 0f);
				sb.Draw(glow, pos + shake, null, Color.White.Additive(fade), rot, origin, 1f, SpriteEffects.None, 0f);
				return false;
			}

			// speed streak behind the lance
			Vector2 streakOrigin = new(trail.Width, trail.Height / 2f);
			sb.Draw(trail, pos, null, ReUtils.Gold.Additive(0.6f), Projectile.rotation, streakOrigin, new Vector2(0.8f, 0.45f), SpriteEffects.None, 0f);
			sb.Draw(trail, pos, null, Color.White.Additive(0.5f), Projectile.rotation, streakOrigin, new Vector2(0.6f, 0.12f), SpriteEffects.None, 0f);
			FX.Bloom(sb, pos, ReUtils.PaleGold, 36f, 0.45f);
			sb.Draw(tex, pos, null, Color.White, rot, origin, 1f, SpriteEffects.None, 0f);
			sb.Draw(glow, pos, null, Color.White.Additive(1f), rot, origin, 1f, SpriteEffects.None, 0f);
			return false;
		}
	}
}
