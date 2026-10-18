using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Replicant.Content.Projectiles;
using Replicant.Content.Projectiles.Recursion;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Recursion
{
	public static class RecursionSounds
	{
		private const string P = "Replicant/Assets/Sounds/Recursion/";
		public static readonly SoundStyle Roar = new(P + "Roar") { Volume = 1f };
		public static readonly SoundStyle Screech = new(P + "Screech") { Volume = 0.85f, MaxInstances = 2 };
		public static readonly SoundStyle Rush = new(P + "Rush") { Volume = 0.8f, MaxInstances = 2 };
		public static readonly SoundStyle Bite = new(P + "Bite") { Volume = 0.9f, MaxInstances = 3 };
		public static readonly SoundStyle Telegraph = new(P + "Telegraph") { Volume = 0.6f, MaxInstances = 3 };
		public static readonly SoundStyle Portal = new(P + "Portal") { Volume = 0.85f, MaxInstances = 2 };
		public static readonly SoundStyle Erupt = new(P + "Erupt") { Volume = 1f, MaxInstances = 2 };
		public static readonly SoundStyle ChainLock = new(P + "ChainLock") { Volume = 0.85f, MaxInstances = 4 };
		public static readonly SoundStyle Needle = new(P + "Needle") { Volume = 0.35f, PitchVariance = 0.2f, MaxInstances = 8 };
		public static readonly SoundStyle Death = new(P + "Death") { Volume = 1f };
		public static readonly SoundStyle OminousDrone = new(P + "OminousDrone") { Volume = 0.9f };
		public static readonly SoundStyle SonicBoom = new(P + "SonicBoom") { Volume = 0.9f, MaxInstances = 2 };
		public static readonly SoundStyle GroundSkim = new(P + "GroundSkim") { Volume = 0.7f, MaxInstances = 2 };
		public static readonly SoundStyle Constrict = new(P + "Constrict") { Volume = 0.8f, MaxInstances = 1 };
		public static readonly SoundStyle RewindTape = new(P + "RewindTape") { Volume = 0.85f, MaxInstances = 1 };
		public static readonly SoundStyle ExecuteGlitch = new(P + "ExecuteGlitch") { Volume = 0.9f, MaxInstances = 1 };
		public static readonly SoundStyle Chitter = new(P + "Chitter", 1, 2) { Volume = 0.45f, PitchVariance = 0.25f, MaxInstances = 3 };
		public static readonly SoundStyle Hit = new(P + "Chitter", 1, 2) { Volume = 0.3f, Pitch = 0.4f, PitchVariance = 0.3f, MaxInstances = 4 };
	}

	/// <summary>
	/// RECURSION — the verse that devours. A centipede of black lacquered chitin, inscribed in gold, that eats
	/// the song's last singer and then comes for you. Fast and lethal in the spirit of the Devourer of Gods:
	/// every one of its big moves is clearly telegraphed, and every one of them can end you.
	///
	/// The head runs everything: it owns the body segments (separate NPCs sharing its life), moves them along
	/// behind it like a rope, decides which of them may hurt or be hurt, and draws the whole animal.
	/// </summary>
	public class RecursionHead : ModNPC
	{
		public override string Texture => "Replicant/Assets/Textures/NPCs/Recursion/RecursionHead";

		public const int Segments = 44;
		public const float Spacing = 60f;
		public const int StScripted = 0, StHunt = 1, StLunge = 2, StPortal = 3, StCage = 4, StCoil = 5, StRewind = 6, StExecute = 7, StDying = 9, StLeave = 10;

		public ref float State => ref NPC.ai[0];
		public ref float Timer => ref NPC.ai[1];
		public ref float Sub => ref NPC.ai[2];

		public readonly int[] seg = new int[Segments];
		private bool spawnedSegments;
		/// <summary>Per-segment: hidden inside a portal (no drawing, no damage either way).</summary>
		public readonly bool[] hidden = new bool[Segments + 1];
		private readonly Vector2[] segOld = new Vector2[Segments];
		private bool headHidden;

		// ---- scripted control (the devour cutscene)
		public Vector2 ScriptTarget;
		public float ScriptSpeed = 40f, ScriptTurn = 0.2f;

		// ---- attack bookkeeping
		private readonly List<int> bag = new();
		private bool frenzy;
		private bool rewindUsed;
		private Vector2 aimDir = Vector2.UnitX;
		private Vector2 portalIn, portalOut;
		private float portalInOpen, portalOutOpen;
		private float coilAngle, coilRadius;
		private int volleyTimer;
		private float lethalLine;       // 0..1 telegraph strength for the lethal line
		private Vector2 lethalFrom, lethalDir;
		private bool lethal;
		private float jaw, jawTarget;   // mandibles 0..1
		private float hurtFlash;
		private float glowPulse;
		private int dyingPopped;
		private float charge, legPhase;
		private Vector2 tearFrom, tearTo, coilCenter;
		private float tearLife;
		private readonly Queue<Vector2> path = new();   // last positions, for the rewind
		private static readonly List<Vector2> pathCopy = new();

		public float Fury => 1f + 0.3f * (1f - NPC.life / (float)NPC.lifeMax) + (frenzy ? 0.18f : 0f);
		private Player Target => Main.player[NPC.target];

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = 4;
			NPCID.Sets.MPAllowedEnemies[Type] = true;
			NPCID.Sets.BossBestiaryPriority.Add(Type);
			NPCID.Sets.ImmuneToRegularBuffs[Type] = true;
			NPCID.Sets.CantTakeLunchMoney[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true });
		}

		public override void SetDefaults()
		{
			NPC.width = 110;
			NPC.height = 110;
			NPC.lifeMax = 125000;
			NPC.damage = 190;
			NPC.defense = 52;
			NPC.knockBackResist = 0f;
			NPC.noGravity = true;
			NPC.noTileCollide = true;
			NPC.boss = true;
			NPC.npcSlots = 20f;
			NPC.aiStyle = -1;
			NPC.lavaImmune = true;
			NPC.netAlways = true;
			NPC.behindTiles = false;
			NPC.value = Item.buyPrice(platinum: 1);
			NPC.HitSound = RecursionSounds.Hit;
			NPC.DeathSound = null;
			NPC.dontTakeDamage = true;
			NPC.BossBar = ModContent.GetInstance<RecursionBossBar>();
			if (!Main.dedServ)
				Music = MusicLoader.GetMusicSlot(Mod, "Assets/Music/AtTheSpeedOfLight");
		}

		public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
		{
			NPC.lifeMax = (int)(NPC.lifeMax * 0.8f * balance * bossAdjustment);
			NPC.damage = (int)(NPC.damage * 0.8f);
		}

		public override bool CheckActive() => false;

		public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) => false;

		public override void BossHeadRotation(ref float rotation) => rotation = NPC.rotation;

		public override void BossHeadSlot(ref int index)
		{
			if (headHidden || State == StScripted)
				index = -1;
		}

		// ================================================================ AI
		public override void AI()
		{
			if (!spawnedSegments)
				SpawnSegments();
			if (NPC.target < 0 || NPC.target >= Main.maxPlayers || Target.dead || !Target.active)
				NPC.TargetClosest(false);
			Player p = Target;

			if (State != StDying && State != StLeave && State != StScripted && (p.dead || !p.active)) {
				Enter(StLeave);
			}
			jawTarget = 0.15f;
			lethal = false;
			lethalLine *= 0.85f;
			NPC.dontTakeDamage = State == StScripted || State == StDying || State == StLeave || headHidden;

			switch ((int)State) {
				case StScripted: Scripted(); break;
				case StHunt: Hunt(p); break;
				case StLunge: Lunge(p); break;
				case StPortal: PortalDive(p); break;
				case StCage: Cage(p); break;
				case StCoil: Coil(p); break;
				case StRewind: Rewind(p); break;
				case StExecute: Execute(p); break;
				case StDying: Dying(); break;
				case StLeave: Leave(); break;
			}
			Timer++;
			ApplyFloor();
			if (NPC.velocity.LengthSquared() > 0.01f)
				NPC.rotation = NPC.velocity.ToRotation();

			// bodies follow like a rope
			FollowSegments();
			UpdateVolley(p);

			// remember where the head has been (the rewind replays it)
			if (!headHidden) {
				path.Enqueue(NPC.Center);
				while (path.Count > 300)
					path.Dequeue();
			}

			if (!Main.dedServ)
				ClientEffects(p);
		}

		private void SpawnSegments()
		{
			spawnedSegments = true;
			if (Main.netMode == NetmodeID.MultiplayerClient)
				return;
			int prev = NPC.whoAmI;
			for (int i = 0; i < Segments; i++) {
				int type = i == Segments - 1 ? ModContent.NPCType<RecursionTail>() : ModContent.NPCType<RecursionBody>();
				Vector2 at = NPC.Center - NPC.velocity.SafeNormalize(Vector2.UnitX) * Spacing * (i + 1);
				int n = NPC.NewNPC(NPC.GetSource_FromAI(), (int)at.X, (int)at.Y, type, NPC.whoAmI, NPC.whoAmI, i);
				if (n < 0 || n >= Main.maxNPCs)
					break;
				NPC s = Main.npc[n];
				s.realLife = NPC.whoAmI;
				s.Center = at;
				seg[i] = n;
				segOld[i] = at;
				prev = n;
			}
		}

		private NPC Seg(int i)
		{
			int n = seg[i];
			return n >= 0 && n < Main.maxNPCs && Main.npc[n].active && Main.npc[n].realLife == NPC.whoAmI ? Main.npc[n] : null;
		}

		public Vector2 SegPos(int i) => Seg(i)?.Center ?? NPC.Center;

		private void FollowSegments()
		{
			Vector2 lead = NPC.Center;
			for (int i = 0; i < Segments; i++) {
				NPC s = Seg(i);
				if (s == null)
					continue;
				segOld[i] = s.Center;
				Vector2 d = s.Center - lead;
				if (d.LengthSquared() < 0.01f)
					d = -NPC.velocity.SafeNormalize(Vector2.UnitX);
				d.Normalize();
				float sp = Spacing * SegScale(i);
				s.Center = lead + d * sp;
				// plates ride over the field, never through it (only a rift can take them under)
				if (!hidden[i]) {
					float fl = Floor(s.Center.X) - 30f;
					if (s.Center.Y > fl)
						s.Center = new Vector2(s.Center.X, MathHelper.Lerp(s.Center.Y, fl, 0.6f));
					d = (s.Center - lead).SafeNormalize(d);
				}
				s.rotation = (-d).ToRotation();
				s.velocity = Vector2.Zero;
				lead = s.Center;
			}
		}

		/// <summary>The field's surface under an x (no floor outside the dimension).</summary>
		public static float Floor(float x) => DimensionBridge.InDimension ? DimensionTerrain.SurfaceAt(x / 16f) * 16f : float.MaxValue;

		private int skimCooldown;

		/// <summary>
		/// The head never dives into the ground: it skims along it instead, ploughing up oil, petals and sparks,
		/// and curls back up into the air.
		/// </summary>
		private void ApplyFloor()
		{
			if (headHidden || State == StScripted)
				return;
			float g = Floor(NPC.Center.X) - 70f;
			if (NPC.Center.Y < g - 60f)
				return;
			float speed = NPC.velocity.Length();
			if (NPC.velocity.Y > 0f) {
				// bend the motion along the surface, keeping its speed
				Vector2 v = NPC.velocity;
				v.Y = MathHelper.Lerp(v.Y, -MathF.Abs(v.X) * 0.12f - 2f, 0.35f);
				NPC.velocity = v.SafeNormalize(Vector2.UnitX) * speed;
			}
			if (NPC.Center.Y > g)
				NPC.Center = new Vector2(NPC.Center.X, g);
			if (skimCooldown > 0)
				skimCooldown--;
			if (Main.dedServ || speed < 12f)
				return;
			// the ground answers
			Vector2 at = new(NPC.Center.X, Floor(NPC.Center.X) - 6f);
			for (int i = 0; i < 4; i++) {
				Vector2 v = new(-NPC.velocity.X * Main.rand.NextFloat(0.1f, 0.35f) + Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(3f, 10f));
				if (i == 0)
					ParticleSystem.Smoke(at, v * 0.4f, new Color(14, 9, 6) * 0.8f, Main.rand.NextFloat(0.8f, 1.5f), 50);
				else if (i == 1)
					ParticleSystem.Goo(at, v * 0.6f, Main.rand.NextFloat(0.4f, 0.8f), 50).Gravity = 0.25f;
				else
					ParticleSystem.Spark(at, v, i == 2 ? ReUtils.Ember : new Color(250, 236, 210), 0.9f, 30).Gravity = 0.3f;
			}
			Common.Dimension.DimensionSystem.PushField(at, 140f, NPC.velocity.X);
			if (skimCooldown <= 0) {
				skimCooldown = 50;
				SoundEngine.PlaySound(RecursionSounds.GroundSkim, at);
				ScreenFX.Shake(5f);
			}
		}

		public static float SegScale(int i) => 1.12f - 0.3f * MathF.Pow(i / (float)Segments, 2f);

		// ---------------------------------------------------------------- movement helpers
		/// <summary>Steer the head: turn toward a direction by at most `turn` rad/tick and ease the speed.</summary>
		private void Steer(Vector2 toward, float speed, float turn, float accel = 0.08f)
		{
			Vector2 v = NPC.velocity;
			if (v.LengthSquared() < 1f)
				v = toward.SafeNormalize(Vector2.UnitX) * 2f;
			float cur = v.ToRotation();
			float want = toward.ToRotation();
			float diff = MathHelper.WrapAngle(want - cur);
			cur += MathHelper.Clamp(diff, -turn, turn);
			float len = MathHelper.Lerp(v.Length(), speed, accel);
			NPC.velocity = cur.ToRotationVector2() * len;
		}

		private void Enter(int state)
		{
			State = state;
			Timer = 0;
			Sub = 0;
			NPC.netUpdate = true;
		}

		private void NextAttack()
		{
			if (bag.Count == 0) {
				if (frenzy)
					bag.AddRange(new[] { StLunge, StPortal, StLunge, StCoil, StPortal, StCage });
				else
					bag.AddRange(new[] { StLunge, StPortal, StCage, StCoil });
				if (!frenzy && !rewindUsed && NPC.life < NPC.lifeMax * 0.6f)
					bag.Add(StRewind);
				// shuffle, but never the same attack twice in a row
				for (int i = bag.Count - 1; i > 0; i--) {
					int j = Main.rand.Next(i + 1);
					(bag[i], bag[j]) = (bag[j], bag[i]);
				}
			}
			int next = bag[0];
			bag.RemoveAt(0);
			Enter(next);
		}

		// ---------------------------------------------------------------- states
		private void Scripted()
		{
			Steer(ScriptTarget - NPC.Center, ScriptSpeed, ScriptTurn, 0.2f);
			jawTarget = 0.6f;
		}

		/// <summary>Released from the cutscene: the hunt begins.</summary>
		public void Release()
		{
			Enter(StHunt);
			Timer = -40;
		}

		private int HuntLength => frenzy ? 100 : 170;

		private void Hunt(Player p)
		{
			float dist = NPC.Distance(p.Center);
			float speed = (dist > 1000f ? 27f : 19f) * Fury;
			Steer(p.Center - NPC.Center, speed, dist > 900f ? 0.08f : 0.052f);
			if (dist < 320f)
				jawTarget = 0.85f;
			if (Timer > 0 && Timer % 70 == 35)
				volleyTimer = 1;
			// low life: it starts to rewind its own path behind you (once)
			if (!frenzy && NPC.life < NPC.lifeMax * 0.25f) {
				Enter(StExecute);
				return;
			}
			if (Timer > HuntLength)
				NextAttack();
		}

		private int LungeCount => frenzy ? 5 : 3;
		private int LungeWind => (int)MathHelper.Max(18f, (frenzy ? 30f : 44f) - Sub * 5f);

		/// <summary>Coils back, aims a burning line through you — and is already there.</summary>
		private void Lunge(Player p)
		{
			int wind = LungeWind;
			int t = (int)Timer;
			if (t < wind) {
				// rear back: slow, turn to face the player
				Steer(p.Center - NPC.Center, 7f, 0.14f, 0.15f);
				jawTarget = 0.4f + 0.6f * t / wind;
				if (t < wind - 10) {
					aimDir = (p.Center + p.velocity * 8f - NPC.Center).SafeNormalize(Vector2.UnitX);
				}
				lethalFrom = NPC.Center;
				lethalDir = aimDir;
				lethalLine = MathF.Max(lethalLine, ReUtils.InverseLerp(0, wind * 0.5f, t));
				if (t == 1 && !Main.dedServ)
					SoundEngine.PlaySound(RecursionSounds.Telegraph with { Pitch = Sub * 0.12f }, NPC.Center);
				// script is drawn into its maw as it gathers itself
				if (!Main.dedServ && t % 2 == 0) {
					Vector2 maw = NPC.Center + NPC.velocity.SafeNormalize(aimDir) * 70f;
					Vector2 from = maw + Main.rand.NextVector2CircularEdge(140f, 140f);
					ParticleSystem.Glyph(from, (maw - from) / 10f, t % 4 == 0 ? ReUtils.Ember : ReUtils.PaleGold, 0.9f, 12);
				}
				charge = t / (float)wind;
			}
			else if (t < wind + 34) {
				if (t == wind) {
					NPC.velocity = aimDir * 47f * Fury;
					charge = 0f;
					tearFrom = NPC.Center;
					tearLife = 1f;
					if (!Main.dedServ) {
						SoundEngine.PlaySound(RecursionSounds.Screech, NPC.Center);
						SoundEngine.PlaySound(RecursionSounds.SonicBoom, NPC.Center);
						ScreenFX.Shake(12f);
						ScreenFX.ZoomPunch(0.04f);
						for (int r = 0; r < 3; r++)
							ParticleSystem.Ring(NPC.Center, r == 1 ? ReUtils.PaleGold : ReUtils.Ember, 0.08f + r * 0.06f, 1.1f + r * 0.02f, 18 + r * 6);
					}
				}
				tearTo = NPC.Center;
				lethal = true;
				jawTarget = 1f;
			}
			else if (t < wind + 60) {
				Steer(p.Center - NPC.Center, 18f * Fury, 0.09f, 0.1f);
			}
			else {
				Sub++;
				Timer = -1;
				if (Sub >= LungeCount)
					Enter(StHunt);
			}
		}

		private const int DiveTime = 70;
		private int PortalWarn => frenzy ? 40 : 56;

		/// <summary>
		/// Dives into a rift it tears open, vanishes — then a second rift opens on the edge of the screen, a burning
		/// line is drawn through you, and it erupts along it faster than anything should move.
		/// </summary>
		private void PortalDive(Player p)
		{
			int t = (int)Timer;
			int count = frenzy ? 3 : 2;
			if (Sub == 0 && t == 0) {
				// the entry rift opens ahead of the head
				portalIn = NPC.Center + NPC.velocity.SafeNormalize(Vector2.UnitX) * 340f;
				portalInOpen = 0.01f;
				if (!Main.dedServ)
					SoundEngine.PlaySound(RecursionSounds.Portal, portalIn);
			}
			if (Sub == 0) {
				// dive into it, everything following
				if (t < DiveTime) {
					if (!headHidden) {
						Steer(portalIn - NPC.Center, 36f, 0.2f, 0.2f);
						if (NPC.Distance(portalIn) < 50f) {
							headHidden = true;
							aimDir = NPC.velocity.SafeNormalize(Vector2.UnitX);
							if (!Main.dedServ) {
								ParticleSystem.Ring(portalIn, ReUtils.Ember, 0.15f, 1.1f, 24);
								ScreenFX.Shake(8f);
							}
						}
					}
					else
						NPC.velocity = aimDir * 40f;
					HideBehindPlane(portalIn, aimDir, hideAhead: true);
					if (!Main.dedServ && portalInOpen > 0f) {
						// the rift drinks the light around it
						Vector2 from = portalIn + Main.rand.NextVector2CircularEdge(260f, 260f);
						ParticleSystem.Glyph(from, (portalIn - from) / 14f, ReUtils.Gold, 1f, 14);
					}
					return;
				}
				EraseAll();
				portalInOpen = 0f;
				Sub = 1;
				Timer = -1;
				return;
			}
			// warn: the exit rift tears open, and the line through you burns brighter until it fires
			int warn = PortalWarn;
			if (t == 0) {
				// it always comes from above or from the sides: the field below is not its door
				float ang = Main.rand.NextFloat(0.3f, MathHelper.Pi - 0.3f);
				aimDir = ang.ToRotationVector2();
				portalOutOpen = 0.01f;
				if (!Main.dedServ)
					SoundEngine.PlaySound(RecursionSounds.Portal with { Pitch = 0.3f }, p.Center);
			}
			if (t < warn) {
				// it tracks you until the last moment, then locks
				if (t < warn - (frenzy ? 12 : 18))
					portalOut = p.Center - aimDir * 1250f;
				LayOutBehind(portalOut, aimDir);
				headHidden = true;
				lethalFrom = portalOut;
				lethalDir = aimDir;
				lethalLine = MathF.Max(lethalLine, ReUtils.InverseLerp(0, warn * 0.6f, t));
				if (t == warn - 22 && !Main.dedServ)
					SoundEngine.PlaySound(RecursionSounds.Telegraph with { Pitch = 0.4f }, p.Center);
				if (!Main.dedServ && t % 3 == 0)
					ParticleSystem.Spark(portalOut + Main.rand.NextVector2Circular(30f, 160f).RotatedBy(aimDir.ToRotation()), aimDir * Main.rand.NextFloat(2f, 6f), ReUtils.Ember, 0.8f, 20);
				NPC.velocity = aimDir * 0.01f;
				return;
			}
			if (t == warn) {
				headHidden = false;
				NPC.Center = portalOut;
				NPC.velocity = aimDir * 60f * Fury;
				if (!Main.dedServ) {
					SoundEngine.PlaySound(RecursionSounds.Erupt, portalOut);
					SoundEngine.PlaySound(RecursionSounds.SonicBoom with { Pitch = -0.2f }, portalOut);
					tearFrom = portalOut;
					tearLife = 1f;
					SoundEngine.PlaySound(RecursionSounds.Screech with { Pitch = 0.2f }, portalOut);
					ScreenFX.Shake(22f);
					ScreenFX.ZoomPunch(0.06f);
					Cinematic.Flash(0.25f);
					for (int i = 0; i < 40; i++)
						ParticleSystem.Spark(portalOut, (aimDir.ToRotation() + Main.rand.NextFloat(-0.9f, 0.9f)).ToRotationVector2() * Main.rand.NextFloat(6f, 22f), i % 3 == 0 ? ReUtils.Ember : ReUtils.PaleGold, 1.2f, 30);
				}
			}
			lethal = true;
			jawTarget = 1f;
			tearTo = NPC.Center;
			HideBehindPlane(portalOut, aimDir, hideAhead: false);
			if (t > warn + 62) {
				portalOutOpen = 0f;
				for (int i = 0; i < hidden.Length; i++)
					hidden[i] = false;
				Sub++;
				Timer = -1;
				if (Sub > count)
					Enter(StHunt);
				else {
					// next round: straight into the next exit (no new dive)
					headHidden = true;
					EraseAll();
				}
			}
		}

		/// <summary>Segments on one side of a portal plane are inside the rift.</summary>
		private void HideBehindPlane(Vector2 at, Vector2 dir, bool hideAhead)
		{
			for (int i = 0; i < Segments; i++) {
				float d = Vector2.Dot(SegPos(i) - at, dir);
				hidden[i] = hideAhead ? d > 0f : d < 0f;
			}
			if (!hideAhead)
				headHidden = Vector2.Dot(NPC.Center - at, dir) < 0f;
		}

		private void EraseAll()
		{
			headHidden = true;
			for (int i = 0; i < hidden.Length; i++)
				hidden[i] = true;
		}

		/// <summary>Lays the whole body out in a straight line behind a point (ready to erupt from it).</summary>
		private void LayOutBehind(Vector2 at, Vector2 dir)
		{
			NPC.Center = at - dir * 12f;
			Vector2 lead = NPC.Center;
			for (int i = 0; i < Segments; i++) {
				NPC s = Seg(i);
				if (s == null)
					continue;
				lead -= dir * Spacing * SegScale(i);
				s.Center = lead;
				s.rotation = dir.ToRotation();
				segOld[i] = lead;
				hidden[i] = true;
			}
		}

		/// <summary>It circles wide while walls of inscribed lances close in from the edges, one gap at a time.</summary>
		private void Cage(Player p)
		{
			int t = (int)Timer;
			coilAngle += 0.03f * Fury;
			Vector2 orbit = p.Center + coilAngle.ToRotationVector2() * new Vector2(900f, 560f);
			orbit.Y = MathF.Min(orbit.Y, Floor(orbit.X) - 200f);
			Steer(orbit - NPC.Center, 30f * Fury, 0.12f, 0.12f);
			int every = frenzy ? 50 : 62;
			int waves = frenzy ? 6 : 5;
			if (t > 20 && (t - 20) % every == 0 && (t - 20) / every < waves && Main.netMode != NetmodeID.MultiplayerClient) {
				int w = (t - 20) / every;
				bool horizontal = w % 2 == 0;
				float half = horizontal ? Main.screenHeight / 2f + 120f : Main.screenWidth / 2f + 120f;
				half = MathF.Max(half, 700f);
				float spacing = 92f;
				float gapCenter = (horizontal ? p.Center.Y : p.Center.X) + Main.rand.NextFloat(-160f, 160f);
				int dirSign = (w / 2) % 2 == 0 ? 1 : -1;
				for (float o = -half; o <= half; o += spacing) {
					float line = (horizontal ? p.Center.Y : p.Center.X) + o;
					if (MathF.Abs(line - gapCenter) < spacing * 1.15f)
						continue;
					Vector2 from = horizontal
						? new Vector2(p.Center.X - dirSign * 1150f, line)
						: new Vector2(line, p.Center.Y - dirSign * 800f);
					Vector2 dir = horizontal ? new Vector2(dirSign, 0f) : new Vector2(0f, dirSign);
					Projectile.NewProjectile(NPC.GetSource_FromAI(), from, dir, ModContent.ProjectileType<GlyphLance>(), ReUtils.ProjDamage(150), 0f, Main.myPlayer, frenzy ? 34 : 42, 30f * Fury);
				}
				if (!Main.dedServ)
					SoundEngine.PlaySound(RecursionSounds.Telegraph with { Volume = 0.4f, Pitch = -0.3f }, p.Center);
			}
			if (t > 20 + every * waves + 50)
				Enter(StHunt);
		}

		/// <summary>It wraps itself around you in a closing ring of chitin and fires inward from every plate.</summary>
		private void Coil(Player p)
		{
			int t = (int)Timer;
			if (t == 0) {
				coilAngle = (NPC.Center - p.Center).ToRotation();
				coilRadius = 820f;
				if (!Main.dedServ)
				{
					SoundEngine.PlaySound(RecursionSounds.Roar with { Volume = 0.6f, Pitch = 0.2f }, NPC.Center);
					SoundEngine.PlaySound(RecursionSounds.Constrict, p.Center);
				}
			}
			int len = frenzy ? 300 : 360;
			if (t < len) {
				coilRadius = MathHelper.Lerp(coilRadius, t < len - 60 ? 360f : 900f, t < len - 60 ? 0.02f : 0.06f);
				float speed = 34f * Fury;
				coilAngle += speed / coilRadius;
				coilCenter = p.Center;
				Vector2 target = p.Center + coilAngle.ToRotationVector2() * coilRadius;
				// where the ring would pass under the field it flattens along the surface instead: a dome
				target.Y = MathF.Min(target.Y, Floor(target.X) - 80f);
				NPC.velocity = (target - NPC.Center).SafeNormalize(Vector2.Zero) * MathF.Min(speed * 1.6f, NPC.Distance(target));
				if (!Main.dedServ)
					ScreenFX.HoldFixedZoom(0.84f, ReUtils.InverseLerp(0, 60, t) * ReUtils.InverseLerp(len, len - 40, t));
				// every plate fires inward, in a ripple around the ring
				int every = frenzy ? 42 : 52;
				if (t > 90 && t < len - 60 && t % every == 0)
					volleyTimer = 1;
				jawTarget = 0.7f;
			}
			else
				Enter(StHunt);
		}

		/// <summary>Below 60%: it rewinds — a ghost of its own path plays backwards through the arena while it hunts on.</summary>
		private void Rewind(Player p)
		{
			int t = (int)Timer;
			if (t == 0) {
				rewindUsed = true;
				if (Main.netMode != NetmodeID.MultiplayerClient && path.Count > 40) {
					pathCopy.Clear();
					pathCopy.AddRange(path);
					pathCopy.Reverse();
					int id = Projectile.NewProjectile(NPC.GetSource_FromAI(), pathCopy[0], Vector2.Zero, ModContent.ProjectileType<RewindEcho>(), ReUtils.ProjDamage(170), 0f, Main.myPlayer);
					if (id >= 0 && Main.projectile[id].ModProjectile is RewindEcho e)
						e.SetPath(pathCopy);
				}
				if (!Main.dedServ) {
					RecursionOverlay.Card("Mods.Replicant.Recursion.Rewind", 110);
					SoundEngine.PlaySound(RecursionSounds.RewindTape);
					SoundEngine.PlaySound(ReSounds.EyesClose with { Pitch = 0.4f, Volume = 0.7f });
					ScreenFX.Shake(10f);
				}
			}
			float dist = NPC.Distance(p.Center);
			Steer(p.Center - NPC.Center, (dist > 1000f ? 26f : 18f) * Fury, 0.05f);
			if (t > 0 && t % 80 == 40)
				volleyTimer = 1;
			if (t > 220)
				Enter(StHunt);
		}

		/// <summary>Below 25% (once): EXECUTE. It screams, the screen breaks, and from now on it never slows down.</summary>
		private void Execute(Player p)
		{
			int t = (int)Timer;
			NPC.velocity *= 0.93f;
			Steer(p.Center - NPC.Center, 6f, 0.05f, 0.05f);
			jawTarget = 1f;
			if (t == 1 && !Main.dedServ) {
				SoundEngine.PlaySound(RecursionSounds.Roar with { Pitch = 0.15f }, NPC.Center);
				RecursionOverlay.Card("Mods.Replicant.Recursion.Execute", 120);
				SoundEngine.PlaySound(RecursionSounds.ExecuteGlitch);
				Cinematic.Flash(0.6f);
				ScreenFX.Shake(30f);
			}
			if (t > 1 && t < 80 && !Main.dedServ)
				ScreenFX.Focus(NPC.Center, 0.35f * ReUtils.Bump(t / 80f));
			if (t >= 90) {
				frenzy = true;
				rewindUsed = true;
				bag.Clear();
				NextAttack();
			}
		}

		private void Leave()
		{
			NPC.velocity = Vector2.Lerp(NPC.velocity, new Vector2(0f, -40f), 0.05f);
			if (Timer > 150) {
				Despawn();
			}
		}

		private void Despawn()
		{
			for (int i = 0; i < Segments; i++) {
				NPC s = Seg(i);
				if (s != null)
					s.active = false;
			}
			NPC.active = false;
		}

		// ---------------------------------------------------------------- the segment volley
		private void UpdateVolley(Player p)
		{
			if (volleyTimer <= 0)
				return;
			volleyTimer++;
			// ripple from head to tail: every few segments, a needle wakes up and flies at you a beat later
			if (volleyTimer % 2 != 0)
				return;
			int k = volleyTimer / 2 - 1;
			int i = 2 + k * (State == StCoil ? 4 : 5);
			if (i >= Segments) {
				volleyTimer = 0;
				return;
			}
			if (Main.netMode != NetmodeID.MultiplayerClient && !hidden[i]) {
				Vector2 from = SegPos(i);
				float delay = 22f;
				Projectile.NewProjectile(NPC.GetSource_FromAI(), from, Vector2.Zero, ModContent.ProjectileType<VerseNeedle>(), ReUtils.ProjDamage(115), 0f, Main.myPlayer, delay, 15f * Fury);
			}
		}

		// ---------------------------------------------------------------- death
		public override bool CheckDead()
		{
			if (State == StDying && Timer >= DyingLength)
				return true;
			NPC.life = 1;
			NPC.dontTakeDamage = true;
			if (State != StDying) {
				Enter(StDying);
				coilAngle = (NPC.Center - Target.Center).ToRotation();
				portalInOpen = portalOutOpen = 0f;
				headHidden = false;
				for (int i = 0; i < hidden.Length; i++)
					hidden[i] = false;
				if (!Main.dedServ) {
					SoundEngine.PlaySound(RecursionSounds.Death, NPC.Center);
					Cinematic.Begin(NPC.Center, DyingLength + 30);
					ScreenFX.Shake(20f);
				}
			}
			return false;
		}

		private const int DyingLength = 230;

		/// <summary>It writhes, curls, and comes apart plate by plate from the tail up, until only the head screams.</summary>
		private void Dying()
		{
			int t = (int)Timer;
			coilAngle += 0.06f * (1f - t / (float)DyingLength) + 0.01f;
			Vector2 c = Target.Center + new Vector2(0f, -260f);
			Vector2 target = c + coilAngle.ToRotationVector2() * MathHelper.Lerp(420f, 120f, t / (float)DyingLength);
			target.Y = MathF.Min(target.Y, Floor(target.X) - 120f);
			Steer(target - NPC.Center, MathHelper.Lerp(26f, 6f, t / (float)DyingLength), 0.2f, 0.1f);
			jawTarget = 0.5f + 0.5f * MathF.Sin(t * 0.4f);
			if (!Main.dedServ)
				Cinematic.SetFocus(NPC.Center);
			// plates pop from the tail
			int target2 = (int)(Segments * ReUtils.InverseLerp(20, DyingLength - 40, t));
			while (dyingPopped < target2 && dyingPopped < Segments) {
				int i = Segments - 1 - dyingPopped;
				dyingPopped++;
				hidden[i] = true;
				NPC s = Seg(i);
				if (s != null && !Main.dedServ) {
					Bleed(s.Center, 14, 1.1f);
					ParticleSystem.Ring(s.Center, ReUtils.Ember, 0.05f, 1.1f, 16);
					if (i % 3 == 0)
						SoundEngine.PlaySound(RecursionSounds.Bite with { Volume = 0.5f, Pitch = 0.3f + Main.rand.NextFloat(0.3f) }, s.Center);
					ScreenFX.Shake(4f);
				}
			}
			if (t == DyingLength - 2 && !Main.dedServ) {
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 1f }, NPC.Center);
				Cinematic.Flash(1f);
				ScreenFX.Shake(40f);
				ScreenFX.ZoomPunch(0.15f);
				Bleed(NPC.Center, 80, 1.6f);
				ParticleSystem.GlyphBurst(NPC.Center, 160, 20f, 1.4f);
				for (int i = 0; i < 3; i++)
					ParticleSystem.Ring(NPC.Center, i == 1 ? ReUtils.Ember : ReUtils.PaleGold, 0.1f + i * 0.08f, 1.08f + i * 0.02f, 40 + i * 8);
			}
			if (t >= DyingLength && Main.netMode != NetmodeID.MultiplayerClient) {
				NPC.dontTakeDamage = false;
				NPC.life = 0;
				NPC.HitEffect();
				NPC.checkDead();
			}
		}

		public override void OnKill()
		{
			for (int i = 0; i < Segments; i++) {
				NPC s = Seg(i);
				if (s != null)
					s.active = false;
			}
			if (DimensionBridge.InDimension) {
				TwinsFight.OnBothDefeated();
				if (!Main.dedServ)
					EpilogueCutscene.Begin();
			}
		}

		/// <summary>Its blood is the colour of the song: black oil cored with ember and gold.</summary>
		public static void Bleed(Vector2 at, int count, float scale)
		{
			for (int i = 0; i < count; i++) {
				Vector2 v = Main.rand.NextVector2Circular(9f, 9f) * scale;
				int k = i % 4;
				if (k == 0)
					ParticleSystem.Smoke(at, v * 0.4f, new Color(10, 4, 3) * 0.9f, Main.rand.NextFloat(0.8f, 1.6f) * scale, 60);
				else if (k == 1)
					ParticleSystem.Spark(at, v, new Color(200, 40, 14), 1.2f * scale, 34).Gravity = 0.25f;
				else if (k == 2)
					ParticleSystem.Spark(at, v * 1.3f, ReUtils.Ember, scale, 28).Gravity = 0.2f;
				else
					ParticleSystem.Glyph(at, v * 0.5f, ReUtils.Gold, 0.9f * scale, 40);
			}
		}

		// ---------------------------------------------------------------- damage rules
		public override bool CanHitPlayer(Player target, ref int cooldownSlot) => !headHidden && State != StScripted && State != StDying && State != StLeave;

		public override void ModifyHitPlayer(Player target, ref Player.HurtModifiers modifiers)
		{
			// its lunges and eruptions are executions
			if (lethal)
				modifiers.FinalDamage.Flat += target.statLifeMax2 * 2f;
		}

		public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
		{
			modifiers.FinalDamage *= 1.2f;   // the head is the weak point
		}

		public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers) => modifiers.FinalDamage *= 1.2f;

		public override void HitEffect(NPC.HitInfo hit)
		{
			hurtFlash = 1f;
			if (!Main.dedServ && Main.rand.NextBool(2))
				Bleed(NPC.Center + Main.rand.NextVector2Circular(30, 30), 4, 0.8f);
		}

		// ================================================================ client effects
		private float lastSpeed;

		private void ClientEffects(Player p)
		{
			jaw = MathHelper.Lerp(jaw, jawTarget, 0.25f);
			hurtFlash *= 0.85f;
			glowPulse += 0.1f + NPC.velocity.Length() * 0.006f;
			portalInOpen = portalInOpen > 0f ? MathF.Min(1f, portalInOpen + 0.06f) : 0f;
			portalOutOpen = portalOutOpen > 0f ? MathF.Min(1f, portalOutOpen + 0.06f) : 0f;
			float speed = NPC.velocity.Length();
			// it rushes past you: the air tears
			float dist = Vector2.Distance(Main.LocalPlayer.Center, NPC.Center);
			if (!headHidden && speed > 30f && lastSpeed <= 30f && dist < 1400f)
				SoundEngine.PlaySound(RecursionSounds.Rush with { Volume = 0.6f }, NPC.Center);
			if (!headHidden && speed > 36f && dist < 320f)
				ScreenFX.Shake(2f);
			tearLife *= 0.95f;
			legPhase += 0.3f + speed * 0.012f;
			RecursionOverlay.Frenzy = MathHelper.Lerp(RecursionOverlay.Frenzy, frenzy && State != StDying ? 1f : 0f, 0.03f);
			if (State != StCoil)
				coilCenter = Vector2.Zero;
			// oil drips from the plates, script flakes off when it moves fast
			if (Main.rand.NextBool(3)) {
				int i = Main.rand.Next(Segments);
				NPC sgm = Seg(i);
				if (sgm != null && !hidden[i] && Vector2.Distance(sgm.Center, Main.LocalPlayer.Center) < 1600f) {
					if (Main.rand.NextBool(2))
						ParticleSystem.Goo(sgm.Center + Main.rand.NextVector2Circular(20f, 20f), new Vector2(0f, Main.rand.NextFloat(0.5f, 1.5f)), Main.rand.NextFloat(0.3f, 0.6f), 50).Gravity = 0.15f;
					else if (speed > 18f)
						ParticleSystem.Glyph(sgm.Center, -NPC.velocity * 0.05f + Main.rand.NextVector2Circular(1f, 1f), ReUtils.Gold, 0.8f, 30);
				}
			}
			lastSpeed = speed;
			if (!headHidden && Main.rand.NextBool(80) && dist < 1600f)
				SoundEngine.PlaySound(RecursionSounds.Chitter, NPC.Center);
			RecursionOverlay.Speed = MathHelper.Lerp(RecursionOverlay.Speed, !headHidden && dist < 1300f ? ReUtils.InverseLerp(20f, 50f, speed) : 0f, 0.15f);
			RecursionOverlay.SpeedDir = NPC.velocity.SafeNormalize(Vector2.UnitX);
			Lighting.AddLight(NPC.Center, 1f, 0.6f, 0.2f);
		}

		// ================================================================ drawing (the whole animal, from the system hook)
		public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => false;

		public void DrawWorm(SpriteBatch sb)
		{
			Vector2 sp = Main.screenPosition;
			float time = Main.GlobalTimeWrappedHourly;
			Texture2D body = RTex.Body, bodyGlow = RTex.BodyGlow, tail = RTex.Tail, tailGlow = RTex.TailGlow, head = RTex.Head, headGlow = RTex.HeadGlow;
			int bfh = body.Height / BodyFrames, tfh = tail.Height / TailFrames, hfh = head.Height / HeadFrames;

			DrawPortal(sb, portalIn, portalInOpen, aimDir, time);
			DrawPortal(sb, portalOut, portalOutOpen, aimDir, time);
			DrawLethalLine(sb, time);
			DrawTear(sb, time);
			DrawCoilRing(sb, time);

			// tail first, head last (on top)
			for (int i = Segments - 1; i >= 0; i--) {
				NPC s = Seg(i);
				if (s == null || hidden[i])
					continue;
				Vector2 pos = s.Center - sp;
				if (pos.X < -300 || pos.X > Main.screenWidth / Main.GameViewMatrix.Zoom.X + 300 || pos.Y < -300 || pos.Y > Main.screenHeight / Main.GameViewMatrix.Zoom.Y + 300)
					continue;
				bool isTail = i == Segments - 1;
				Texture2D tex = isTail ? tail : body;
				Texture2D glow = isTail ? tailGlow : bodyGlow;
				int fh = isTail ? tfh : bfh;
				int frame = isTail ? ((int)(time * 10f) % TailFrames) : ((int)(legPhase + i * 1.6f) % BodyFrames + BodyFrames) % BodyFrames;
				Rectangle src = new(0, frame * fh, tex.Width, fh);
				Vector2 origin = new(tex.Width / 2f, fh / 2f);
				float scale = SegScale(i);
				Vector2 vel = s.Center - segOld[i];
				float spd = vel.Length();
				float wave = 0.45f + 0.55f * MathF.Max(0f, MathF.Sin(glowPulse - i * 0.45f));
				Color lit = Color.Lerp(Lighting.GetColor(s.Center.ToTileCoordinates()), Color.White, 0.55f);
				// motion blur: smeared afterimages along the motion
				if (spd > 10f) {
					for (int k = 3; k >= 1; k--) {
						Vector2 off = -vel * (k * 0.33f);
						sb.Draw(tex, pos + off, src, lit * (0.18f / k), s.rotation, origin, scale, SpriteEffects.None, 0f);
					}
				}
				sb.Draw(tex, pos, src, lit, s.rotation, origin, scale, SpriteEffects.None, 0f);
				sb.Draw(glow, pos, src, Color.White.Additive(wave), s.rotation, origin, scale, SpriteEffects.None, 0f);
				if (hurtFlash > 0.05f)
					sb.Draw(tex, pos, src, new Color(255, 200, 140).Additive(hurtFlash * 0.35f), s.rotation, origin, scale, SpriteEffects.None, 0f);
			}

			if (!headHidden) {
				Vector2 pos = NPC.Center - sp;
				int frame = Math.Clamp((int)MathF.Round(jaw * (HeadFrames - 1)), 0, HeadFrames - 1);
				Rectangle src = new(0, frame * hfh, head.Width, hfh);
				Vector2 origin = new(head.Width * 0.42f, hfh / 2f);
				float scale = 1.45f;
				Color lit = Color.Lerp(Lighting.GetColor(NPC.Center.ToTileCoordinates()), Color.White, 0.6f);
				float spd = NPC.velocity.Length();
				if (spd > 10f) {
					for (int k = 4; k >= 1; k--)
						sb.Draw(head, pos - NPC.velocity * (k * 0.3f), src, lit * (0.2f / k), NPC.rotation, origin, scale, SpriteEffects.None, 0f);
				}
				// a chromatic tear on the head when it is at full speed
				if (spd > 34f) {
					Vector2 n = new(-MathF.Sin(NPC.rotation), MathF.Cos(NPC.rotation));
					sb.Draw(head, pos + n * 4f, src, new Color(255, 60, 20).Additive(0.35f), NPC.rotation, origin, scale, SpriteEffects.None, 0f);
					sb.Draw(head, pos - n * 4f, src, new Color(255, 220, 120).Additive(0.25f), NPC.rotation, origin, scale, SpriteEffects.None, 0f);
				}
				DrawCrown(sb, pos, time);
				sb.Draw(head, pos, src, lit, NPC.rotation, origin, scale, SpriteEffects.None, 0f);
				sb.Draw(headGlow, pos, src, Color.White.Additive(0.85f + 0.15f * MathF.Sin(time * 8f)), NPC.rotation, origin, scale, SpriteEffects.None, 0f);
				if (hurtFlash > 0.05f)
					sb.Draw(head, pos, src, new Color(255, 200, 140).Additive(hurtFlash * 0.4f), NPC.rotation, origin, scale, SpriteEffects.None, 0f);
				// its great eyes flare when it means to kill
				Vector2 fwd = NPC.rotation.ToRotationVector2(), side = new(-fwd.Y, fwd.X);
				float stare = MathF.Max(charge, lethal ? 1f : 0f);
				for (int e = -1; e <= 1; e += 2) {
					Vector2 eye = pos + fwd * 56f + side * e * 14f;
					FX.Bloom(sb, eye, ReUtils.Ember, 14f + stare * 20f, 0.5f + stare * 0.4f);
					if (stare > 0.05f)
						FX.Flare(sb, eye, ReUtils.PaleGold, 0.5f + stare * 0.8f, stare, NPC.rotation, 0.35f);
				}
				if (charge > 0.01f)
					FX.Bloom(sb, pos + fwd * 90f, ReUtils.PaleGold, 30f + charge * 70f, charge * 0.7f);
				Vector2 maw = pos + NPC.rotation.ToRotationVector2() * 80f;
				FX.Bloom(sb, maw, ReUtils.Ember, 50f + jaw * 40f, 0.25f + jaw * 0.35f);
				FX.Bloom(sb, pos + NPC.rotation.ToRotationVector2() * 30f, ReUtils.Gold, 120f, 0.12f);
			}
		}

		public const int HeadFrames = 6, BodyFrames = 8, TailFrames = 4;

		/// <summary>A crown of its own script turning behind the head, and a thin halo — the verse wears it.</summary>
		private void DrawCrown(SpriteBatch sb, Vector2 pos, float time)
		{
			Vector2 back = pos - NPC.rotation.ToRotationVector2() * 30f;
			float a = 0.6f + 0.2f * MathF.Sin(time * 3f) + (frenzy ? 0.2f : 0f);
			FX.Ring(sb, back, ReUtils.Gold, 104f, 0.18f * a);
			for (int g = 0; g < 14; g++) {
				float ang = g / 14f * MathHelper.TwoPi + time * (frenzy ? 1.4f : 0.7f);
				Vector2 q = back + new Vector2(MathF.Cos(ang) * 112f, MathF.Sin(ang) * 112f);
				float flick = 0.5f + 0.5f * MathF.Sin(time * 6f + g * 1.7f);
				sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(g * 5 + (int)(time * 2f)), (g % 3 == 0 ? ReUtils.Ember : ReUtils.PaleGold).Additive(a * flick), ang + MathHelper.PiOver2, new Vector2(7, 9), 1.15f, SpriteEffects.None, 0f);
			}
		}

		/// <summary>Where it lunged, the air stays torn for a moment: a burning scar with script falling out of it.</summary>
		private void DrawTear(SpriteBatch sb, float time)
		{
			if (tearLife < 0.03f)
				return;
			Vector2 a = tearFrom - Main.screenPosition, b = tearTo - Main.screenPosition;
			Vector2 d = b - a;
			float len = d.Length();
			if (len < 10f)
				return;
			float rot = d.ToRotation();
			FX.Beam(sb, a, rot, len, 40f * tearLife, ReUtils.Ember, 0.25f * tearLife, ReTex.Thread);
			FX.Beam(sb, a, rot, len, 6f * tearLife, ReUtils.PaleGold, 0.8f * tearLife, ReTex.Thread);
			Vector2 n = d / len;
			for (float s = 0f; s < len; s += 60f) {
				Vector2 q = a + n * s + new Vector2(0f, (time * 60f + s) % 40f);
				sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame((int)(s / 60f) + 5), ReUtils.Gold.Additive(0.7f * tearLife), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
		}

		/// <summary>During the coil: the safe ring it leaves you is traced in light, so you always see your space.</summary>
		private void DrawCoilRing(SpriteBatch sb, float time)
		{
			if (coilCenter == Vector2.Zero || coilRadius <= 0f)
				return;
			Texture2D px = TextureAssets.MagicPixel.Value;
			Vector2 c = coilCenter - Main.screenPosition;
			float r = coilRadius - 70f;
			for (int i = 0; i < 90; i++) {
				float ang = i / 90f * MathHelper.TwoPi + time * 0.4f;
				Vector2 q = c + ang.ToRotationVector2() * r;
				float floorY = Floor(q.X + Main.screenPosition.X) - Main.screenPosition.Y - 20f;
				if (q.Y > floorY)
					continue;
				if (i % 2 == 0)
					sb.Draw(px, q, new Rectangle(0, 0, 1, 1), ReUtils.Gold.Additive(0.35f), ang + MathHelper.PiOver2, new Vector2(0.5f), new Vector2(8f, 2f), SpriteEffects.None, 0f);
				if (i % 15 == 0)
					sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(i / 15 + (int)time), ReUtils.PaleGold.Additive(0.5f), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
		}

		private static void DrawPortal(SpriteBatch sb, Vector2 at, float open, Vector2 dir, float time)
		{
			if (open <= 0f)
				return;
			Vector2 c = at - Main.screenPosition;
			float o = ReUtils.EaseOutBack(open);
			float rot = dir.ToRotation();
			// a vertical tear across the direction of travel, black inside, burning at the rim
			FX.Smoke(sb, c, Color.Black * (0.9f * o), 150f * o, rot, 0.45f);
			FX.Bloom(sb, c, ReUtils.Ember, 220f * o, 0.35f, 0.35f, 1f, rot);
			FX.Bloom(sb, c, ReUtils.PaleGold, 120f * o, 0.4f, 0.18f, 1f, rot);
			Texture2D px = TextureAssets.MagicPixel.Value;
			for (int i = 0; i < 40; i++) {
				float a = i / 40f * MathHelper.TwoPi + time * 2f;
				Vector2 e = new Vector2(MathF.Cos(a) * 40f, MathF.Sin(a) * 190f) * o;
				Vector2 q = c + e.RotatedBy(rot);
				sb.Draw(px, q, new Rectangle(0, 0, 1, 1), ReUtils.Gold.Additive(0.8f), rot, new Vector2(0.5f), 4f, SpriteEffects.None, 0f);
				if (i % 5 == 0)
					sb.Draw(ReTex.GlyphAtlasGlow, c + (e * 1.25f).RotatedBy(rot), ReTex.GlyphFrame(i + (int)(time * 6f)), ReUtils.PaleGold.Additive(0.8f), 0f, new Vector2(7, 9), 1.2f, SpriteEffects.None, 0f);
			}
		}

		/// <summary>The execution line: ember chevrons racing along it, a white core that hardens as the moment nears.</summary>
		private void DrawLethalLine(SpriteBatch sb, float time)
		{
			if (lethalLine < 0.02f)
				return;
			Texture2D px = TextureAssets.MagicPixel.Value;
			Vector2 c = lethalFrom - Main.screenPosition;
			float rot = lethalDir.ToRotation();
			float a = lethalLine;
			FX.Beam(sb, c, rot, 3200f, 34f * a, ReUtils.Ember, 0.18f * a, ReTex.Thread);
			FX.Beam(sb, c, rot, 3200f, 3f + 3f * a, Color.White, 0.6f * a, ReTex.Thread);
			for (float d = (time * 900f) % 70f; d < 3200f; d += 70f) {
				Vector2 q = c + lethalDir * d;
				for (int s = -1; s <= 1; s += 2) {
					Vector2 arm = (rot + MathHelper.Pi + s * 0.6f).ToRotationVector2() * 14f;
					sb.Draw(px, q, new Rectangle(0, 0, 1, 1), new Color(255, 90, 30).Additive(0.85f * a), (arm).ToRotation(), new Vector2(0f, 0.5f), new Vector2(14f, 3f), SpriteEffects.None, 0f);
				}
			}
		}
	}

	/// <summary>A plate of Recursion's body. All behaviour and drawing is the head's.</summary>
	public class RecursionBody : ModNPC
	{
		public override string Texture => "Replicant/Assets/Textures/NPCs/Recursion/RecursionBody";
		protected virtual float DamageTaken => 0.7f;

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = 4;
			NPCID.Sets.ImmuneToRegularBuffs[Type] = true;
			NPCID.Sets.CantTakeLunchMoney[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true });
		}

		public override void SetDefaults()
		{
			NPC.width = 62;
			NPC.height = 62;
			NPC.lifeMax = 125000;
			NPC.damage = 120;
			NPC.defense = 70;
			NPC.knockBackResist = 0f;
			NPC.noGravity = true;
			NPC.noTileCollide = true;
			NPC.aiStyle = -1;
			NPC.lavaImmune = true;
			NPC.netAlways = true;
			NPC.dontCountMe = true;
			NPC.npcSlots = 0f;
			NPC.HitSound = RecursionSounds.Hit;
			NPC.DeathSound = null;
		}

		public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment) => NPC.damage = (int)(NPC.damage * 0.8f);

		protected RecursionHead Head => NPC.realLife >= 0 && Main.npc[NPC.realLife].active && Main.npc[NPC.realLife].ModNPC is RecursionHead h ? h : null;
		protected int Index => (int)NPC.ai[1];

		public override bool CheckActive() => false;

		public override void AI()
		{
			RecursionHead h = Head;
			if (h == null) {
				NPC.active = false;
				return;
			}
			bool hid = h.hidden[Math.Clamp(Index, 0, RecursionHead.Segments - 1)];
			NPC.dontTakeDamage = h.NPC.dontTakeDamage || hid || h.State == RecursionHead.StDying;
			NPC.target = h.NPC.target;
		}

		public override bool CanHitPlayer(Player target, ref int cooldownSlot)
		{
			RecursionHead h = Head;
			if (h == null)
				return false;
			return !h.hidden[Math.Clamp(Index, 0, RecursionHead.Segments - 1)] && h.State != RecursionHead.StScripted && h.State != RecursionHead.StDying && h.State != RecursionHead.StLeave;
		}

		public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
		{
			modifiers.FinalDamage *= DamageTaken;
			// piercing weapons shred worms: they bite less here
			if (projectile.penetrate < 0 || projectile.penetrate > 1)
				modifiers.FinalDamage *= 0.55f;
		}

		public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers) => modifiers.FinalDamage *= DamageTaken;

		public override void HitEffect(NPC.HitInfo hit)
		{
			if (!Main.dedServ && Main.rand.NextBool(3))
				RecursionHead.Bleed(NPC.Center, 3, 0.7f);
		}

		public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) => false;
		public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => false;
	}

	public class RecursionTail : RecursionBody
	{
		public override string Texture => "Replicant/Assets/Textures/NPCs/Recursion/RecursionTail";
		protected override float DamageTaken => 1f;

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = 2;
			NPCID.Sets.ImmuneToRegularBuffs[Type] = true;
			NPCID.Sets.CantTakeLunchMoney[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true });
		}
	}

	public static class RTex
	{
		private const string P = "Replicant/Assets/Textures/NPCs/Recursion/";
		private static Texture2D Get(string n) => ModContent.Request<Texture2D>(P + n, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
		public static Texture2D Head => Get("RecursionHead");
		public static Texture2D HeadGlow => Get("RecursionHead_Glow");
		public static Texture2D Body => Get("RecursionBody");
		public static Texture2D BodyGlow => Get("RecursionBody_Glow");
		public static Texture2D Tail => Get("RecursionTail");
		public static Texture2D TailGlow => Get("RecursionTail_Glow");
	}

	public class RecursionBossBar : ModBossBar
	{
		public override ReLogic.Content.Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame) => ModContent.Request<Texture2D>("Replicant/Assets/Textures/NPCs/Recursion/RecursionHead_Head_Boss");

		public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, ref BossBarDrawParams drawParams)
		{
			if (npc.ModNPC is RecursionHead h && h.State == RecursionHead.StScripted)
				return false;
			return true;
		}
	}
}
