using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles;
using Replicant.Content.Projectiles.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Twins
{
	public enum TwinPose { Idle, Cast, Dash, Sing, Hurt }

	/// <summary>
	/// Shared body of the two sisters: the fight clock, the false fight (they cannot fall before 2:00),
	/// the revelation and rewind, the awakening, their deaths, movement helpers and all drawing.
	/// Attacks live in Requiem.cs / Refrain.cs.
	/// </summary>
	public abstract class TwinBase : ModNPC
	{
		// ---------------------------------------------------------------- states
		public const int StIntro = 0, StIdle = 1, StHarass = 2, StBlink = 3;
		/// <summary>Ticks a sister spends unveiling a movement the first time she sings it.</summary>
		public const int AnnounceTicks = 96;
		public const int StDuetBeam = 30, StLyricWall = 31, StLilyStorm = 32;
		public const int StReveal = 40, StDying = 50;

		public const int FrameW = 144, FrameH = 200, FrameCount = 24;
		public static readonly Vector2 Origin = new(72, 104);

		public ref float State => ref NPC.ai[0];
		public ref float Timer => ref NPC.ai[1];
		public ref float Sub => ref NPC.ai[2];
		/// <summary>Ticks since the twins appeared (the fight clock everything is keyed to).</summary>
		public int FightTicks => (int)NPC.ai[3];

		public bool Awakened;
		/// <summary>Her sister has fallen: she fights alone, faster and without mercy.</summary>
		public bool Grief;
		public bool DeathDone;
		/// <summary>Whether this is the last sister standing (decided as she starts to die).</summary>
		public bool IsLast;
		public Vector2 Anchor;   // shared point for duo attacks / reveal
		protected Vector2 aim = Vector2.UnitX;

		// ---------------------------------------------------------------- per-twin identity
		public abstract int Speaker { get; }
		/// <summary>-1 = prefers the player's left, +1 = right.</summary>
		public abstract int Side { get; }
		public abstract Color Accent { get; }
		public abstract Texture2D Sheet { get; }
		public abstract Texture2D GlowSheet { get; }
		protected abstract int ChooseAttack();
		protected abstract void RunAttack(Player target);
		
		// ---------------------------------------------------------------- visual state (client)
		public TwinPose Pose;
		private TwinPose lastPose;
		private int poseTime;
		private float glitch;          // 0..1 slice/tear amount
		private float hurtFlash;
		private float rewind;          // 0..1 during the reveal heal
		private float haloAlpha;
		private float glowBoost;
		protected float telegraph;     // generic line telegraph opacity (dashes)
		protected Vector2 telegraphDir;
		private float visualTime;
		/// <summary>The life the boss bar shows: in the false fight it keeps falling after the real life stops.</summary>
		public float ShownLife = -1f;
		private int barLieTimer;

		protected readonly List<int> bag = new();

		public float Fury => Grief ? 1.55f : Awakened ? 1.3f : 1f;
		public bool InFalseFight => FightTicks < TwinsTimeline.RevealStart;
		public float LifeFloor => NPC.lifeMax * TwinsTimeline.FalseLifeFloor;

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = FrameCount;
			NPCID.Sets.MPAllowedEnemies[Type] = true;
			NPCID.Sets.BossBestiaryPriority.Add(Type);
			NPCID.Sets.TrailCacheLength[Type] = 10;
			NPCID.Sets.TrailingMode[Type] = 3;
			NPCID.Sets.ImmuneToRegularBuffs[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers {
				PortraitScale = 0.9f,
				PortraitPositionYOverride = 0f,
			});
		}

		public override void SetDefaults()
		{
			NPC.width = 44;
			NPC.height = 128;
			NPC.lifeMax = 58000;
			NPC.damage = 120;
			NPC.defense = 46;
			NPC.knockBackResist = 0f;
			NPC.noGravity = true;
			NPC.noTileCollide = true;
			NPC.boss = true;
			NPC.npcSlots = 10f;
			NPC.aiStyle = -1;
			NPC.lavaImmune = true;
			NPC.value = Item.buyPrice(gold: 25);
			NPC.HitSound = TwinSounds.Hurt;
			NPC.DeathSound = null;
			NPC.Opacity = 0f;
			NPC.dontTakeDamage = true;
			NPC.BossBar = this is Requiem ? ModContent.GetInstance<RequiemBossBar>() : ModContent.GetInstance<RefrainBossBar>();
			if (!Main.dedServ)
				Music = MusicLoader.GetMusicSlot(Mod, "Assets/Music/SongOfTheAncients");
		}

		public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
		{
			NPC.lifeMax = (int)(NPC.lifeMax * 0.8f * balance * bossAdjustment);
			NPC.damage = (int)(NPC.damage * 0.8f);
		}

		public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
		{
			bestiaryEntry.Info.AddRange(new List<IBestiaryInfoElement> {
				new MoonLordPortraitBackgroundProviderBestiaryInfoElement(),
				new FlavorTextBestiaryInfoElement("Mods.Replicant.Bestiary." + Name),
			});
		}

		public override bool CheckActive() => false;

		public override bool CanHitPlayer(Player target, ref int cooldownSlot)
		{
			cooldownSlot = ImmunityCooldownID.Bosses;
			return Pose == TwinPose.Dash && NPC.Opacity > 0.9f && State != StDying && State != StReveal;
		}

		public static TwinBase OtherTwin(NPC self)
		{
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.whoAmI != self.whoAmI && n.ModNPC is TwinBase t)
					return t;
			}
			return null;
		}

		public TwinBase Sister => OtherTwin(NPC);

		// ================================================================ damage rules
		public override void HitEffect(NPC.HitInfo hit)
		{
			if (FightTicks < TwinsTimeline.RevealEnd && NPC.life < LifeFloor) {
				// the wound doesn't take
				NPC.life = (int)LifeFloor;
				glitch = MathF.Max(glitch, 0.55f);
			}
			if (Main.dedServ)
				return;
			hurtFlash = 1f;
			glowBoost = MathF.Max(glowBoost, 0.4f);
			if (ShownLife < 0f)
				ShownLife = NPC.life;
			if (InFalseFight)
				ShownLife = MathF.Max(0f, ShownLife - hit.Damage);
			int n = Math.Clamp(hit.Damage / 500, 1, 5);
			for (int i = 0; i < n; i++) {
				Vector2 p = NPC.Center + Main.rand.NextVector2Circular(14, 40);
				ParticleSystem.Glyph(p, Main.rand.NextVector2Circular(2.5f, 2.5f) - Vector2.UnitY, Accent, 0.8f, 30);
				ParticleSystem.Spark(p, Main.rand.NextVector2Circular(5f, 5f), ReUtils.PaleGold, 0.6f, 14);
			}
		}

		public override bool CheckDead()
		{
			if (DeathDone)
				return true;
			if (FightTicks < TwinsTimeline.RevealEnd) {
				NPC.life = Math.Max(1, (int)LifeFloor);
				NPC.active = true;
				return false;
			}
			if (State != StDying)
				BeginDying();
			NPC.life = 1;
			NPC.active = true;
			NPC.dontTakeDamage = true;
			return false;
		}

		private void BeginDying()
		{
			TwinBase other = Sister;
			IsLast = other == null || other.State == StDying;
			State = StDying;
			Timer = 0;
			Sub = 0;
			NPC.velocity *= 0.2f;
			NPC.netUpdate = true;
			if (other != null && other.State != StDying) {
				other.Grief = true;
				other.State = StIdle;
				other.Timer = -100;   // she stops to look
				other.NPC.netUpdate = true;
			}
		}

		public override void OnKill()
		{
			// only the very last sister ends the fight
			if (Sister != null)
				return;
			// the last sister has fallen
			if (DimensionBridge.InDimension) {
				TwinsFight.OnBothDefeated();
			}
			else {
				NPC.SetEventFlagCleared(ref ReDownedSystem.DownedTwins, -1);
				ReDownedSystem.DownedRe = true;
				Item.NewItem(NPC.GetSource_Loot(), NPC.Hitbox, ItemID.GreaterHealingPotion, 10);
				Item.NewItem(NPC.GetSource_Loot(), NPC.Hitbox, ItemID.PlatinumCoin, 2);
				if (Main.netMode == NetmodeID.Server)
					NetMessage.SendData(MessageID.WorldData);
			}
		}

		public override void SendExtraAI(BinaryWriter writer)
		{
			writer.Write(Awakened);
			writer.Write(Grief);
			writer.Write(DeathDone);
			writer.Write(IsLast);
			writer.WriteVector2(Anchor);
			writer.WriteVector2(aim);
		}

		public override void ReceiveExtraAI(BinaryReader reader)
		{
			Awakened = reader.ReadBoolean();
			Grief = reader.ReadBoolean();
			DeathDone = reader.ReadBoolean();
			IsLast = reader.ReadBoolean();
			Anchor = reader.ReadVector2();
			aim = reader.ReadVector2();
		}

		// ================================================================ AI
		protected Player Target => Main.player[NPC.target];
		protected bool Server => Main.netMode != NetmodeID.MultiplayerClient;

		public override void AI()
		{
			NPC.ai[3]++;
			int t = FightTicks;
			visualTime++;
			if (NPC.target < 0 || NPC.target == 255 || Target.dead || !Target.active)
				NPC.TargetClosest(false);
			Player p = Target;

			if (State != StDying && (p.dead || !p.active)) {
				// nobody left to sing to
				NPC.velocity.Y -= 0.3f;
				NPC.Opacity -= 0.01f;
				if (NPC.Opacity <= 0f)
					NPC.active = false;
				return;
			}

			// the story clock overrides everything
			if (State != StDying && t >= TwinsTimeline.RevealStart && t < TwinsTimeline.RevealEnd && State != StReveal)
				Enter(StReveal);
			if (State != StDying && State != StIntro && State != StReveal)
				NPC.dontTakeDamage = false;
			if (State != StIntro && State != StBlink && State != StDying && NPC.Opacity < 1f)
				NPC.Opacity = MathF.Min(1f, NPC.Opacity + 0.08f);

			SetPose(TwinPose.Idle);
			telegraph *= 0.85f;
			switch ((int)State) {
				case StIntro: DoIntro(p); break;
				case StIdle: DoIdle(p); break;
				case StHarass: DoHarass(p); break;
				case StBlink: DoBlink(p); break;
				case StReveal: DoReveal(p); break;
				case StDying: DoDying(p); break;
				default:
					if (Timer == 0 && !Main.dedServ) {
						// every movement begins with a beat: a ring and a flare of her colour
						ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.1f, 18);
						ParticleSystem.Flare(StaffTip, Accent, 1.4f, 16);
						glowBoost = MathF.Max(glowBoost, 0.8f);
					}
					if (Timer < 0)
						DoAnnounce(p);
					else if (State >= StDuetBeam && State < StReveal)
						RunDuo(p);
					else
						RunAttack(p);
					break;
			}
			Timer++;

			if (Pose != TwinPose.Dash)
				NPC.spriteDirection = p.Center.X > NPC.Center.X ? 1 : -1;
			else if (Math.Abs(NPC.velocity.X) > 0.5f)
				NPC.spriteDirection = Math.Sign(NPC.velocity.X);

			Lighting.AddLight(NPC.Center, Accent.ToVector3() * (0.6f + (Awakened ? 0.4f : 0f)));
			if (!Main.dedServ)
				UpdateVisuals();
		}

		protected void Enter(int state)
		{
			State = state;
			Timer = 0;
			Sub = 0;
			NPC.netUpdate = true;
			// a movement she has never sung in this fight (or not since the awakening) is unveiled first
			if (state >= 10 && state < StReveal && TwinsFight.Revealed.Add(state))
				Timer = -AnnounceTicks;
		}

		public static string AttackKey(int state) => state switch {
			10 => "Lullaby", 11 => "Bells", 12 => "Verse", 13 => "OilRain",
			20 => "EchoDash", 21 => "Score", 22 => "Canon", 23 => "Glissando",
			StDuetBeam => "DuetBeam", StLyricWall => "LyricWall", StLilyStorm => "LilyStorm",
			_ => null,
		};

		/// <summary>The unveiling: she rises, gathers glyphs into her staff, and the movement's name breaks the screen.</summary>
		private void DoAnnounce(Player p)
		{
			int k = (int)Timer + AnnounceTicks;            // 0 .. AnnounceTicks
			bool duo = State >= StDuetBeam;
			Vector2 spot = duo ? p.Center + new Vector2(Side * 330f, -230f) : HomeSpot(p) + new Vector2(-Side * 60f, -70f);
			Hover(spot, 14f, 10f);
			NPC.velocity *= 0.92f;
			SetPose(k < 50 ? TwinPose.Cast : TwinPose.Sing);
			NPC.dontTakeDamage = false;
			if (Main.dedServ)
				return;
			if (k == 0) {
				bool reveal = !duo || this is Requiem;
				if (reveal)
					AttackReveal.Show(duo ? Subtitles.Both : Speaker, AttackKey((int)State), NPC.Center, reprise: Awakened);
				glowBoost = 1.2f;
				ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.13f, 30);
				ParticleSystem.Flare(StaffTip, Accent, 2.2f, 30);
			}
			// glyphs drawn in from the air into the staff
			if (k < AnnounceTicks - 14 && k % 2 == 0) {
				Vector2 from = StaffTip + Main.rand.NextVector2CircularEdge(260f, 260f);
				ParticleSystem.Glyph(from, (StaffTip - from) / 20f, Accent, Main.rand.NextFloat(0.8f, 1.3f), 20).Drag = 1f;
			}
			if (k % 18 == 0)
				ParticleSystem.Ring(StaffTip, Accent, 0.9f, 0.9f, 20);
			// the camera leans toward her while she sings the name
			float lean = ReUtils.Bump(k / (float)AnnounceTicks) * 0.32f;
			if (p.whoAmI == Main.myPlayer)
				ScreenFX.Focus(Vector2.Lerp(p.Center, NPC.Center, 0.6f), lean);
			if (k == AnnounceTicks - 8) {
				// release
				ScreenFX.Shake(10f);
				ScreenFX.ZoomPunch(0.06f);
				ParticleSystem.Ring(NPC.Center, ReUtils.PaleGold, 0.1f, 1.16f, 26);
				ParticleSystem.GlyphBurst(StaffTip, 24, 9f, 1f);
				SoundEngine.PlaySound(TwinSounds.Cast with { Volume = 0.8f, Pitch = -0.2f }, NPC.Center);
			}
		}

		/// <summary>Between movements she sometimes unwrites herself and is rewritten elsewhere.</summary>
		private void DoBlink(Player p)
		{
			const int Out = 14, In = 30;
			NPC.velocity *= 0.8f;
			SetPose(TwinPose.Cast);
			if (Timer < Out) {
				NPC.Opacity = 1f - Timer / Out;
				if (!Main.dedServ && Timer % 2 == 0)
					ParticleSystem.Glyph(NPC.Center + Main.rand.NextVector2Circular(20, 60), Main.rand.NextVector2Circular(5f, 5f), Accent, 1f, 24);
			}
			else if (Timer == Out) {
				if (!Main.dedServ) {
					ParticleSystem.Ring(NPC.Center, Accent, 0.4f, 0.88f, 14);
					SoundEngine.PlaySound(TwinSounds.Dash with { Volume = 0.45f, Pitch = 0.5f }, NPC.Center);
				}
				if (Server) {
					float side = Main.rand.NextBool() ? -1f : 1f;
					NPC.Center = p.Center + new Vector2(side * Main.rand.NextFloat(280f, 420f), -Main.rand.NextFloat(120f, 260f));
					NPC.velocity = Vector2.Zero;
					NPC.netUpdate = true;
				}
				if (!Main.dedServ) {
					for (int i = 0; i < 18; i++) {
						Vector2 from = NPC.Center + Main.rand.NextVector2CircularEdge(120f, 120f);
						ParticleSystem.Glyph(from, (NPC.Center - from) / 12f, Accent, 1f, 12).Drag = 1f;
					}
					ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.14f, 20);
				}
			}
			else {
				NPC.Opacity = ReUtils.Saturate((Timer - Out) / (In - Out));
				if (Timer >= In) {
					int next = (int)Sub;
					NPC.Opacity = 1f;
					Enter(next >= 10 ? next : StIdle);
				}
			}
		}

		protected void SetPose(TwinPose pose) => Pose = pose;

		/// <summary>Velocity steering toward a point.</summary>
		protected void Hover(Vector2 target, float speed, float inertia = 14f)
		{
			// they are quick: everything they do is faster than it looks
			speed *= Grief ? 1.9f : Awakened ? 1.75f : 1.5f;
			inertia = MathF.Max(4f, inertia * 0.65f);
			Vector2 d = target - NPC.Center;
			float len = d.Length();
			Vector2 want = len < speed ? d : d / len * speed;
			NPC.velocity = (NPC.velocity * (inertia - 1f) + want) / inertia;
		}

		protected Vector2 HomeSpot(Player p) => p.Center + new Vector2(Side * 380f, -190f + MathF.Sin(FightTicks * 0.025f + Side) * 30f);

		protected int OrbDamage => ReUtils.ProjDamage(Awakened ? 115 : 95);

		protected void Orb(Vector2 pos, Vector2 vel, float kind, float behaviour = 0f, float param = 0f, int damage = -1)
		{
			if (!Server)
				return;
			Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel, ModContent.ProjectileType<SongOrb>(), damage < 0 ? OrbDamage : damage, 0f, Main.myPlayer, kind, behaviour, param);
		}

		protected Vector2 StaffTip => NPC.Center + new Vector2(NPC.spriteDirection * 28f, -76f);

		// ---------------------------------------------------------------- intro
		private void DoIntro(Player p)
		{
			NPC.dontTakeDamage = true;
			float k = Timer / 200f;
			NPC.Opacity = ReUtils.Saturate(k * 1.4f);
			NPC.velocity = new Vector2(0f, -0.4f * (1f - k));
			SetPose(Timer < 140 ? TwinPose.Sing : TwinPose.Idle);
			if (!Main.dedServ) {
				if (Timer < 160 && Timer % 2 == 0) {
					// shards of Re pulled in and rewritten into a girl
					Vector2 from = NPC.Center + Main.rand.NextVector2CircularEdge(220f, 220f) * Main.rand.NextFloat(0.6f, 1f);
					ParticleSystem.Glyph(from, (NPC.Center - from) / 26f, Accent, Main.rand.NextFloat(0.8f, 1.2f), 26);
					if (Main.rand.NextBool(3))
						ParticleSystem.Goo(from, (NPC.Center - from) / 30f, 0.8f, 30);
				}
				if (Timer == 150) {
					SoundEngine.PlaySound(TwinSounds.Cast, NPC.Center);
					ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.12f, 26);
					for (int i = 0; i < 14; i++)
						ParticleSystem.Spark(NPC.Center, Main.rand.NextVector2CircularEdge(8f, 8f), ReUtils.PaleGold, 0.8f, 20);
					glowBoost = 1f;
				}
			}
			if (Timer >= 240) {
				NPC.Opacity = 1f;
				Enter(Side < 0 ? StIdle : StHarass);
			}
		}

		// ---------------------------------------------------------------- idle / scheduling
		private void DoIdle(Player p)
		{
			Hover(HomeSpot(p), 9f);
			if (Timer < 0) {
				// grieving pause
				SetPose(TwinPose.Hurt);
				NPC.velocity *= 0.9f;
				return;
			}
			if (Timer < (Awakened ? 6 : 12) || !Server)
				return;
			TwinBase other = Sister;
			bool otherBusy = other != null && other.State >= 10 && other.State < StReveal;
			// the duo attacks belong to the awakened sisters, led by Requiem
			if (Awakened && other != null && this is Requiem && !otherBusy && (other.State == StIdle || other.State == StHarass) && Main.rand.NextBool(3) && FightTicks > TwinsTimeline.RevealEnd + 300) {
				int duo = Main.rand.Next(3) switch { 0 => StDuetBeam, 1 => StLyricWall, _ => StLilyStorm };
				StartDuo(duo, other, p);
				return;
			}
			// before the awakening they take turns
			if (otherBusy && (!Awakened || Main.rand.NextBool(2)) && !Grief) {
				Enter(StHarass);
				return;
			}
			int next = ChooseAttack();
			// sometimes she unwrites herself and reappears elsewhere before singing
			if (FightTicks > TwinsTimeline.IntroEnd && Main.rand.NextBool(Awakened ? 2 : 3)) {
				Enter(StBlink);
				Sub = next;
				return;
			}
			Enter(next);
		}

		protected int FromBag(params int[] options)
		{
			if (bag.Count == 0) {
				bag.AddRange(options);
				for (int i = bag.Count - 1; i > 0; i--) {
					int j = Main.rand.Next(i + 1);
					(bag[i], bag[j]) = (bag[j], bag[i]);
				}
			}
			int s = bag[^1];
			bag.RemoveAt(bag.Count - 1);
			return s;
		}

		private void DoHarass(Player p)
		{
			Hover(HomeSpot(p), 10f);
			int every = Awakened ? 34 : 52;
			if (Timer > 20 && Timer % every == 0) {
				SetPose(TwinPose.Cast);
				Vector2 dir = StaffTip.SafeDirectionTo(p.Center);
				float kind = this is Requiem && Timer % (every * 2) == 0 ? SongOrb.Dark : SongOrb.Gold;
				Orb(StaffTip, dir * 6.5f, kind, SongOrb.Straight, 1.01f);
				if (Awakened) {
					Orb(StaffTip, dir.RotatedBy(0.3f) * 6f, SongOrb.Gold, SongOrb.Straight, 1.01f);
					Orb(StaffTip, dir.RotatedBy(-0.3f) * 6f, SongOrb.Gold, SongOrb.Straight, 1.01f);
				}
				if (!Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Note((int)(Timer / every) + Side), NPC.Center);
			}
			if (Timer > every * 0.5f && Timer % every < 10)
				SetPose(TwinPose.Cast);
			if (Timer >= (Awakened ? 90 : 140))
				Enter(StIdle);
		}

		private void StartDuo(int duo, TwinBase other, Player p)
		{
			Anchor = p.Center;
			other.Anchor = p.Center;
			Enter(duo);
			other.State = duo;
			other.Timer = Timer;     // shares the unveiling, if any
			other.Sub = 0;
			other.NPC.netUpdate = true;
		}

		/// <summary>Duo attacks, run by both sisters (each knows her own part).</summary>
		protected void RunDuo(Player p)
		{
			TwinBase other = Sister;
			bool alone = other == null || other.State != State;
			switch ((int)State) {
				case StDuetBeam: {
					// they orbit a fixed point, a gapped beam strung between them sweeping the arena
					float ang0 = Side < 0 ? MathHelper.Pi : 0f;
					float omega = Grief ? 0.02f : 0.014f;
					float ang = ang0 + MathF.Max(0f, Timer - 60) * omega * (Sub == 0 ? 1f : -1f);
					Vector2 spot = Anchor + ang.ToRotationVector2() * 430f;
					Hover(spot, Timer < 60 ? 16f : 30f, Timer < 60 ? 10f : 3f);
					SetPose(TwinPose.Sing);
					if (Timer == 1 && this is Requiem && !alone && Server)
						Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero, ModContent.ProjectileType<DuetBeam>(), ReUtils.ProjDamage(130), 0f, Main.myPlayer, NPC.whoAmI, other.NPC.whoAmI, 60);
					if (alone && Timer % 40 == 0) {
						for (int i = 0; i < 10; i++)
							Orb(NPC.Center, (MathHelper.TwoPi * i / 10f + Timer * 0.1f).ToRotationVector2() * 4f, i % 2 == 0 ? SongOrb.Dark : SongOrb.Gold, SongOrb.Straight, 1.01f);
					}
					if (Timer >= 330)
						Enter(StIdle);
					break;
				}
				case StLyricWall: {
					// walls of verse sweep the arena: Requiem's are dark (find the gap), Refrain's are gold (cut through)
					Hover(Anchor + new Vector2(Side * 520f, -300f), 12f);
					SetPose(TwinPose.Cast);
					int every = Grief ? 34 : 44;
					int myTurn = this is Requiem ? 0 : 1;
					if (Timer >= 40 && Timer < 270 && Timer % every == 0 && ((int)(Timer / every) % 2 == myTurn || alone)) {
						Player pl = p;
						float gapY = pl.Center.Y + Main.rand.NextFloat(-170f, 170f);
						float x0 = pl.Center.X + Side * 980f;
						for (int i = -9; i <= 9; i++) {
							float y = pl.Center.Y + i * 52f;
							if (Math.Abs(y - gapY) < 80f)
								continue;
							bool edge = Math.Abs(y - gapY) < 135f;
							float kind = this is Refrain || edge ? SongOrb.Gold : SongOrb.Dark;
							Orb(new Vector2(x0, y), new Vector2(-Side * 7f, 0f), kind, SongOrb.Straight, 0f);
						}
						if (!Main.dedServ) {
							SoundEngine.PlaySound(this is Requiem ? TwinSounds.Bell with { Volume = 0.45f } : TwinSounds.Chain, NPC.Center);
							glowBoost = 0.8f;
						}
					}
					if (Timer >= 300)
						Enter(StIdle);
					break;
				}
				case StLilyStorm: {
					// the lily field answers their song: petals rise from below
					Hover(Anchor + new Vector2(Side * 420f, -260f + MathF.Sin(Timer * 0.04f) * 40f), 10f);
					SetPose(TwinPose.Sing);
					if (!Main.dedServ)
						DimensionSystem.LilyGlowTarget = 1.6f;
					if (this is Requiem || alone) {
						if (Timer > 30 && Timer < 250 && Timer % (Grief ? 3 : 5) == 0)
							Orb(p.Center + new Vector2(Main.rand.NextFloat(-820f, 820f), 560f), new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(5f, 7.5f)), SongOrb.Petal, SongOrb.Homing, 0f, ReUtils.ProjDamage(80));
					}
					if (Timer % 60 == 30) {
						Vector2 d = StaffTip.SafeDirectionTo(p.Center);
						for (int i = -1; i <= 1; i++)
							Orb(StaffTip, d.RotatedBy(i * 0.25f) * 7f, SongOrb.Dark, SongOrb.Straight, 0f);
					}
					if (Timer >= 280) {
						if (!Main.dedServ)
							DimensionSystem.LilyGlowTarget = 1f;
						Enter(StIdle);
					}
					break;
				}
			}
		}

		// ---------------------------------------------------------------- the revelation (2:00 – 2:25)
		private void DoReveal(Player p)
		{
			int r = FightTicks - TwinsTimeline.RevealStart;
			NPC.dontTakeDamage = true;
			if (r <= 1) {
				Anchor = p.Center;
				if (Server) {
					foreach (Projectile proj in Main.ActiveProjectiles) {
						if (proj.hostile && proj.ModProjectile is IReProjectile)
							proj.Kill();
					}
				}
			}
			Vector2 spot = Anchor + new Vector2(Side * 170f, -60f);
			if (r < 420) {
				// broken, at the false floor: they drift in, wounded
				Hover(spot + new Vector2(0f, MathF.Sin(r * 0.03f) * 6f), 6f, 22f);
				SetPose(r < 300 ? TwinPose.Hurt : TwinPose.Idle);
			}
			else {
				float rise = ReUtils.EaseInOutSine((r - 420) / 600f);
				Hover(spot + new Vector2(Side * 60f * rise, -120f * rise), 4f, 20f);
				SetPose(TwinPose.Sing);
			}
			NPC.spriteDirection = -Side;   // face each other / the player

			// the rewind: every wound is read back
			int t = FightTicks;
			if (t >= TwinsTimeline.RewindStart && t <= TwinsTimeline.RewindEnd) {
				float k = ReUtils.EaseInOutSine(ReUtils.InverseLerp(TwinsTimeline.RewindStart, TwinsTimeline.RewindEnd, t));
				NPC.life = (int)MathHelper.Lerp(LifeFloor, NPC.lifeMax, k);
				ShownLife = NPC.life;
				if (!Main.dedServ) {
					rewind = 1f;
					if (t % 2 == 0) {
						Vector2 from = NPC.Center + Main.rand.NextVector2CircularEdge(340f, 340f);
						ParticleSystem.Glyph(from, (NPC.Center - from) / 22f, Main.rand.NextBool(3) ? ReUtils.EyeBlue : Accent, 1f, 22);
					}
					if (t % 30 == 0) {
						ParticleSystem.Ring(NPC.Center, Accent, 1.1f, 0.94f, 30);
						SoundEngine.PlaySound(TwinSounds.Note(t / 30) with { Pitch = -0.5f + k * 0.8f, Volume = 0.35f }, NPC.Center);
					}
				}
			}
			if (t == TwinsTimeline.RewindEnd)
				NPC.life = NPC.lifeMax;
			if (t == TwinsTimeline.TransformAt) {
				TwinsFight.Revealed.Clear();     // every movement returns, transformed, as a reprise
				Awakened = true;
				NPC.defense = (int)(NPC.defense * 1.15f);
				NPC.damage = (int)(NPC.damage * 1.2f);
				NPC.netUpdate = true;
				if (!Main.dedServ) {
					glowBoost = 1.5f;
					for (int i = 0; i < 30; i++)
						ParticleSystem.Spark(NPC.Center, Main.rand.NextVector2CircularEdge(14f, 14f), ReUtils.PaleGold, 1.1f, 30);
				}
			}
			if (t >= TwinsTimeline.RevealEnd - 1) {
				Awakened = true;
				NPC.dontTakeDamage = false;
				Enter(Side < 0 ? StIdle : StHarass);
			}
		}

		// ---------------------------------------------------------------- death
		private void DoDying(Player p)
		{
			NPC.dontTakeDamage = true;
			int length = IsLast ? 420 : 170;
			SetPose(TwinPose.Hurt);
			NPC.velocity *= 0.93f;
			NPC.velocity.Y += IsLast ? -0.01f : 0.02f;
			if (!Main.dedServ) {
				glitch = MathF.Max(glitch, 0.25f + 0.5f * Timer / length);
				if (Timer % 3 == 0)
					ParticleSystem.Glyph(NPC.Center + Main.rand.NextVector2Circular(16, 46), new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(1f, 3f)), Accent, 1f, 50);
				if (Timer == 1) {
					SoundEngine.PlaySound(TwinSounds.Death with { Volume = 0.7f }, NPC.Center);
					ScreenFX.Shake(10f);
					if (IsLast) {
						Subtitles.Push(Speaker, "Mods.Replicant.Twins.Lines.Last1" + (Speaker == Subtitles.Requiem ? "a" : "b"), 220);
						Cinematic.Begin(NPC.Center, length + 40);
						DimensionSystem.AwakenTarget = 0f;
					}
					else {
						Subtitles.Push(Speaker, "Mods.Replicant.Twins.Lines.Fall" + (Speaker == Subtitles.Requiem ? "a" : "b"), 200);
					}
				}
				if (!IsLast && Timer == 110)
					Subtitles.Push(Speaker == Subtitles.Requiem ? Subtitles.Refrain : Subtitles.Requiem, "Mods.Replicant.Twins.Lines.Grief" + (Speaker == Subtitles.Requiem ? "b" : "a"), 260);
				if (IsLast && Timer == 230)
					Subtitles.Push(Speaker, "Mods.Replicant.Twins.Lines.Last2", 200);
				if (Timer == length - 2) {
					SoundEngine.PlaySound(ReSounds.Burst with { Volume = IsLast ? 1f : 0.6f }, NPC.Center);
					ScreenFX.Shake(IsLast ? 30f : 14f);
					Cinematic.Flash(IsLast ? 1f : 0.5f);
					ParticleSystem.GlyphBurst(NPC.Center, IsLast ? 160 : 70, IsLast ? 18f : 11f, 1.2f);
					for (int i = 0; i < 3; i++)
						ParticleSystem.Ring(NPC.Center, i == 1 ? ReUtils.Ember : ReUtils.PaleGold, 0.1f + i * 0.08f, 1.08f + i * 0.02f, 40 + i * 8);
				}
			}
			if (Timer >= length && Server) {
				DeathDone = true;
				NPC.dontTakeDamage = false;
				NPC.life = 0;
				NPC.HitEffect();
				NPC.checkDead();
				NPC.netUpdate = true;
			}
		}

		// ================================================================ visuals
		private void UpdateVisuals()
		{
			if (Pose != lastPose) {
				lastPose = Pose;
				poseTime = 0;
			}
			poseTime++;
			hurtFlash *= 0.85f;
			glowBoost *= 0.94f;
			rewind *= 0.95f;
			haloAlpha = MathHelper.Lerp(haloAlpha, Awakened ? 1f : 0f, 0.03f);

			int t = FightTicks;
			// the false fight starts to fray as 2:00 approaches
			if (t > TwinsTimeline.GlitchStart && t < TwinsTimeline.RevealStart && Main.rand.NextBool(t > TwinsTimeline.GlitchStart + 900 ? 40 : 90))
				glitch = MathF.Max(glitch, Main.rand.NextFloat(0.2f, 0.6f));
			glitch *= 0.9f;

			if (ShownLife < 0f || !InFalseFight || NPC.life > LifeFloor + 1f)
				ShownLife = NPC.life;
			else if (ShownLife < NPC.lifeMax * 0.015f) {
				// the bar runs dry... and quietly fills back to where it was
				if (++barLieTimer > 70) {
					barLieTimer = 0;
					ShownLife = NPC.life;
					glitch = 0.8f;
					SoundEngine.PlaySound(TwinSounds.Note(2) with { Pitch = -0.8f, Volume = 0.3f }, NPC.Center);
				}
			}

			if (Awakened && Main.rand.NextBool(5))
				ParticleSystem.Spark(NPC.Center + new Vector2(Main.rand.NextFloat(-18, 18), 60), new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), -Main.rand.NextFloat(1f, 2.5f)), Accent, 0.5f, 30);
			if (Main.rand.NextBool(Awakened ? 6 : 12))
				ParticleSystem.Smoke(NPC.Center + new Vector2(Main.rand.NextFloat(-16, 16), 56), new Vector2(0, -0.6f), Color.Black * 0.5f, 0.6f, 50);
			// her gown is still Re: oil drips from the hem and falls away
			if (NPC.Opacity > 0.5f && Main.rand.NextBool(Awakened ? 4 : 7)) {
				Particle drip = ParticleSystem.Goo(NPC.Center + new Vector2(Main.rand.NextFloat(-30, 30), 74), new Vector2(NPC.velocity.X * 0.2f, Main.rand.NextFloat(0.5f, 1.5f)), Main.rand.NextFloat(0.35f, 0.6f), 50);
				drip.Gravity = 0.12f;
			}
		}

		public override void FindFrame(int frameHeight)
		{
			int f;
			switch (Pose) {
				case TwinPose.Cast:
					f = 8 + Math.Min(poseTime / 4, 5);
					if (poseTime >= 24)
						f = 12 + (poseTime / 6) % 2;
					break;
				case TwinPose.Dash:
					f = 14 + (poseTime / 4) % 4;
					break;
				case TwinPose.Sing:
					f = 18 + (int)(visualTime / 9) % 4;
					break;
				case TwinPose.Hurt:
					f = 22 + (int)(visualTime / 8) % 2;
					break;
				default:
					f = (int)(visualTime / 7) % 8;
					break;
			}
			NPC.frame = new Rectangle(0, f * FrameH, FrameW, FrameH);
		}

		public override bool PreDraw(SpriteBatch sb, Vector2 screenPos, Color drawColor)
		{
			if (NPC.IsABestiaryIconDummy) {
				sb.Draw(Sheet, NPC.Center - screenPos, NPC.frame, Color.White, 0f, Origin, NPC.scale, SpriteEffects.None, 0f);
				return false;
			}
			Texture2D tex = Sheet;
			Texture2D glow = GlowSheet;
			Rectangle src = NPC.frame;
			Vector2 pos = NPC.Center - screenPos + new Vector2(0f, NPC.gfxOffY + MathF.Sin(visualTime * 0.05f) * 2f);
			SpriteEffects fx = NPC.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			float op = NPC.Opacity;
			Color body = Color.Lerp(drawColor, Color.White, 0.55f) * op;
			float time = Main.GlobalTimeWrappedHourly;

			// --- presence: a well of black under her, a warm halo of light around her
			FX.Smoke(sb, pos + new Vector2(0, 10), Color.Black * (0.5f * op), 90f);
			FX.Bloom(sb, pos, Accent, 120f, (0.12f + glowBoost * 0.12f + (Awakened ? 0.1f : 0f)) * op);

			// --- the halo of the awakened
			if (haloAlpha > 0.01f) {
				Texture2D halo = ReTex.Halo;
				Vector2 hp = pos + new Vector2(-NPC.spriteDirection * 2f, -74f);
				float hs = 0.5f + 0.04f * MathF.Sin(time * 2f);
				sb.Draw(halo, hp, null, Accent.Additive(0.35f * haloAlpha * op), time * 0.4f * Side, halo.Size() / 2f, hs * 1.15f, SpriteEffects.None, 0f);
				sb.Draw(halo, hp, null, ReUtils.PaleGold.Additive(0.8f * haloAlpha * op), -time * 0.25f * Side, halo.Size() / 2f, hs, SpriteEffects.None, 0f);
				FX.Bloom(sb, hp, Accent, 50f, 0.25f * haloAlpha * op);
			}

			// --- afterimages (dash, or always faintly once awakened)
			float trail = Pose == TwinPose.Dash ? 1f : NPC.velocity.Length() > 9f ? 0.6f : Awakened ? 0.35f : 0f;
			if (trail > 0f) {
				for (int i = NPC.oldPos.Length - 1; i > 0; i--) {
					if (NPC.oldPos[i] == Vector2.Zero)
						continue;
					float k = 1f - i / (float)NPC.oldPos.Length;
					Vector2 p = NPC.oldPos[i] + NPC.Size / 2f - screenPos + new Vector2(0f, NPC.gfxOffY);
					sb.Draw(tex, p, src, ReUtils.GoldRamp(k * 0.8f).Additive(0.45f * k * trail * op), NPC.oldRot[i], Origin, NPC.scale, fx, 0f);
				}
			}

			// --- dash telegraph
			if (telegraph > 0.02f)
				FX.Beam(sb, pos, telegraphDir.ToRotation(), 900f, 10f, Accent, 0.5f * telegraph, ReTex.Thread);

			// --- reveal / death: the body is written in from the top or erased from the top
			int cutTop = 0;
			if (State == StIntro)
				cutTop = (int)(FrameH * (1f - ReUtils.EaseOutCubic(ReUtils.InverseLerp(20, 190, Timer))));
			else if (State == StDying && DeathDoneProgress > 0f)
				cutTop = (int)(FrameH * DeathDoneProgress);
			Rectangle drawSrc = new(src.X, src.Y + cutTop, src.Width, src.Height - cutTop);
			Vector2 drawOrigin = Origin - new Vector2(0, cutTop);

			// --- rewind: chromatic echoes
			if (rewind > 0.02f) {
				for (int i = 0; i < 3; i++) {
					Vector2 off = new Vector2(MathF.Sin(time * 9f + i * 2f), MathF.Cos(time * 7f + i)) * 10f * rewind;
					Color c = i == 0 ? ReUtils.EyeBlue : i == 1 ? ReUtils.Ember : ReUtils.PaleGold;
					sb.Draw(tex, pos + off, drawSrc, c.Additive(0.3f * rewind * op), 0f, drawOrigin, NPC.scale, fx, 0f);
				}
			}

			// --- orbiting glyph motes (the far half passes behind her)
			DrawMotes(sb, pos, op, back: true);

			// --- the body (sliced when the world glitches)
			float gl = glitch;
			if (gl > 0.05f) {
				int slices = 7;
				int h = drawSrc.Height / slices + 1;
				for (int i = 0; i < slices; i++) {
					int y0 = drawSrc.Y + i * h;
					int hh = Math.Min(h, drawSrc.Bottom - y0);
					if (hh <= 0)
						break;
					Rectangle s = new(drawSrc.X, y0, drawSrc.Width, hh);
					float jitter = (ReUtils.Hash01(i, (int)(visualTime / 3), NPC.whoAmI) - 0.5f) * 26f * gl;
					if (ReUtils.Hash01(i, (int)(visualTime / 3), 7) > 0.6f)
						jitter = 0f;
					Vector2 o = drawOrigin - new Vector2(0, i * h);
					sb.Draw(tex, pos + new Vector2(jitter - 3f * gl, 0), s, new Color(255, 40, 30).Additive(0.5f * gl * op), 0f, o, NPC.scale, fx, 0f);
					sb.Draw(tex, pos + new Vector2(jitter + 3f * gl, 0), s, new Color(40, 180, 255).Additive(0.35f * gl * op), 0f, o, NPC.scale, fx, 0f);
					sb.Draw(tex, pos + new Vector2(jitter, 0), s, body, 0f, o, NPC.scale, fx, 0f);
					sb.Draw(glow, pos + new Vector2(jitter, 0), s, Color.White.Additive((0.85f + glowBoost) * op), 0f, o, NPC.scale, fx, 0f);
				}
			}
			else {
				if (Awakened) {
					// ember rim light
					for (int i = 0; i < 4; i++) {
						Vector2 off = (MathHelper.PiOver2 * i + time).ToRotationVector2() * 2f;
						sb.Draw(tex, pos + off, drawSrc, Accent.Additive(0.3f * op), 0f, drawOrigin, NPC.scale, fx, 0f);
					}
				}
				sb.Draw(tex, pos, drawSrc, body, 0f, drawOrigin, NPC.scale, fx, 0f);
				if (hurtFlash > 0.05f)
					sb.Draw(tex, pos, drawSrc, ReUtils.PaleGold.Additive(0.5f * hurtFlash * op), 0f, drawOrigin, NPC.scale, fx, 0f);
				sb.Draw(glow, pos, drawSrc, Color.White.Additive((0.85f + glowBoost + (Awakened ? 0.25f : 0f)) * op), 0f, drawOrigin, NPC.scale, fx, 0f);
			}

			// --- the writing / erasing line
			if (cutTop > 0 && cutTop < FrameH) {
				Vector2 lp = pos + new Vector2(0f, cutTop - Origin.Y);
				FX.Bloom(sb, lp, Accent, 60f, 0.5f * op + 0.3f, 1.4f, 0.12f);
				for (int i = -3; i <= 3; i++)
					sb.Draw(ReTex.GlyphAtlasGlow, lp + new Vector2(i * 13f, 0f), ReTex.GlyphFrame((int)(visualTime / 3) + i * 7), ReUtils.PaleGold.Additive(0.9f), 0f, new Vector2(7, 9), 0.8f, SpriteEffects.None, 0f);
			}

			DrawMotes(sb, pos, op, back: false);
			// where her eyes should be, the light of the written-over glyphs (Requiem: through the blindfold)
			{
				Vector2 face = pos + new Vector2(NPC.spriteDirection * 1f, -70f);
				float flick = this is Refrain ? 0.55f + 0.45f * ReUtils.Hash01((int)(visualTime / 3), NPC.whoAmI) : 0.6f + 0.2f * MathF.Sin(time * 2f);
				FX.Bloom(sb, face, Accent, 16f, 0.35f * flick * op, 2.2f, 0.6f);
			}

			DrawExtras(sb, pos, screenPos);
			return false;
		}

		private void DrawMotes(SpriteBatch sb, Vector2 pos, float op, bool back)
		{
			float time = Main.GlobalTimeWrappedHourly;
			int count = Awakened ? 7 : 5;
			for (int k = 0; k < count; k++) {
				float ang = time * (0.9f + 0.1f * k) * Side + k * MathHelper.TwoPi / count;
				float depth = MathF.Sin(ang);
				if (back != depth < 0f)
					continue;
				Vector2 p = pos + new Vector2(MathF.Cos(ang) * (38f + 6f * MathF.Sin(time + k)), depth * 9f - 8f + MathF.Sin(time * 1.7f + k) * 18f);
				float a = (back ? 0.35f : 0.85f) * op * (0.6f + 0.4f * MathF.Sin(time * 3f + k * 2f));
				sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(k * 7 + (int)(time * 0.7f) + NPC.whoAmI), Accent.Additive(a), 0f, new Vector2(7, 9), back ? 0.7f : 0.9f, SpriteEffects.None, 0f);
				if (!back)
					FX.Bloom(sb, p, Accent, 12f, 0.25f * a);
			}
		}

		private float DeathDoneProgress => State == StDying ? ReUtils.InverseLerp(IsLast ? 250 : 60, IsLast ? 418 : 168, Timer) : 0f;

		/// <summary>Per-twin overlays (telegraphs, staves...).</summary>
		protected virtual void DrawExtras(SpriteBatch sb, Vector2 pos, Vector2 screenPos) { }

		public override void BossHeadSlot(ref int index)
		{
			if (NPC.Opacity < 0.5f)
				index = -1;
		}
	}

	/// <summary>Boss bar that lies during the false fight.</summary>
	public class RequiemBossBar : TwinBossBar
	{
		protected override string Head => "Replicant/Assets/Textures/NPCs/Twins/Requiem_Head_Boss";
	}

	public class RefrainBossBar : TwinBossBar
	{
		protected override string Head => "Replicant/Assets/Textures/NPCs/Twins/Refrain_Head_Boss";
	}

	public abstract class TwinBossBar : ModBossBar
	{
		protected abstract string Head { get; }

		public override ReLogic.Content.Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame) => ModContent.Request<Texture2D>(Head);

		public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, ref BossBarDrawParams drawParams)
		{
			if (npc.ModNPC is not TwinBase t)
				return true;
			if (t.ShownLife >= 0f)
				drawParams.Life = MathHelper.Clamp(t.ShownLife, 0f, drawParams.LifeMax);
			if (t.InFalseFight && npc.life <= t.LifeFloor + 1f) {
				drawParams.BarCenter += Main.rand.NextVector2Circular(1.5f, 1.5f);
				if (Main.rand.NextBool(12))
					drawParams.Life = npc.life;
			}
			return true;
		}

		public override void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams)
		{
			if (npc.ModNPC is not TwinBase t || !t.InFalseFight || npc.life > t.LifeFloor + 1f)
				return;
			// glyphs crawl over the bar where the missing life should be
			float time = Main.GlobalTimeWrappedHourly;
			for (int i = 0; i < 6; i++) {
				float x = drawParams.BarCenter.X - 230f + ReUtils.Hash01(i, (int)(time * 8f)) * 460f;
				Vector2 p = new(x, drawParams.BarCenter.Y + (ReUtils.Hash01(i, 3, (int)(time * 8f)) - 0.5f) * 10f);
				spriteBatch.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(i * 7 + (int)(time * 10f)), ReUtils.Gold.Additive(0.7f), 0f, new Vector2(7, 9), 0.9f, SpriteEffects.None, 0f);
			}
		}
	}
}
