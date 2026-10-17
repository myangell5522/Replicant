using System;
using Microsoft.Xna.Framework;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.ReBoss
{
	public partial class Re
	{
		private Player Target => Main.player[NPC.target];
		private bool Server => Main.netMode != NetmodeID.MultiplayerClient;
		private float Speedup => Unraveled ? 1.25f : 1f;

		public override void AI()
		{
			visualTime++;
			if (tentacles == null)
				InitTentacles();

			// ---------------------------------------------------------- targeting / despawn
			if (State != AIState.Dying) {
				if (NPC.target < 0 || NPC.target >= Main.maxPlayers || Target.dead || !Target.active || NPC.Distance(Target.Center) > 6000f)
					NPC.TargetClosest();
				if (Target.dead || !Target.active || NPC.Distance(Target.Center) > 6000f) {
					Despawn();
					UpdateVisuals();
					return;
				}
			}
			NPC.timeLeft = 1800;

			if (State != AIState.Intro && State != AIState.Dying)
				NPC.Opacity = MathHelper.Min(1f, NPC.Opacity + 0.05f);

			// ---------------------------------------------------------- phase transition
			if (!Unraveled && State != AIState.Intro && State != AIState.Dying && State != AIState.Unravel && NPC.life < NPC.lifeMax / 2) {
				SwitchTo(AIState.Unravel);
			}

			mawTarget = 0f;
			eyeFlare *= 0.92f;
			tremble *= 0.9f;
			telegraphLine *= 0.85f;

			switch (State) {
				case AIState.Intro: DoIntro(); break;
				case AIState.Reposition: DoReposition(); break;
				case AIState.GlyphBarrage: DoGlyphBarrage(); break;
				case AIState.TendrilDash: DoTendrilDash(); break;
				case AIState.RuneCircle: DoRuneCircle(); break;
				case AIState.GooRain: DoGooRain(); break;
				case AIState.LanceStorm: DoLanceStorm(); break;
				case AIState.Unravel: DoUnravel(); break;
				case AIState.Dying: DoDying(); break;
			}
			Timer++;

			// body lean
			float lean = State == AIState.TendrilDash ? 0.03f : 0.018f;
			NPC.rotation = NPC.rotation.AngleLerp(MathHelper.Clamp(NPC.velocity.X * lean, -0.5f, 0.5f), 0.15f);

			Lighting.AddLight(NPC.Center, new Vector3(0.9f, 0.6f, 0.15f) * (0.6f + glowBoost) * NPC.Opacity);
			UpdateVisuals();
		}

		// =================================================================== helpers
		private void InitTentacles()
		{
			tentacles = new[] {
				new ReTentacle(new Vector2(-30, 34), MathHelper.PiOver2 + 0.55f, 10, 22f, 0.0f, 1.0f),
				new ReTentacle(new Vector2(30, 34), MathHelper.PiOver2 - 0.55f, 10, 22f, 1.7f, 1.0f),
				new ReTentacle(new Vector2(-38, 0), MathHelper.Pi - 0.35f, 9, 22f, 3.1f, 0.9f),
				new ReTentacle(new Vector2(38, 0), 0.35f, 9, 22f, 4.4f, 0.9f),
				new ReTentacle(new Vector2(-14, 48), MathHelper.PiOver2 + 0.18f, 8, 20f, 2.2f, 0.75f),
				new ReTentacle(new Vector2(14, 48), MathHelper.PiOver2 - 0.18f, 8, 20f, 5.3f, 0.75f),
			};
			foreach (ReTentacle t in tentacles)
				t.Reset(NPC.Center);
		}

		private void SwitchTo(AIState next)
		{
			State = next;
			Timer = -1; // incremented to 0 at end of AI
			SubCounter = 0;
			Misc = 0;
			NPC.netUpdate = true;
		}

		private void FlyTowards(Vector2 destination, float speed, float inertia)
		{
			Vector2 toDest = destination - NPC.Center;
			float dist = toDest.Length();
			if (dist < 1f)
				return;
			float s = MathF.Min(speed, dist / 8f + 2f);
			Vector2 desired = toDest / dist * s;
			NPC.velocity = (NPC.velocity * (inertia - 1f) + desired) / inertia;
		}

		private AIState NextAttack()
		{
			if (attackBag.Count == 0) {
				attackBag.AddRange(new[] { AIState.GlyphBarrage, AIState.TendrilDash, AIState.RuneCircle, AIState.GooRain, AIState.LanceStorm });
				// shuffle, avoiding an immediate repeat
				for (int i = attackBag.Count - 1; i > 0; i--) {
					int j = Main.rand.Next(i + 1);
					(attackBag[i], attackBag[j]) = (attackBag[j], attackBag[i]);
				}
				if (attackBag[0] == lastAttack)
					(attackBag[0], attackBag[^1]) = (attackBag[^1], attackBag[0]);
			}
			AIState a = attackBag[0];
			attackBag.RemoveAt(0);
			lastAttack = a;
			return a;
		}

		private void Despawn()
		{
			NPC.velocity.Y -= 0.5f;
			NPC.velocity.X *= 0.95f;
			NPC.Opacity -= 0.02f;
			NPC.EncourageDespawn(10);
			if (NPC.Opacity <= 0f)
				NPC.active = false;
		}

		private int ProjType<T>() where T : ModProjectile => ModContent.ProjectileType<T>();

		private Vector2 MawPosition => NPC.Center + new Vector2(0, 12).RotatedBy(NPC.rotation);

		private int Shoot<T>(Vector2 pos, Vector2 vel, int damage, float ai0 = 0, float ai1 = 0, float ai2 = 0) where T : ModProjectile
		{
			if (!Server)
				return -1;
			return Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel, ProjType<T>(), ReUtils.ProjDamage(damage), 0f, Main.myPlayer, ai0, ai1, ai2);
		}

		// =================================================================== states
		// ---------------------------------------------------------------- cinematic entrance
		public const int IntroLength = 270;
		public const int IntroTear = 20;        // a crack of light splits the air
		public const int IntroPour = 55;        // goo + glyphs spiral into the tear
		public const int IntroReveal = 118;     // body condenses top → bottom
		public const int IntroRoar = 152;       // impact
		/// <summary>0..1 how much of the body has condensed (drawn top → bottom).</summary>
		public float IntroRevealAmount => State == AIState.Intro ? ReUtils.InverseLerp(IntroReveal, IntroRoar - 4, Timer) : 1f;

		private void DoIntro()
		{
			NPC.dontTakeDamage = true;
			if (Timer == 0) {
				NPC.Center = Target.Center + new Vector2(0, -420);
				NPC.velocity = Vector2.Zero;
				if (!Main.dedServ) {
					Cinematic.Begin(NPC.Center, IntroLength, IntroRoar);
					foreach (ReTentacle t in tentacles)
						t.Reset(NPC.Center);
				}
			}
			NPC.velocity *= 0.85f;
			NPC.Opacity = 1f;

			if (!Main.dedServ) {
				Cinematic.SetFocus(NPC.Center + new Vector2(0, 40));
				Vector2 c = NPC.Center;

				// --- the tear
				if (Timer == IntroTear) {
					SoundEngine.PlaySound(ReSounds.Sigil with { Pitch = -0.6f, Volume = 0.9f }, c);
					ScreenFX.Shake(3f);
				}
				// --- the pour: black goo and glyphs spiral inward
				if (Timer >= IntroPour && Timer < IntroRoar) {
					float k = ReUtils.InverseLerp(IntroPour, IntroRoar, Timer);
					for (int i = 0; i < 2 + (int)(k * 4); i++) {
						float ang = Main.rand.NextFloat(MathHelper.TwoPi);
						float dist = Main.rand.NextFloat(220f, 520f);
						Vector2 from = c + ang.ToRotationVector2() * dist;
						// tangential + inward velocity = vortex
						Vector2 vel = (c - from) * 0.035f + (ang + MathHelper.PiOver2).ToRotationVector2() * 7f;
						if (Main.rand.NextBool(3))
							ParticleSystem.Glyph(from, vel, ReUtils.GoldRamp(Main.rand.NextFloat(0.3f, 0.9f)), Main.rand.NextFloat(0.7f, 1.1f), 34);
						else
							ParticleSystem.Smoke(from, vel * 0.9f, Color.Black, Main.rand.NextFloat(1.2f, 2.2f), 40);
					}
					if (Timer % 3 == 0)
						ParticleSystem.Goo(c + Main.rand.NextVector2Circular(30, 30), Main.rand.NextVector2Circular(2, 2), 1.2f, 40);
					ScreenFX.Shake(1f + k * 3f);
				}
				if (Timer == IntroRoar - 57)
					SoundEngine.PlaySound(ReSounds.Awaken, c); // its swell lands the growl on the impact
				// --- the reveal scanline throws sparks
				if (Timer >= IntroReveal && Timer < IntroRoar) {
					float y = -88f + 168f * IntroRevealAmount;
					for (int i = 0; i < 3; i++)
						ParticleSystem.Spark(c + new Vector2(Main.rand.NextFloat(-60f, 60f), y), new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-4f, -1f)), ReUtils.Gold, 0.8f, 18);
				}
				// --- IMPACT
				if (Timer == IntroRoar) {
					ScreenFX.Shake(26f);
					ScreenFX.ZoomPunch(0.16f);
					Cinematic.Flash(0.55f);
					for (int i = 0; i < 4; i++)
						ParticleSystem.Ring(c, i % 2 == 0 ? ReUtils.Gold : ReUtils.Ember, 0.15f + i * 0.08f, 1.07f + i * 0.02f, 40 + i * 10);
					ParticleSystem.GlyphBurst(c, 70, 16f, 1.2f);
					for (int i = 0; i < 40; i++)
						ParticleSystem.Smoke(c, Main.rand.NextVector2CircularEdge(1f, 1f) * Main.rand.NextFloat(6f, 16f), Color.Black, Main.rand.NextFloat(1.6f, 2.8f), 70);
					for (int i = 0; i < 20; i++)
						ParticleSystem.Goo(c, Main.rand.NextVector2Circular(10f, 10f) - Vector2.UnitY * 4f, 1.4f, 80);
					BossIntroCard.Show(NPC.whoAmI);
				}
				if (Timer > IntroRoar && Timer < IntroRoar + 70) {
					tremble = 3.5f * (1f - (Timer - IntroRoar) / 70f);
					glowBoost = 0.9f;
					eyeFlare = 1f;
					if (Timer % 2 == 0)
						ParticleSystem.Glyph(c + Main.rand.NextVector2Circular(70, 80), Main.rand.NextVector2Circular(2f, 2f) - Vector2.UnitY * 2.5f, ReUtils.Gold, 0.9f, 40);
				}
			}
			mawTarget = Timer > IntroRoar - 6 && Timer < IntroRoar + 60 ? 1f : 0f;
			foreach (ReTentacle t in tentacles)
				t.Flare = Timer < IntroRoar + 40 ? 1f : MathHelper.Lerp(t.Flare, 0f, 0.05f);

			if (Timer >= IntroLength) {
				NPC.dontTakeDamage = false;
				SwitchTo(AIState.Reposition);
			}
		}

		private void DoReposition()
		{
			int duration = (int)(55 / Speedup);
			Vector2 dest = Target.Center + new Vector2(sideSign * 120f, -300f);
			FlyTowards(dest, 13f * Speedup, 16f);
			if (Timer >= duration && Server) {
				sideSign = Target.Center.X > NPC.Center.X ? -1 : 1;
				SwitchTo(NextAttack());
			}
		}

		private void DoGlyphBarrage()
		{
			Vector2 dest = Target.Center + new Vector2(sideSign * 420f, -240f);
			FlyTowards(dest, 12f, 20f);
			mawTarget = Timer > 15 ? 1f : 0f;
			foreach (ReTentacle t in tentacles) t.Flare = MathHelper.Lerp(t.Flare, 0.8f, 0.1f);

			if (Timer == 0 && !Main.dedServ)
				SoundEngine.PlaySound(ReSounds.Charge, NPC.Center);

			int waves = Unraveled ? 4 : 3;
			int spacing = Unraveled ? 32 : 40;
			int wave = (int)((Timer - 40) / spacing);
			if (Timer >= 40 && (Timer - 40) % spacing == 0 && wave < waves) {
				int count = (Main.expertMode ? 9 : 7) + (Unraveled ? 2 : 0);
				float spread = MathHelper.ToRadians(Unraveled ? 85f : 70f);
				Vector2 aim = MawPosition.SafeDirectionTo(Target.Center + Target.velocity * 10f);
				float offset = wave % 2 == 1 ? spread / (count - 1) * 0.5f : 0f;
				for (int i = 0; i < count; i++) {
					float rot = MathHelper.Lerp(-spread / 2f, spread / 2f, i / (float)(count - 1)) + offset;
					Shoot<GlyphBolt>(MawPosition, aim.RotatedBy(rot) * 4.5f, 120, GlyphBolt.ModeAccelerate, 1.045f);
				}
				if (wave == waves - 1) {
					int ring = Unraveled ? 20 : 14;
					for (int i = 0; i < ring; i++)
						Shoot<GlyphBolt>(MawPosition, (MathHelper.TwoPi * i / ring).ToRotationVector2() * 2.5f, 110, GlyphBolt.ModeAccelerate, 1.03f);
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(ReSounds.GlyphShoot, NPC.Center);
					ParticleSystem.Flare(MawPosition, ReUtils.Ember, 1.4f, 14);
					ParticleSystem.Ring(MawPosition, ReUtils.Gold, 0.1f, 1.1f, 18);
					ScreenFX.Shake(4f);
					glowBoost = 0.6f;
					eyeFlare = 1f;
				}
				NPC.velocity -= aim * 4f; // recoil
			}
			if (Timer >= 40 + spacing * waves + 40)
				SwitchTo(AIState.Reposition);
		}

		private void DoTendrilDash()
		{
			int windup = Unraveled ? 30 : 40;
			int dashTime = 26;
			int brake = 18;
			int dashes = Unraveled ? 4 : 3;
			float speed = Unraveled ? 34f : 30f;

			if (Timer < windup) {
				// hover back, aim, telegraph
				Vector2 away = Target.Center.SafeDirectionTo(NPC.Center) * 360f;
				FlyTowards(Target.Center + away, 8f, 12f);
				NPC.velocity *= 0.93f;
				if (Server && (Timer == 0 || Timer < windup - 10)) {
					dashDirection = NPC.Center.SafeDirectionTo(Target.Center + Target.velocity * 14f);
					if (Timer % 6 == 0)
						NPC.netUpdate = true;
				}
				tremble = 2.5f * Timer / windup;
				eyeFlare = 1f;
				telegraphLine = ReUtils.InverseLerp(0, windup * 0.6f, Timer);
				mawTarget = 0.6f;
				foreach (ReTentacle t in tentacles) t.Flare = MathHelper.Lerp(t.Flare, 1f, 0.15f);
				if (Timer == 0 && SubCounter == 0 && !Main.dedServ)
					SoundEngine.PlaySound(ReSounds.Charge with { Pitch = 0.2f }, NPC.Center);
			}
			else if (Timer < windup + dashTime) {
				if (Timer == windup) {
					NPC.velocity = dashDirection * speed;
					if (!Main.dedServ) {
						SoundEngine.PlaySound(ReSounds.Dash, NPC.Center);
						ScreenFX.Shake(7f);
						ParticleSystem.Ring(NPC.Center, ReUtils.Gold, 0.15f, 1.12f, 20);
						for (int i = 0; i < 12; i++)
							ParticleSystem.Smoke(NPC.Center, -dashDirection.RotatedByRandom(0.6f) * Main.rand.NextFloat(2f, 6f), Color.Black, 1.3f, 40);
					}
					NPC.netUpdate = true;
				}
				mawTarget = 1f;
				foreach (ReTentacle t in tentacles) t.Flare = MathHelper.Lerp(t.Flare, 0f, 0.2f);
				// glyph mines left in the wake
				if ((Timer - windup) % 5 == 0) {
					Vector2 perp = dashDirection.RotatedBy(MathHelper.PiOver2) * (((int)Timer / 5) % 2 == 0 ? 1 : -1);
					Shoot<GlyphBolt>(NPC.Center, perp * 0.6f, 110, GlyphBolt.ModeMine, 50 + (Timer - windup) * 0.5f);
				}
				if (!Main.dedServ) {
					ParticleSystem.Goo(NPC.Center + Main.rand.NextVector2Circular(30, 30), -NPC.velocity * 0.15f + Main.rand.NextVector2Circular(2, 2), 1.2f);
					ParticleSystem.Spark(NPC.Center + Main.rand.NextVector2Circular(40, 40), -NPC.velocity * 0.3f, ReUtils.Gold, 1f, 18);
					glowBoost = 0.7f;
				}
			}
			else if (Timer < windup + dashTime + brake) {
				NPC.velocity *= 0.86f;
			}
			else {
				SubCounter++;
				Timer = -1;
				if (SubCounter >= dashes)
					SwitchTo(AIState.Reposition);
			}
		}

		private void DoRuneCircle()
		{
			FlyTowards(Target.Center + new Vector2(0, -340f), 10f, 20f);
			foreach (ReTentacle t in tentacles) t.Flare = MathHelper.Lerp(t.Flare, 0.6f, 0.08f);
			int[] casts = Unraveled ? new[] { 15, 75, 135 } : new[] { 15, 95 };
			for (int i = 0; i < casts.Length; i++) {
				if (Timer == casts[i]) {
					int bolts = (Main.expertMode ? 14 : 11) + (Unraveled ? 4 : 0);
					float rot = i * 0.35f;
					Shoot<RuneSigil>(Target.Center, Vector2.Zero, 120, 62f / Speedup, bolts, rot);
					if (!Main.dedServ) {
						SoundEngine.PlaySound(ReSounds.Sigil, Target.Center);
						eyeFlare = 1f;
						glowBoost = 0.9f;
						ParticleSystem.Flare(NPC.Center + new Vector2(0, -60), ReUtils.Gold, 1.6f, 20);
					}
				}
			}
			mawTarget = Timer % 60 < 30 ? 0.4f : 0f;
			if (Timer >= casts[^1] + 120)
				SwitchTo(AIState.Reposition);
		}

		private void DoGooRain()
		{
			FlyTowards(Target.Center + new Vector2(MathF.Sin(Timer * 0.03f) * 260f, -400f), 11f, 18f);
			int interval = Unraveled ? 8 : 12;
			if (Timer >= 40 && Timer <= 140) {
				mawTarget = 1f;
				if ((Timer - 40) % interval == 0) {
					Vector2 vel = new(Main.rand.NextFloat(-9f, 9f) + (Target.Center.X - NPC.Center.X) * 0.008f, Main.rand.NextFloat(-10f, -6f));
					Shoot<GooOrb>(MawPosition, vel, 130);
					if (!Main.dedServ) {
						SoundEngine.PlaySound(ReSounds.GooSpit, NPC.Center);
						for (int i = 0; i < 6; i++)
							ParticleSystem.Goo(MawPosition, vel.RotatedByRandom(0.4f) * Main.rand.NextFloat(0.3f, 0.8f), 1f);
						NPC.velocity.Y -= 1.5f;
					}
				}
			}
			if (Timer >= 190)
				SwitchTo(AIState.Reposition);
		}

		private void DoLanceStorm()
		{
			FlyTowards(Target.Center + new Vector2(sideSign * 520f, -220f), 7f, 24f);
			foreach (ReTentacle t in tentacles) t.Flare = MathHelper.Lerp(t.Flare, 0.9f, 0.08f);
			glowBoost = MathF.Max(glowBoost, 0.3f);
			int[] volleys = { 30, 95, 160 };
			for (int v = 0; v < volleys.Length; v++) {
				if (Timer != volleys[v])
					continue;
				bool cross = Unraveled && v == volleys.Length - 1;
				int count = cross ? 8 : (Unraveled ? 7 : 5);
				float baseRot = Main.rand.NextFloat(MathHelper.TwoPi);
				for (int i = 0; i < count; i++) {
					float a = cross ? MathHelper.PiOver4 * i : baseRot + MathHelper.TwoPi * i / count + Main.rand.NextFloat(-0.15f, 0.15f);
					Vector2 start = Target.Center + a.ToRotationVector2() * 900f;
					Vector2 dir = start.SafeDirectionTo(Target.Center);
					Shoot<GlyphLance>(start, dir, 150, 50f + i * 2f, Unraveled ? 30f : 26f);
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(ReSounds.LanceTelegraph, Target.Center);
					eyeFlare = 1f;
				}
			}
			mawTarget = Timer % 65 > 25 && Timer % 65 < 50 ? 0.7f : 0f;
			if (Timer >= 240)
				SwitchTo(AIState.Reposition);
		}

		private void DoUnravel()
		{
			NPC.velocity *= 0.9f;
			NPC.dontTakeDamage = true;
			mawTarget = Timer > 15 ? 1f : 0f;
			tremble = Timer < 90 ? 4f : 0f;
			foreach (ReTentacle t in tentacles) t.Flare = 1f;
			if (Timer == 20) {
				Unraveled = true;
				NPC.netUpdate = true;
				if (!Main.dedServ) {
					SoundEngine.PlaySound(ReSounds.Awaken with { Pitch = -0.25f }, NPC.Center);
					ScreenFX.Shake(22f);
					ScreenFX.ZoomPunch(0.12f);
					for (int i = 0; i < 4; i++)
						ParticleSystem.Ring(NPC.Center, i % 2 == 0 ? ReUtils.Gold : ReUtils.Ember, 0.1f + i * 0.05f, 1.08f + i * 0.015f, 40 + i * 8);
					ParticleSystem.GlyphBurst(NPC.Center, 80, 18f, 1.4f);
					for (int i = 0; i < 30; i++)
						ParticleSystem.Smoke(NPC.Center, Main.rand.NextVector2Circular(9f, 9f), Color.Black, 2f, 70);
				}
			}
			if (Timer > 20 && Timer < 90 && !Main.dedServ) {
				eyeFlare = 1f;
				glowBoost = 1f;
				if (Timer % 3 == 0)
					ParticleSystem.Glyph(NPC.Center + Main.rand.NextVector2Circular(50, 60), Main.rand.NextVector2Circular(6, 6), ReUtils.Ember, 1.1f, 40);
			}
			if (Timer >= 110) {
				NPC.dontTakeDamage = false;
				SwitchTo(AIState.Reposition);
			}
		}

		private void DoDying()
		{
			NPC.dontTakeDamage = true;
			NPC.life = Math.Max(NPC.life, 1);
			NPC.velocity *= 0.9f;
			mawTarget = Timer > 30 ? 1f : 0.3f;

			if (Timer == 0) {
				// wipe the arena so nothing hits the player during the cutscene
				if (Server) {
					foreach (Projectile p in Main.ActiveProjectiles) {
						if (p.hostile && p.ModProjectile is IReProjectile)
							p.Kill();
					}
				}
				if (!Main.dedServ)
					SoundEngine.PlaySound(ReSounds.DeathBuildup, NPC.Center);
			}

			// the cutscene starts locally on every client that sees this state
			if (!Main.dedServ && !startedCutscene) {
				startedCutscene = true;
				RiddleCutscene.Begin(NPC.whoAmI);
			}

			if (!Main.dedServ && !Vanished) {
				float build = ReUtils.InverseLerp(0, 150, Timer);
				tremble = 1.5f + build * 5f;
				glowBoost = 0.5f + build * 0.8f + (Main.rand.NextBool(4) ? 0.6f : 0f);
				eyeFlare = 1f;
				if (Timer % 2 == 0)
					ParticleSystem.Smoke(NPC.Center + Main.rand.NextVector2Circular(50, 60), Main.rand.NextVector2Circular(2f, 2f) - Vector2.UnitY, Color.Black, 1.2f + build, 60);
				if (Main.rand.NextFloat() < 0.3f + build)
					ParticleSystem.Glyph(NPC.Center + Main.rand.NextVector2Circular(50, 60), Main.rand.NextVector2Circular(3f, 3f) - Vector2.UnitY * 2f, ReUtils.GoldRamp(Main.rand.NextFloat()), 1f, 50);
				if (Timer % 20 == 0)
					ScreenFX.Shake(2f + build * 6f);
			}

			// Multiplayer safety net: if no client reports back, finish anyway after 4 minutes.
			if (Main.netMode == NetmodeID.Server && Timer > 60 * 240 && !CutsceneFinished)
				FinishDeathSequence();
		}

		// =================================================================== visuals (all clients)
		private void UpdateVisuals()
		{
			if (Main.dedServ)
				return;
			mawOpen = MathHelper.Lerp(mawOpen, mawTarget, mawTarget > mawOpen ? 0.18f : 0.1f);
			glowBoost *= 0.95f;
			foreach (ReTentacle t in tentacles) {
				if (State is AIState.Reposition or AIState.GooRain)
					t.Flare = MathHelper.Lerp(t.Flare, 0f, 0.05f);
				t.Curl = MathF.Sin(visualTime * 0.013f + t.Phase) * 0.6f;
				t.Update(NPC.Center, visualTime, NPC.direction);
			}
		}
	}
}
