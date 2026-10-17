using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.ReBoss
{
	/// <summary>
	/// "Re" — phase one of the Replicant boss. A hooded shade of black slime veined with golden glyphs.
	/// AI lives in Re.AI.cs, rendering in Re.Draw.cs.
	/// </summary>
	[AutoloadBossHead]
	public partial class Re : ModNPC
	{
		public enum AIState
		{
			Intro = 0,
			Reposition = 1,
			GlyphBarrage = 2,
			TendrilDash = 3,
			RuneCircle = 4,
			GooRain = 5,
			LanceStorm = 6,
			Unravel = 7,
			Dying = 8,
		}

		public const int IdleFrames = 8;
		public const int AttackFrames = 6;
		public const int TotalFrames = IdleFrames + AttackFrames;

		public override string Texture => "Replicant/Assets/Textures/NPCs/Re/Re";
		public override string BossHeadTexture => "Replicant/Assets/Textures/NPCs/Re/Re_Head_Boss";

		public AIState State {
			get => (AIState)(int)NPC.ai[0];
			set => NPC.ai[0] = (int)value;
		}
		public ref float Timer => ref NPC.ai[1];
		public ref float SubCounter => ref NPC.ai[2];
		public ref float Misc => ref NPC.ai[3];

		/// <summary>True after the 50% "Unravel" transition.</summary>
		public bool Unraveled;
		/// <summary>Set (by the cutscene or the server packet) when the death sequence has finished.</summary>
		public bool CutsceneFinished;
		/// <summary>Body hidden (set by the cutscene at the moment of the burst).</summary>
		public bool Vanished;

		private Vector2 dashDirection = Vector2.UnitX;
		private int sideSign = 1;
		private readonly List<AIState> attackBag = new();
		private AIState lastAttack = AIState.Reposition;

		// ------------------------------------------------------------- client-side visual state
		private ReTentacle[] tentacles;
		private float mawOpen;          // eased 0..1
		private float mawTarget;
		private float eyeFlare;         // 0..1
		private float glowBoost;        // extra glyph brightness
		private float tremble;          // jitter amount
		private float telegraphLine;    // dash telegraph opacity
		private float visualTime;
		private bool startedCutscene;

		public override void SetStaticDefaults()
		{
			Main.npcFrameCount[Type] = TotalFrames;
			NPCID.Sets.MPAllowedEnemies[Type] = true;
			NPCID.Sets.BossBestiaryPriority.Add(Type);
			NPCID.Sets.TrailCacheLength[Type] = 12;
			NPCID.Sets.TrailingMode[Type] = 1;
			NPCID.Sets.ImmuneToRegularBuffs[Type] = true;
			NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers {
				PortraitScale = 0.75f,
				PortraitPositionYOverride = 10f,
			});
		}

		public override void SetDefaults()
		{
			NPC.width = 96;
			NPC.height = 104;
			NPC.lifeMax = 92000;
			NPC.damage = 140;
			NPC.defense = 40;
			NPC.knockBackResist = 0f;
			NPC.noGravity = true;
			NPC.noTileCollide = true;
			NPC.boss = true;
			NPC.npcSlots = 15f;
			NPC.aiStyle = -1;
			NPC.lavaImmune = true;
			NPC.value = Item.buyPrice(gold: 30);
			NPC.HitSound = ReSounds.Hurt;
			NPC.DeathSound = null;
			NPC.Opacity = 0f;
			NPC.dontTakeDamage = true;
			NPC.SpawnWithHigherTime(30);
			if (!Main.dedServ)
				Music = MusicLoader.GetMusicSlot(Mod, "Assets/Music/AtTheSpeedOfLight");
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
				new FlavorTextBestiaryInfoElement("Mods.Replicant.Bestiary.Re"),
			});
		}

		public override void BossLoot(ref int potionType) => potionType = ItemID.GreaterHealingPotion;

		public override void OnKill()
		{
			NPC.SetEventFlagCleared(ref ReDownedSystem.DownedRe, -1);
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.WorldData);
			// Re shatters into her two daughters. When the cutscene carried the player into the dimension,
			// it spawns them there itself.
			if (!RiddleCutscene.SpawnHandledByCutscene)
				Twins.TwinsFight.SpawnTwins(NPC.Center, NPC.target);
		}

		public override bool CanHitPlayer(Player target, ref int cooldownSlot)
		{
			cooldownSlot = ImmunityCooldownID.Bosses;
			return State is not (AIState.Intro or AIState.Dying or AIState.Unravel) && NPC.Opacity > 0.8f;
		}

		/// <summary>Re never dies normally: the killing blow starts the riddle cutscene instead.</summary>
		public override bool CheckDead()
		{
			if (CutsceneFinished)
				return true;
			if (State != AIState.Dying) {
				State = AIState.Dying;
				Timer = 0;
				SubCounter = 0;
				NPC.velocity *= 0.2f;
				NPC.netUpdate = true;
			}
			NPC.life = 1;
			NPC.dontTakeDamage = true;
			NPC.active = true;
			return false;
		}

		public override bool CheckActive() => false;

		public override void HitEffect(NPC.HitInfo hit)
		{
			if (Main.dedServ)
				return;
			int n = Math.Clamp(hit.Damage / 400, 1, 6);
			for (int i = 0; i < n; i++) {
				Vector2 p = NPC.Center + Main.rand.NextVector2Circular(NPC.width * 0.4f, NPC.height * 0.4f);
				ParticleSystem.Goo(p, Main.rand.NextVector2Circular(4f, 4f) - Vector2.UnitY * 2f, Main.rand.NextFloat(0.8f, 1.3f));
				if (Main.rand.NextBool(2))
					ParticleSystem.Glyph(p, Main.rand.NextVector2Circular(3f, 3f), ReUtils.Gold, 0.8f, 30);
			}
			glowBoost = MathF.Max(glowBoost, 0.35f);
		}

		public override void SendExtraAI(BinaryWriter writer)
		{
			writer.Write(Unraveled);
			writer.Write(CutsceneFinished);
			writer.WriteVector2(dashDirection);
			writer.Write((sbyte)sideSign);
		}

		public override void ReceiveExtraAI(BinaryReader reader)
		{
			Unraveled = reader.ReadBoolean();
			CutsceneFinished = reader.ReadBoolean();
			dashDirection = reader.ReadVector2();
			sideSign = reader.ReadSByte();
		}

		public override void FindFrame(int frameHeight)
		{
			NPC.frameCounter++;
			int frame;
			if (mawOpen > 0.15f) {
				// opening frames 8..11 follow mawOpen, then 11..13 shimmer
				if (mawOpen < 0.95f)
					frame = IdleFrames + (int)(mawOpen * 3.99f);
				else
					frame = IdleFrames + 3 + (int)(NPC.frameCounter / 5 % 3);
			}
			else {
				frame = (int)(NPC.frameCounter / 7 % IdleFrames);
			}
			NPC.frame.Y = frame * frameHeight;
		}

		/// <summary>Called by the riddle cutscene when it's done (single player) or by the packet handler (server).</summary>
		public void FinishDeathSequence()
		{
			CutsceneFinished = true;
			if (Main.netMode == NetmodeID.MultiplayerClient)
				return;
			NPC.dontTakeDamage = false;
			NPC.life = 0;
			NPC.HitEffect();
			NPC.checkDead();
			NPC.netUpdate = true;
		}

		public static Re FindActive()
		{
			int type = ModContent.NPCType<Re>();
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.type == type && n.ModNPC is Re re)
					return re;
			}
			return null;
		}
	}
}
