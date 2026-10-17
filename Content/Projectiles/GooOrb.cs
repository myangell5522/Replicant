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
	/// <summary>Lobbed ball of black goo; bursts into glyph shrapnel on impact or after a fuse.</summary>
	public class GooOrb : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/Projectiles/GooOrb";

		public override void SetStaticDefaults()
		{
			Main.projFrames[Type] = 4;
			ProjectileID.Sets.TrailCacheLength[Type] = 10;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

		public override void SetDefaults()
		{
			Projectile.width = 30;
			Projectile.height = 30;
			Projectile.hostile = true;
			Projectile.tileCollide = true;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 140;
		}

		public override void AI()
		{
			// Fall through platforms/tiles for the first few frames so it can leave the boss.
			Projectile.tileCollide = Projectile.timeLeft < 120;
			Projectile.velocity.Y = MathF.Min(Projectile.velocity.Y + 0.24f, 14f);
			Projectile.rotation += Projectile.velocity.X * 0.03f;
			if (++Projectile.frameCounter >= 5) {
				Projectile.frameCounter = 0;
				Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];
			}
			Lighting.AddLight(Projectile.Center, 0.5f, 0.3f, 0.08f);
			if (!Main.dedServ) {
				if (Main.rand.NextBool(2))
					ParticleSystem.Smoke(Projectile.Center, -Projectile.velocity * 0.1f, Color.Black, 0.6f, 30);
				if (Main.rand.NextBool(6))
					ParticleSystem.Goo(Projectile.Center, Projectile.velocity * 0.3f, 0.8f, 40);
			}
		}

		public override bool OnTileCollide(Vector2 oldVelocity) => true;

		public override void OnKill(int timeLeft)
		{
			if (Main.netMode != NetmodeID.MultiplayerClient) {
				int n = Main.expertMode ? 7 : 5;
				float start = Main.rand.NextFloat(MathHelper.TwoPi);
				for (int i = 0; i < n; i++) {
					Vector2 v = (start + MathHelper.TwoPi * i / n).ToRotationVector2() * 2.6f;
					Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, v, ModContent.ProjectileType<GlyphBolt>(),
						Projectile.damage, 0f, Main.myPlayer, GlyphBolt.ModeAccelerate, 1.045f);
				}
			}
			if (Main.dedServ)
				return;
			SoundEngine.PlaySound(ReSounds.GooSpit with { Pitch = -0.35f, Volume = 0.6f }, Projectile.Center);
			ParticleSystem.Ring(Projectile.Center, ReUtils.Gold, 0.05f, 1.13f, 18);
			ParticleSystem.Bloom(Projectile.Center, Vector2.Zero, ReUtils.Ember, 1.1f, 14);
			for (int i = 0; i < 12; i++) {
				ParticleSystem.Goo(Projectile.Center, Main.rand.NextVector2Circular(6f, 6f) - Vector2.UnitY * 3f, Main.rand.NextFloat(0.8f, 1.4f));
				ParticleSystem.Smoke(Projectile.Center, Main.rand.NextVector2Circular(3f, 3f), Color.Black, 1f, 40);
			}
			ScreenFX.Shake(2.5f);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D tex = TextureAssets.Projectile[Type].Value;
			Rectangle frame = tex.VFrame(Projectile.frame, Main.projFrames[Type]);
			Vector2 pos = Projectile.Center - Main.screenPosition;
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float t = i / (float)Projectile.oldPos.Length;
				Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				sb.Draw(tex, p, frame, Color.Black * (1f - t) * 0.5f, Projectile.rotation, frame.Size() / 2f, 1f - t * 0.5f, SpriteEffects.None, 0f);
			}
			float fuse = ReUtils.InverseLerp(30, 0, Projectile.timeLeft);
			FX.Bloom(sb, pos, Color.Lerp(ReUtils.DeepGold, ReUtils.Ember, fuse), 46f + fuse * 20f, 0.35f + fuse * 0.4f);
			Color lit = Color.Lerp(lightColor, Color.White, 0.4f);
			sb.Draw(tex, pos, frame, lit, Projectile.rotation, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
			sb.Draw(ReTex.GooOrbGlow, pos, frame, Color.White.Additive(0.8f + 0.2f * MathF.Sin(Projectile.timeLeft * 0.5f)), Projectile.rotation, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
			return false;
		}
	}
}
