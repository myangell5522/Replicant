using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Twins
{
	/// <summary>
	/// What makes the sisters feel alive: tempo, the dance they move in between attacks, zipping bursts,
	/// barks, the camera leaning into their movements, and the parry — they turn some of your blows aside.
	/// </summary>
	public abstract partial class TwinBase
	{
		// ---------------------------------------------------------------- tempo
		/// <summary>Attacks run this many steps per tick. Everything quickens after the awakening.</summary>
		public float Tempo => Grief ? 1.6f : Awakened ? 1.45f : 1.15f;
		private float tempoAcc;

		/// <summary>Runs extra attack steps so the whole choreography speeds up without changing its shape.</summary>
		private void RunTempoSteps(Player p, Action<Player> step)
		{
			tempoAcc += Tempo - 1f;
			while (tempoAcc >= 1f) {
				tempoAcc -= 1f;
				if (Timer < 0)
					break;
				float before = State;
				Timer++;
				inExtraStep = true;
				step(p);
				inExtraStep = false;
				if (State != before)
					break;
			}
		}

		// ---------------------------------------------------------------- the dance
		/// <summary>
		/// Between attacks they never just hover: each circles the player on her own side of a figure-eight,
		/// sweeping high and low, and every so often zips to a new point leaving an afterimage.
		/// Deterministic from the fight clock so it stays in sync in multiplayer.
		/// </summary>
		protected void Dance(Player p, float radius = 360f)
		{
			float t = FightTicks;
			float speed = Awakened ? 0.022f : 0.016f;
			float ang = (Side < 0 ? MathHelper.Pi : 0f) + MathF.Sin(t * 0.009f + Side) * 1.1f + t * speed * Side * 0.35f;
			Vector2 target = p.Center + new Vector2(MathF.Cos(ang) * radius, -170f + MathF.Sin(ang * 2f + t * 0.013f) * 110f);
			Hover(target, 12f, 7f);
			// zip
			int period = Awakened ? 52 : 74;
			if ((FightTicks + (Side < 0 ? 0 : period / 2)) % period == 0) {
				Vector2 to = target + new Vector2(MathF.Sin(t * 0.37f) * 160f, MathF.Cos(t * 0.29f) * 60f);
				NPC.velocity = NPC.Center.SafeDirectionTo(to) * (Awakened ? 26f : 20f);
				zipTrail = 1f;
				if (!Main.dedServ) {
					SoundEngine.PlaySound(TwinSounds.Dash with { Volume = 0.35f, Pitch = 0.3f + Main.rand.NextFloat(0.2f) }, NPC.Center);
					ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.08f, 12);
					for (int i = 0; i < 6; i++)
						ParticleSystem.Glyph(NPC.Center + Main.rand.NextVector2Circular(16, 40), -NPC.velocity * 0.1f + Main.rand.NextVector2Circular(1.5f, 1.5f), Accent, 0.8f, 26);
				}
			}
		}

		private float zipTrail;

		// ---------------------------------------------------------------- the camera
		private int leanTimer, leanLength;
		private float leanStrength;

		/// <summary>Lean the local camera toward this sister for a moment (they liked this; use it on big beats).</summary>
		protected void Lean(int ticks, float strength = 0.3f)
		{
			if (Main.dedServ)
				return;
			leanTimer = leanLength = ticks;
			leanStrength = strength * 0.45f;   // a nudge, not a pull
		}

		private void UpdateLean()
		{
			if (leanTimer <= 0)
				return;
			leanTimer--;
			Player p = Main.LocalPlayer;
			float k = ReUtils.Bump(1f - leanTimer / (float)leanLength);
			if (p.Distance(NPC.Center) < 2200f)
				ScreenFX.Focus(Vector2.Lerp(p.Center, NPC.Center, 0.5f), k * leanStrength);
		}

		// ---------------------------------------------------------------- voices
		private static int lastBarkTick = -9999;

		/// <summary>A short line as an attack begins (only when nobody else is speaking).</summary>
		protected void Bark()
		{
			if (Main.dedServ || Subtitles.Busy || FightTicks - lastBarkTick < 420 || !Main.rand.NextBool(3))
				return;
			lastBarkTick = FightTicks;
			string who = this is Requiem ? "Requiem" : "Refrain";
			int pool = Grief ? 4 : 8;
			string key = Grief ? $"Mods.Replicant.Twins.Barks.Grief{who}{Main.rand.Next(pool)}" : $"Mods.Replicant.Twins.Barks.{who}{Main.rand.Next(pool)}";
			Subtitles.Push(Speaker, key, 150);
		}

		// ---------------------------------------------------------------- the parry
		private int blockCooldown;
		private float blockFlash;
		private float blockAngle;
		private int restoreHitSound;
		/// <summary>Shielded (the Aria): every blow is turned aside.</summary>
		public bool Shielded;

		private float BlockChance {
			get {
				if (Shielded)
					return 1f;
				if (State == StDying || State == StReveal || State == StIntro || Timer < 0)
					return 0f;
				if (Stunned > 0)
					return 0f;
				return Grief ? 0.26f : Awakened ? 0.2f : 0.1f;
			}
		}

		private bool TryBlock(ref NPC.HitModifiers modifiers, Vector2 from)
		{
			if (blockCooldown > 0 && !Shielded)
				return false;
			if (Main.rand.NextFloat() >= BlockChance)
				return false;
			modifiers.SetMaxDamage(1);
			modifiers.HideCombatText();
			modifiers.DisableCrit();
			modifiers.DisableKnockback();
			blockCooldown = Awakened ? 40 : 60;
			blockAngle = NPC.Center.SafeDirectionTo(from).ToRotation();
			blockFlash = 1f;
			if (NPC.HitSound.HasValue) {
				NPC.HitSound = null;
				restoreHitSound = 2;
			}
			if (!Main.dedServ) {
				Vector2 at = NPC.Center + blockAngle.ToRotationVector2() * 46f;
				SoundEngine.PlaySound(TwinSounds.Clink with { Volume = 0.8f, Pitch = Shielded ? 0.4f : -0.1f }, at);
				SoundEngine.PlaySound(TwinSounds.Bell with { Volume = 0.25f, Pitch = 0.6f }, at);
				for (int i = 0; i < 10; i++) {
					Vector2 v = (blockAngle + Main.rand.NextFloat(-0.9f, 0.9f)).ToRotationVector2() * Main.rand.NextFloat(4f, 10f);
					ParticleSystem.Spark(at, v, ReUtils.PaleGold, 0.9f, 16);
				}
				ParticleSystem.Glyph(at, blockAngle.ToRotationVector2() * 3f, Accent, 1.4f, 30);
				ParticleSystem.Flare(at, Accent, 1.4f, 12);
				if (!Shielded)
					CombatText.NewText(NPC.Hitbox, Accent, Terraria.Localization.Language.GetTextValue("Mods.Replicant.Twins.Parry"), dramatic: false, dot: true);
			}
			return true;
		}

		public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers)
		{
			TryBlock(ref modifiers, player.Center);
			ApplyVulnerability(ref modifiers);
		}

		public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
		{
			if (TryBlock(ref modifiers, projectile.Center - projectile.velocity * 3f)) {
				return;
			}
			ApplyVulnerability(ref modifiers);
		}

		// ---------------------------------------------------------------- stun / vulnerability
		/// <summary>Ticks she is open (shield shattered, hack won): she takes extra damage and cannot parry.</summary>
		public int Stunned;

		private void ApplyVulnerability(ref NPC.HitModifiers modifiers)
		{
			if (Stunned > 0)
				modifiers.FinalDamage *= 1.5f;
		}

		/// <summary>Per-tick bookkeeping for everything in this file.</summary>
		private void UpdateLife()
		{
			if (blockCooldown > 0)
				blockCooldown--;
			if (Stunned > 0)
				Stunned--;
			blockFlash *= 0.86f;
			zipTrail *= 0.9f;
			if (restoreHitSound > 0 && --restoreHitSound == 0)
				NPC.HitSound = TwinSounds.Hurt;
			if (!Main.dedServ)
				UpdateLean();
		}

		/// <summary>The barrier that flashes where a blow was turned aside.</summary>
		private void DrawBlock(SpriteBatch sb, Vector2 pos, float op)
		{
			if (blockFlash < 0.03f)
				return;
			Texture2D shield = ReTex.BlockShield;
			Vector2 at = pos + blockAngle.ToRotationVector2() * 40f;
			float s = 0.9f + (1f - blockFlash) * 0.25f;
			sb.Draw(shield, at, null, Accent.Additive(0.9f * blockFlash * op), blockAngle, shield.Size() / 2f, s, SpriteEffects.None, 0f);
			sb.Draw(shield, at, null, Color.White.Additive(0.5f * blockFlash * blockFlash * op), blockAngle, shield.Size() / 2f, s * 0.96f, SpriteEffects.None, 0f);
			FX.Bloom(sb, at, Accent, 70f, 0.35f * blockFlash * op, 0.5f, 1.4f, blockAngle);
		}
	}
}
