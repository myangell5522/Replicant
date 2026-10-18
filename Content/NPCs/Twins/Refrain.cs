using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Twins
{
	/// <summary>
	/// Refrain — the younger sister. Short bob, lyre staff. Fast and close: dashes that echo, a musical score
	/// written across the arena, mirrored canons, and a glissando that sheds petals. Her spheres are mostly
	/// gold — the kind you can cut.
	/// </summary>
	[AutoloadBossHead]
	public class Refrain : TwinBase
	{
		public const int StEchoDash = 20, StScore = 21, StCanon = 22, StGlissando = 23;

		public override string Texture => "Replicant/Assets/Textures/NPCs/Twins/Refrain";
		public override string BossHeadTexture => "Replicant/Assets/Textures/NPCs/Twins/Refrain_Head_Boss";
		public override int Speaker => Subtitles.Refrain;
		public override int Side => 1;
		public override Color Accent => new(255, 214, 120);
		public override Texture2D Sheet => ReTex.RefrainSheet;
		public override Texture2D GlowSheet => ReTex.RefrainGlow;

		// client-side visual for the score
		private Vector2 staffOrigin;
		private float staffAlpha;
		private int staffDir = 1;

		protected override int ChooseAttack()
		{
			// the intrusion: once she has woken, she reaches into the player (single player only)
			if (Awakened && !Grief && Main.netMode == Terraria.ID.NetmodeID.SinglePlayer && FightTicks > TwinsTimeline.RevealEnd + 2400 && FightTicks - TwinsFight.LastHack > 4200) {
				TwinsFight.LastHack = FightTicks;
				return StHack;
			}
			return FromBag(StEchoDash, StScore, StCanon, StGlissando);
		}

		protected override void RunAttack(Player p)
		{
			switch ((int)State) {
				case StEchoDash: EchoDash(p); break;
				case StScore: Score(p); break;
				case StCanon: Canon(p); break;
				case StGlissando: Glissando(p); break;
				default: Enter(StIdle); break;
			}
		}

		/// <summary>Dashes through the player; each one is replayed a moment later by her echo.</summary>
		private void EchoDash(Player p)
		{
			int dashes = Grief ? 5 : Awakened ? 4 : 3;
			int cycle = Grief ? 48 : Awakened ? 56 : 70;
			const int windUp = 30, dashLen = 18;
			int local = (int)Timer % cycle;
			if (Sub >= dashes) {
				NPC.velocity *= 0.9f;
				if (Timer >= dashes * cycle + 30)
					Enter(StIdle);
				return;
			}
			if (local < windUp) {
				Vector2 spot = p.Center + new Vector2((p.Center.X > NPC.Center.X ? -1f : 1f) * 330f, -60f);
				Hover(spot, 14f, 8f);
				SetPose(TwinPose.Cast);
				if (Server && local == windUp - 8) {
					// lead the target a little
					aim = NPC.Center.SafeDirectionTo(p.Center + p.velocity * 14f);
					NPC.netUpdate = true;
				}
				if (local >= windUp - 8) {
					telegraph = 1f;
					telegraphDir = aim;
				}
			}
			else if (local == windUp) {
				NPC.velocity = aim * (Awakened ? 30f : 26f) * Tempo;
				SetPose(TwinPose.Dash);
				if (Server) {
					Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, NPC.velocity, ModContent.ProjectileType<EchoGhost>(), ReUtils.ProjDamage(Awakened ? 120 : 100), 0f, Main.myPlayer, 42, dashLen);
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(TwinSounds.Dash, NPC.Center);
					ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.12f, 18);
					ScreenFX.Shake(4f);
				}
			}
			else if (local < windUp + dashLen) {
				SetPose(TwinPose.Dash);
				if (local % 4 == 0)
					Orb(NPC.Center, NPC.velocity.RotatedBy(MathHelper.PiOver2).SafeNormalize(Vector2.UnitY) * 1.5f, SongOrb.Gold, SongOrb.Delayed, 50);
			}
			else {
				NPC.velocity *= 0.86f;
				SetPose(TwinPose.Dash);
				if (local == cycle - 1)
					Sub++;
			}
		}

		/// <summary>She writes a score across the arena: notes hang on five staff lines, then are played at you in order.</summary>
		/// <summary>
		/// The Score: she plants a music stand and makes you play it (see RhythmStand). In multiplayer, where the
		/// duel can't be shared, she writes the old score of notes across the arena instead.
		/// </summary>
		private void Score(Player p)
		{
			if (Timer <= 1) {
				bool nearReveal = FightTicks > TwinsTimeline.RevealStart - 1200 && FightTicks < TwinsTimeline.RevealEnd;
				Sub = Main.netMode != Terraria.ID.NetmodeID.SinglePlayer || nearReveal ? 1 : 0;
			}
			if (Sub == 1) {
				ScoreClassic(p);
				return;
			}
			SetPose(Timer < 30 ? TwinPose.Cast : TwinPose.Sing);
			if (Timer < 30)
				Hover(p.Center + new Vector2(Side * 260f, -140f), 14f, 8f);
			else
				Dance(p, 440f);
			if (Timer == 24) {
				Vector2 at = FindGround(p.Center + new Vector2(-Side * 200f, -60f)) - new Vector2(0, 76f);
				Projectile.NewProjectile(NPC.GetSource_FromAI(), at, Vector2.Zero, ModContent.ProjectileType<MusicStand>(), 0, 0f, Main.myPlayer);
				int rows = Grief || Awakened ? Main.rand.Next(2, 4) : Main.rand.Next(1, 3);
				int notes = Main.rand.Next(4, Awakened ? 8 : 6);
				var version = Common.Cutscene.RhythmStand.Version.Plain;
				if (Awakened) {
					int r = Main.rand.Next(3);
					version = r == 1 ? Common.Cutscene.RhythmStand.Version.Inverse : r == 2 ? Common.Cutscene.RhythmStand.Version.Fading : version;
				}
				if (!Main.dedServ) {
					Common.Cutscene.RhythmStand.Begin(at, rows, notes, version, NPC.whoAmI);
					SoundEngine.PlaySound(TwinSounds.Cast, NPC.Center);
				}
				Lean(70, 0.35f);
			}
			if ((Timer > 40 && !Common.Cutscene.RhythmStand.Active) || Timer > 3000)
				Enter(StIdle);
		}

		private void ScoreClassic(Player p)
		{
			Hover(HomeSpot(p), 9f);
			SetPose(Timer < 60 ? TwinPose.Cast : TwinPose.Sing);
			int notes = Grief ? 18 : Awakened ? 16 : 13;
			if (Timer == 20) {
				staffOrigin = p.Center;
				staffDir = NPC.Center.X > p.Center.X ? -1 : 1;
				staffAlpha = 0f;
				if (Server) {
					for (int j = 0; j < notes; j++) {
						float x = staffOrigin.X - staffDir * 640f + staffDir * j * (1280f / notes);
						int line = (int)(ReUtils.Hash01(j, FightTicks) * 5f);
						float y = staffOrigin.Y - 240f + line * 34f;
						bool dark = Awakened && j % 3 == 2;
						Orb(new Vector2(x, y), Vector2.Zero, dark ? SongOrb.Dark : SongOrb.Gold, SongOrb.Delayed, 70 + j * (Awakened ? 6 : 8));
					}
				}
				if (!Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Cast, NPC.Center);
			}
			if (Timer >= 20 + 70 + notes * 8 + 60)
				Enter(StIdle);
		}

		/// <summary>A canon: she sings a phrase of gold, and the phrase answers itself in black from the mirror side.</summary>
		private void Canon(Player p)
		{
			Hover(HomeSpot(p), 9f);
			SetPose(TwinPose.Cast);
			int phrases = Awakened ? 6 : 5;
			int gap = Grief ? 16 : 20;
			int echoDelay = 30;
			TwinBase other = Sister;
			for (int k = 0; k < phrases; k++) {
				int at = 30 + k * gap;
				Vector2 d = StaffTip.SafeDirectionTo(p.Center);
				if (Timer == at) {
					for (int i = -2; i <= 2; i++)
						Orb(StaffTip, d.RotatedBy(i * 0.16f) * 7f, SongOrb.Gold, SongOrb.Straight, 1.005f);
					if (!Main.dedServ)
						SoundEngine.PlaySound(TwinSounds.Note(k), NPC.Center);
				}
				if (Timer == at + echoDelay) {
					// the answer comes from her sister, or from her own reflection across the player
					Vector2 from = other != null && other.State != StDying ? other.NPC.Center : p.Center * 2f - NPC.Center;
					Vector2 e = from.SafeDirectionTo(p.Center);
					for (int i = -2; i <= 2; i++)
						Orb(from, e.RotatedBy(-i * 0.16f) * 6f, SongOrb.Dark, SongOrb.Straight, 1.005f);
					if (!Main.dedServ) {
						SoundEngine.PlaySound(TwinSounds.Note(k + 2) with { Pitch = -0.4f }, from);
						ParticleSystem.Flare(from, ReUtils.Ember, 1f, 14);
					}
				}
			}
			if (Timer >= 30 + phrases * gap + echoDelay + 50)
				Enter(StIdle);
		}

		/// <summary>A glissando: she runs the length of the sky above you, petals falling from every step.</summary>
		private void Glissando(Player p)
		{
			int passes = Awakened ? 2 : 1;
			const int setup = 40, run = 64, rest = 20;
			int cycle = setup + run + rest;
			int local = (int)Timer % cycle;
			if (Sub >= passes) {
				NPC.velocity *= 0.9f;
				Enter(StIdle);
				return;
			}
			int dir = Sub % 2 == 0 ? Side * -1 : Side;   // first pass runs away from her side
			float height = Sub % 2 == 0 ? -280f : -200f;
			if (local < setup) {
				Hover(p.Center + new Vector2(-dir * 760f, height), 22f, 6f);
				SetPose(TwinPose.Cast);
			}
			else if (local < setup + run) {
				SetPose(TwinPose.Dash);
				NPC.velocity = new Vector2(dir * (Awakened ? 25f : 22f) * Tempo, MathF.Sin((local - setup) * 0.2f) * 2f);
				if (local % (Grief ? 4 : 7) == 0)
					Orb(NPC.Center, new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(-3f, -1f)), SongOrb.Petal, SongOrb.Homing, 0f, ReUtils.ProjDamage(80));
				if (!Main.dedServ && local % 6 == 0)
					SoundEngine.PlaySound(TwinSounds.Note((local - setup) / 6) with { Pitch = -0.5f + (local - setup) / (float)run, Volume = 0.35f }, NPC.Center);
			}
			else {
				NPC.velocity *= 0.85f;
				if (local == cycle - 1)
					Sub++;
			}
		}

		public override void PostAI()
		{
			if (Main.dedServ)
				return;
			bool scoring = State == StScore && Timer >= 20;
			staffAlpha = MathHelper.Lerp(staffAlpha, scoring ? 1f : 0f, 0.08f);
		}

		protected override void DrawExtras(SpriteBatch sb, Vector2 pos, Vector2 screenPos)
		{
			if (staffAlpha < 0.02f)
				return;
			// five staff lines written across the arena
			Vector2 o = staffOrigin - screenPos;
			float reveal = ReUtils.EaseOutCubic(ReUtils.InverseLerp(20, 60, State == StScore ? Timer : 999));
			for (int i = 0; i < 5; i++) {
				Vector2 start = o + new Vector2(-staffDir * 700f, -240f + i * 34f);
				float rot = staffDir > 0 ? 0f : MathHelper.Pi;
				FX.Beam(sb, start, rot, 1400f * reveal, 3f, Accent, 0.45f * staffAlpha, ReTex.Thread);
			}
			// a clef where the score begins
			Vector2 clef = o + new Vector2(-staffDir * 690f, -172f);
			sb.Draw(ReTex.GlyphAtlasGlow, clef, ReTex.GlyphFrame(23), ReUtils.PaleGold.Additive(0.9f * staffAlpha), 0f, new Vector2(7, 9), 4f, SpriteEffects.None, 0f);
			FX.Bloom(sb, clef, Accent, 60f, 0.3f * staffAlpha);
		}
	}
}
