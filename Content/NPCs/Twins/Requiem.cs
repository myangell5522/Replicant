using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Twins
{
	/// <summary>
	/// Requiem — the elder sister. Long hair, bell staff. She controls space: spirals, bells, lines of verse,
	/// and the black rain. Most of her spheres are the dark, unbreakable kind.
	/// </summary>
	[AutoloadBossHead]
	public class Requiem : TwinBase
	{
		public const int StLullaby = 10, StBells = 11, StVerse = 12, StOilRain = 13;

		public override string Texture => "Replicant/Assets/Textures/NPCs/Twins/Requiem";
		public override string BossHeadTexture => "Replicant/Assets/Textures/NPCs/Twins/Requiem_Head_Boss";
		public override int Speaker => Subtitles.Requiem;
		public override int Side => -1;
		public override Color Accent => new(255, 150, 60);
		public override Texture2D Sheet => ReTex.RequiemSheet;
		public override Texture2D GlowSheet => ReTex.RequiemGlow;

		protected override int ChooseAttack() => FromBag(StLullaby, StBells, StVerse, StOilRain);

		protected override void RunAttack(Player p)
		{
			switch ((int)State) {
				case StLullaby: Lullaby(p); break;
				case StBells: Bells(p); break;
				case StVerse: Verse(p); break;
				case StOilRain: OilRain(p); break;
				default: Enter(StIdle); break;
			}
		}

		/// <summary>A lullaby spiral: arms of spheres unwinding from her staff, gold and dark interleaved.</summary>
		private void Lullaby(Player p)
		{
			Hover(p.Center + new Vector2(Side * 120f, -300f), 9f);
			SetPose(Timer < 30 ? TwinPose.Cast : TwinPose.Sing);
			int arms = Awakened ? 3 : 2;
			int every = Grief ? 3 : Awakened ? 4 : 5;
			int length = Awakened ? 230 : 200;
			if (Timer >= 40 && Timer < length && Timer % every == 0) {
				float spin = Timer < length / 2 || !Awakened ? 1f : -1f;
				float baseAng = Timer * 0.065f * spin;
				for (int i = 0; i < arms; i++) {
					float a = baseAng + MathHelper.TwoPi * i / arms;
					bool dark = (Timer / every) % 3 == 0;
					Orb(NPC.Center, a.ToRotationVector2() * 3.6f, dark ? SongOrb.Dark : SongOrb.Gold, SongOrb.Straight, 1.008f);
				}
				if (!Main.dedServ && Timer % (every * 3) == 0)
					SoundEngine.PlaySound(TwinSounds.Note((int)(Timer / every / 3)) with { Volume = 0.3f, Pitch = -0.2f }, NPC.Center);
			}
			if (!Main.dedServ && Timer == 40) {
				SoundEngine.PlaySound(TwinSounds.Cast, NPC.Center);
				ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.1f, 24);
			}
			if (Timer >= length + 30)
				Enter(StIdle);
		}

		/// <summary>Bells hung in the air around the player: each tolls a ring of spheres with one way out.</summary>
		private void Bells(Player p)
		{
			Hover(HomeSpot(p), 8f);
			SetPose(TwinPose.Cast);
			int count = Grief ? 6 : Awakened ? 5 : 3;
			int spacing = Awakened ? 30 : 40;
			if (Timer >= 30 && (Timer - 30) % spacing == 0 && Sub < count) {
				Sub++;
				if (Server) {
					Vector2 at = p.Center + Main.rand.NextVector2CircularEdge(1f, 1f) * Main.rand.NextFloat(220f, 330f);
					Projectile.NewProjectile(NPC.GetSource_FromAI(), at, Vector2.Zero, ModContent.ProjectileType<BellSigil>(), OrbDamage, 0f, Main.myPlayer, Awakened ? 52 : 64, Awakened ? 18 : 14);
				}
				if (!Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Cast with { Pitch = -0.3f, Volume = 0.4f }, NPC.Center);
			}
			if (Timer >= 30 + spacing * count + 100)
				Enter(StIdle);
		}

		/// <summary>Lines of verse cut the arena; they burn, then slowly turn.</summary>
		private void Verse(Player p)
		{
			Hover(HomeSpot(p) + new Vector2(0, -60f), 7f);
			SetPose(Timer < 20 ? TwinPose.Cast : TwinPose.Sing);
			int waves = Awakened ? 2 : 1;
			int waveGap = 110;
			if (Timer >= 20 && (Timer - 20) % waveGap == 0 && Sub < waves) {
				Sub++;
				int n = Grief ? 7 : Awakened ? 6 : 4;
				float baseAng = Main.rand.NextFloat(MathHelper.TwoPi);
				if (Server) {
					for (int i = 0; i < n; i++) {
						float a = baseAng + MathHelper.Pi * i / n;
						// each line passes beside the player, never straight through, so there is always a lane
						Vector2 dir = a.ToRotationVector2();
						Vector2 normal = dir.RotatedBy(MathHelper.PiOver2);
						Vector2 through = p.Center + normal * ((i % 2 == 0 ? 1f : -1f) * Main.rand.NextFloat(60f, 200f));
						Vector2 start = through - dir * (VerseChain.Length / 2f);
						float spin = (i % 2 == 0 ? 1f : -1f) * (Awakened ? 0.0035f : 0.002f);
						Projectile.NewProjectile(NPC.GetSource_FromAI(), start, dir, ModContent.ProjectileType<VerseChain>(), ReUtils.ProjDamage(Awakened ? 125 : 105), 0f, Main.myPlayer, 70 + i * 4, 90, spin);
					}
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(TwinSounds.Chain, NPC.Center);
					glowBoostPulse();
				}
			}
			if (Timer >= 20 + waveGap * waves + 120)
				Enter(StIdle);
		}

		/// <summary>The black rain: oil falls from a sky that is not there; a few drops still shine.</summary>
		private void OilRain(Player p)
		{
			Hover(p.Center + new Vector2(MathF.Sin(Timer * 0.02f) * 260f, -380f), 8f);
			SetPose(TwinPose.Sing);
			int every = Grief ? 3 : Awakened ? 4 : 6;
			if (Timer >= 30 && Timer < 220 && Timer % every == 0) {
				float wind = MathF.Sin(Timer * 0.015f) * 1.6f;
				Vector2 at = new(p.Center.X + Main.rand.NextFloat(-950f, 950f), p.Center.Y - 640f);
				bool gold = Main.rand.NextBool(4);
				Orb(at, new Vector2(wind, Main.rand.NextFloat(3.5f, 5f)), gold ? SongOrb.Gold : SongOrb.Dark, SongOrb.Straight, 1.012f);
				if (Awakened && Timer % (every * 10) == 0) {
					// a few drops remember where you are
					Orb(at + new Vector2(0, 40), Vector2.UnitY * 2f, SongOrb.Dark, SongOrb.Delayed, 40);
				}
			}
			if (!Main.dedServ && Timer % 40 == 0 && Timer < 220)
				SoundEngine.PlaySound(TwinSounds.Note((int)Timer / 40) with { Pitch = -0.6f, Volume = 0.3f }, NPC.Center);
			if (Timer >= 260)
				Enter(StIdle);
		}

		private void glowBoostPulse() => ParticleSystem.Ring(StaffTip, Accent, 0.05f, 1.08f, 20);
	}
}
