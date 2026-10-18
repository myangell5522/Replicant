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
		public const float Straight = 0f, Delayed = 1f, Homing = 2f, Curve = 3f;

		public override string Texture => "Replicant/Assets/Textures/Projectiles/SongOrbGold";

		private float Kind => Projectile.ai[0];
		private float Behaviour => Projectile.ai[1];
		private float Param => Projectile.ai[2];
		private ref float Timer => ref Projectile.localAI[0];
		private ref float ClinkCooldown => ref Projectile.localAI[1];
		public bool Breakable => Kind != Dark;

		public override void SetStaticDefaults()
		{
			ProjectileID.Sets.TrailCacheLength[Type] = 12;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

		public override void SetDefaults()
		{
			Projectile.width = 26;
			Projectile.height = 26;
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
					Projectile.Resize(32, 32);
				else if (Kind == Petal)
					Projectile.Resize(24, 24);
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
				case 3:
					// a bending arc: turns by Param rad/tick, easing off as it travels
					Projectile.velocity = Projectile.velocity.RotatedBy(Param * MathF.Max(0.15f, 1f - Timer / 160f));
					if (Projectile.velocity.Length() < 9f)
						Projectile.velocity *= 1.012f;
					break;
				case 2: {
					if (Kind == Petal) {
						PetalAI();
						break;
					}
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

		// ---- lily blades: bloom where they are sown, hang while a thread marks their aim, then cut in a straight line.
		// Each one waits a different beat, so a sown row fires as a rolling wave instead of a homing clump.
		public const int PetalBloom = 26, PetalAim = 22;
		private int PetalLock => PetalBloom + (Projectile.identity * 7) % 30;
		private ref float PetalPhase => ref Projectile.localAI[2];

		private void PetalAI()
		{
			int lockAt = PetalLock;
			if (Timer < lockAt) {
				// drift to a stop, opening
				Projectile.velocity *= 0.93f;
				Projectile.rotation += 0.06f * (1f - Timer / (float)lockAt);
				Player p = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
				PetalPhase = (Projectile.Center.SafeDirectionTo(p.Center).ToRotation());
			}
			else if (Timer < lockAt + PetalAim) {
				// aim locked: a thread is drawn, the blade turns to face it and trembles
				Projectile.velocity *= 0.8f;
				Projectile.rotation = Projectile.rotation.AngleLerp(PetalPhase, 0.25f);
				if (Timer == lockAt && !Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Note(Projectile.identity) with { Volume = 0.22f, Pitch = 0.5f }, Projectile.Center);
			}
			else {
				if (Timer == lockAt + PetalAim) {
					Projectile.velocity = PetalPhase.ToRotationVector2() * 4f;
					if (!Main.dedServ)
						ParticleSystem.Flare(Projectile.Center, new Color(255, 236, 190), 0.7f, 10);
				}
				if (Projectile.velocity.Length() < 16f)
					Projectile.velocity *= 1.11f;
				Projectile.rotation = Projectile.velocity.ToRotation();
			}
		}

		public override bool CanHitPlayer(Player target) => Kind != Petal || Timer > PetalBloom * 0.5f;

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

		/// <summary>A lily blade: five pale petals around a gold glyph heart, a gold rim, an aim thread and a cutting ribbon.</summary>
		private void DrawLilyBlade(SpriteBatch sb, Vector2 pos)
		{
			Texture2D pt = ReTex.LilyPetal;
			Texture2D px = Terraria.GameContent.TextureAssets.MagicPixel.Value;
			int lockAt = PetalLock;
			float open = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, PetalBloom, Timer));
			bool aiming = Timer >= lockAt && Timer < lockAt + PetalAim;
			bool flying = Timer >= lockAt + PetalAim;
			float time = Main.GlobalTimeWrappedHourly;
			Color pale = new(255, 246, 226);
			// aim thread: dashed gold line growing out toward where it will cut
			if (aiming) {
				float k = (Timer - lockAt) / (float)PetalAim;
				Vector2 d = PetalPhase.ToRotationVector2();
				float len = 120f + 520f * ReUtils.EaseOutCubic(k);
				for (float s = 18f; s < len; s += 16f) {
					float a = (1f - s / len) * (0.35f + 0.65f * k);
					sb.Draw(px, pos + d * s, new Rectangle(0, 0, 1, 1), ReUtils.Gold.Additive(a * 0.8f), PetalPhase, new Vector2(0.5f, 0.5f), new Vector2(8f, 2f), SpriteEffects.None, 0f);
				}
				FX.Bloom(sb, pos, ReUtils.Gold, 30f + 16f * k, 0.35f + 0.3f * k);
			}
			// cutting ribbon behind it in flight
			if (flying) {
				for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
					if (Projectile.oldPos[i] == Vector2.Zero)
						continue;
					float k = 1f - i / (float)Projectile.oldPos.Length;
					Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
					sb.Draw(pt, p, null, Color.Lerp(ReUtils.Gold, pale, k).Additive(0.45f * k), Projectile.rotation, pt.Size() / 2f, new Vector2(1.8f, 0.9f) * (0.5f + 0.6f * k), SpriteEffects.None, 0f);
				}
			}
			FX.Bloom(sb, pos, new Color(255, 214, 140), 34f * open, 0.4f);
			// petals: radial when open, folding into a blade as it flies
			float fold = flying ? 1f : aiming ? 0.5f * (Timer - lockAt) / PetalAim : 0f;
			for (int i = 0; i < 5; i++) {
				float baseAng = Projectile.rotation + i / 5f * MathHelper.TwoPi;
				float ang = MathHelper.Lerp(baseAng, Projectile.rotation + (i - 2) * 0.22f + MathHelper.Pi, fold);
				Vector2 off = ang.ToRotationVector2() * (11f * open * (1f - fold * 0.5f));
				float sc = 1.25f * open;
				// rim then body
				sb.Draw(pt, pos + off, null, ReUtils.Gold.Additive(0.7f), ang, pt.Size() / 2f, sc * 1.25f, SpriteEffects.None, 0f);
				sb.Draw(pt, pos + off, null, pale, ang, pt.Size() / 2f, sc, SpriteEffects.None, 0f);
			}
			// the glyph heart
			sb.Draw(ReTex.GlyphAtlasGlow, pos, ReTex.GlyphFrame(Projectile.identity + (int)(time * 4f)), ReUtils.Ember.Additive(1f), 0f, new Vector2(7, 9), 0.9f * open, SpriteEffects.None, 0f);
			FX.Bloom(sb, pos, Color.White, 10f, 0.7f * open);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float spawn = ReUtils.InverseLerp(0, 8, Timer);
			if (Kind == Petal) {
				DrawLilyBlade(sb, pos);
				return false;
			}
			bool dark = Kind == Dark;
			Texture2D tex = dark ? ReTex.SongOrbDark : ReTex.SongOrbGold;
			int frame = (int)(Timer / 6) % 4;
			Rectangle src = tex.VFrame(frame, 4);
			float time = Main.GlobalTimeWrappedHourly;
			float pulse = 1f + 0.08f * MathF.Sin(Timer * 0.3f + Projectile.identity);
			float scale = (dark ? 1.35f : 1.45f) * pulse;
			// a comet tail: a ribbon of fading copies, stretched along the motion
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float k = 1f - i / (float)Projectile.oldPos.Length;
				Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				if (dark) {
					FX.Smoke(sb, p, Color.Black * (0.4f * k * spawn), 20f * k + 5f);
					if (i % 3 == 0)
						FX.Bloom(sb, p, ReUtils.Ember, 10f * k, 0.25f * k * spawn);
				}
				else {
					sb.Draw(tex, p, src, ReUtils.GoldRamp(k * 0.8f).Additive(0.35f * k * spawn), 0f, src.Size() / 2f, scale * (0.45f + 0.55f * k), SpriteEffects.None, 0f);
				}
			}
			if (dark) {
				// a black pearl wrapped in a slow ember crackle
				FX.Smoke(sb, pos, Color.Black * (0.7f * spawn), 38f);
				FX.Bloom(sb, pos, ReUtils.Ember, 34f, 0.26f * spawn);
				sb.Draw(tex, pos, src, Color.White * spawn, time * 0.6f, src.Size() / 2f, scale * spawn, SpriteEffects.None, 0f);
				for (int k = 0; k < 3; k++) {
					float a = time * 2.2f + k * MathHelper.TwoPi / 3f + Projectile.identity;
					Vector2 q = pos + a.ToRotationVector2() * 20f * scale * 0.7f;
					FX.Flare(sb, q, ReUtils.Ember, 6f, 0.6f * spawn, a, 0.4f);
				}
			}
			else {
				// a breakable note: gold shell, a glyph turning inside it, a thin halo that rings with the beat
				FX.Bloom(sb, pos, ReUtils.Gold, 40f, 0.42f * spawn);
				sb.Draw(tex, pos, src, Color.White * spawn, 0f, src.Size() / 2f, scale * spawn, SpriteEffects.None, 0f);
				sb.Draw(tex, pos, src, Color.White.Additive(0.3f * spawn), 0f, src.Size() / 2f, scale * spawn, SpriteEffects.None, 0f);
				sb.Draw(ReTex.GlyphAtlasGlow, pos, ReTex.GlyphFrame(Projectile.identity + (int)(Timer / 20)), Color.White.Additive(0.8f * spawn), time * 1.5f, new Vector2(7, 9), 0.75f * spawn, SpriteEffects.None, 0f);
				float ring = (Timer % 30f) / 30f;
				FX.Ring(sb, pos, ReUtils.PaleGold, 16f + ring * 14f, (1f - ring) * 0.5f * spawn);
			}
			return false;
		}
	}
}
