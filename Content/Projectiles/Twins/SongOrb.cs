using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles.Twins
{
	/// <summary>Remembers this tick's melee swing hitboxes so orbs can be cut by swords.</summary>
	public class MeleeTracker : GlobalItem
	{
		public static readonly List<Rectangle> Swings = new();

		public override void MeleeEffects(Item item, Player player, Rectangle hitbox)
		{
			if (item.damage > 0)
				Swings.Add(hitbox);
		}
	}

	public class MeleeTrackerSystem : ModSystem
	{
		public override void PostUpdateProjectiles() => MeleeTracker.Swings.Clear();
	}

	/// <summary>
	/// The twins' spheres (after NieR's bullets): GOLD orbs shatter when anything of yours hits them,
	/// DARK orbs cannot be broken. ai[0] = kind, ai[1] = behaviour, ai[2] = parameter.
	/// Behaviours: 0 = straight, accelerating by ai[2] (0 = constant);
	///             1 = drift, then lock on to the nearest player after ai[2] ticks;
	///             2 = petal: rise, then home gently.
	/// </summary>
	public class SongOrb : ModProjectile, IReProjectile
	{
		public const float Gold = 0f, Dark = 1f, Petal = 2f;
		public const float Straight = 0f, Delayed = 1f, Homing = 2f;

		public override string Texture => "Replicant/Assets/Textures/Projectiles/SongOrbGold";

		private float Kind => Projectile.ai[0];
		private float Behaviour => Projectile.ai[1];
		private float Param => Projectile.ai[2];
		private ref float Timer => ref Projectile.localAI[0];
		private ref float ClinkCooldown => ref Projectile.localAI[1];
		public bool Breakable => Kind != Dark;

		public override void SetStaticDefaults()
		{
			ProjectileID.Sets.TrailCacheLength[Type] = 6;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

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
			Timer++;
			if (Timer == 1) {
				if (Kind == Dark)
					Projectile.Resize(26, 26);
				else if (Kind == Petal)
					Projectile.Resize(14, 14);
			}
			if (ClinkCooldown > 0)
				ClinkCooldown--;
			switch ((int)Behaviour) {
				case 0:
					if (Param > 0f && Projectile.velocity.Length() < 13f)
						Projectile.velocity *= Param;
					break;
				case 1:
					if (Timer < Param)
						Projectile.velocity *= 0.95f;
					else if (Timer == (int)Param) {
						Player p = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
						Projectile.velocity = Projectile.Center.SafeDirectionTo(p.Center) * 3f;
						if (!Main.dedServ) {
							if (Kind == Gold)
								SoundEngine.PlaySound(TwinSounds.Note(Projectile.identity) with { Volume = 0.3f }, Projectile.Center);
							ParticleSystem.Flare(Projectile.Center, Kind == Dark ? ReUtils.Ember : ReUtils.Gold, 0.6f, 10);
						}
					}
					else if (Projectile.velocity.Length() < 11f)
						Projectile.velocity *= 1.06f;
					break;
				case 2: {
					Player p = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
					if (Timer > 35)
						Projectile.velocity = Vector2.Lerp(Projectile.velocity, Projectile.Center.SafeDirectionTo(p.Center) * 8f, 0.03f);
					Projectile.rotation = Projectile.velocity.ToRotation();
					break;
				}
			}
			Lighting.AddLight(Projectile.Center, Kind == Dark ? new Vector3(0.35f, 0.12f, 0.02f) : new Vector3(0.7f, 0.5f, 0.15f));
			CheckBreak();
		}

		/// <summary>Shatter on contact with the player's weapons; dark orbs just ring.</summary>
		private void CheckBreak()
		{
			Rectangle hb = Projectile.Hitbox;
			bool hit = false;
			foreach (Rectangle r in MeleeTracker.Swings) {
				if (r.Intersects(hb)) {
					hit = true;
					break;
				}
			}
			if (!hit) {
				foreach (Projectile p in Main.ActiveProjectiles) {
					if (p.friendly && !p.hostile && p.damage > 0 && p.Colliding(p.Hitbox, hb)) {
						hit = true;
						break;
					}
				}
			}
			if (!hit)
				return;
			if (Breakable) {
				Projectile.Kill();
			}
			else if (ClinkCooldown <= 0 && !Main.dedServ) {
				ClinkCooldown = 20;
				SoundEngine.PlaySound(TwinSounds.Clink, Projectile.Center);
				for (int i = 0; i < 5; i++)
					ParticleSystem.Spark(Projectile.Center, Main.rand.NextVector2Circular(5, 5), ReUtils.Gold, 0.6f, 12);
			}
		}

		public override void OnKill(int timeLeft)
		{
			if (Main.dedServ || timeLeft <= 0)
				return;
			if (Breakable) {
				SoundEngine.PlaySound(TwinSounds.Shatter, Projectile.Center);
				for (int i = 0; i < 9; i++) {
					Vector2 v = Main.rand.NextVector2Circular(6f, 6f);
					ParticleSystem.Spark(Projectile.Center, v, Kind == Petal ? new Color(255, 245, 220) : ReUtils.PaleGold, 0.8f, 18);
				}
				ParticleSystem.Glyph(Projectile.Center, Main.rand.NextVector2Circular(2f, 2f), ReUtils.Gold, 0.9f, 30);
				ParticleSystem.Ring(Projectile.Center, ReUtils.Gold, 0.05f, 1.12f, 12);
			}
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float spawn = ReUtils.InverseLerp(0, 8, Timer);
			if (Kind == Petal) {
				Texture2D pt = ReTex.LilyPetal;
				FX.Bloom(sb, pos, new Color(255, 220, 150), 18f, 0.3f * spawn);
				sb.Draw(pt, pos, null, Color.White * spawn, Projectile.rotation, pt.Size() / 2f, 1f, SpriteEffects.None, 0f);
				return false;
			}
			bool dark = Kind == Dark;
			Texture2D tex = dark ? ReTex.SongOrbDark : ReTex.SongOrbGold;
			int frame = (int)(Timer / 6) % 4;
			Rectangle src = tex.VFrame(frame, 4);
			// short trail
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float k = 1f - i / (float)Projectile.oldPos.Length;
				Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				if (dark)
					FX.Smoke(sb, p, Color.Black * (0.35f * k * spawn), 16f * k + 4f);
				else
					sb.Draw(tex, p, src, ReUtils.DeepGold.Additive(0.3f * k * spawn), 0f, src.Size() / 2f, 0.8f + 0.2f * k, SpriteEffects.None, 0f);
			}
			if (dark) {
				FX.Smoke(sb, pos, Color.Black * (0.6f * spawn), 30f);
				FX.Bloom(sb, pos, ReUtils.Ember, 28f, 0.22f * spawn);
			}
			else {
				FX.Bloom(sb, pos, ReUtils.Gold, 30f, 0.38f * spawn);
			}
			sb.Draw(tex, pos, src, Color.White * spawn, 0f, src.Size() / 2f, spawn, SpriteEffects.None, 0f);
			if (!dark)
				sb.Draw(tex, pos, src, Color.White.Additive(0.25f * spawn), 0f, src.Size() / 2f, spawn, SpriteEffects.None, 0f);
			return false;
		}
	}
}
