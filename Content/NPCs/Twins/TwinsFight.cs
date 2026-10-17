using System;
using Microsoft.Xna.Framework;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace Replicant.Content.NPCs.Twins
{
	/// <summary>
	/// Phase 2 timeline. Everything is measured in ticks since the twins appeared, which is also when
	/// "Song of the Ancients" starts — so story beats land on the music:
	///   0:00–0:15  the twins form out of Re's shards
	///   0:15–2:00  the false fight: they can be hurt, but never quite enough
	///   2:00.5     the song drops out — revelation cutscene, their wounds rewind
	///   2:20       they awaken (the sky splits)
	///   2:25       the real fight
	/// </summary>
	public static class TwinsTimeline
	{
		public const int IntroEnd = 300;
		public const int GlitchStart = 100 * 60;        // boss bars start lying
		public const int RevealStart = 7230;            // 2:00.5 — the music's breath
		public const int RewindStart = 7650;
		public const int RewindEnd = 8100;
		public const int TransformAt = 8400;            // 2:20
		public const int RevealEnd = 8700;              // 2:25
		public const float FalseLifeFloor = 0.12f;
	}

	public enum TwinStage { Intro, False, Reveal, True }

	public class TwinsFight : ModSystem
	{
		public static int RequiemType => ModContent.NPCType<Requiem>();
		public static int RefrainType => ModContent.NPCType<Refrain>();

		public static bool AnyTwinAlive { get; private set; }
		/// <summary>Movements already unveiled in this fight (cleared at the awakening, so each returns as a reprise).</summary>
		public static readonly System.Collections.Generic.HashSet<int> Revealed = new();
		/// <summary>Set by the testing item: spawn the sisters once we arrive in the dimension.</summary>
		public static bool SpawnOnArrival;
		public static Requiem Leader { get; private set; }
		public static Refrain Sister { get; private set; }
		/// <summary>Set when this world must send the player home after the twins fall (dimension).</summary>
		private static int returnHomeTimer = -1;
		private static int lastDialogueTick = -1;

		public static bool ReturningHome => returnHomeTimer > 0;

		public static int FightTicks => Leader != null ? Leader.FightTicks : Sister?.FightTicks ?? 0;

		public static TwinStage Stage {
			get {
				int t = FightTicks;
				if (t < TwinsTimeline.IntroEnd)
					return TwinStage.Intro;
				if (t < TwinsTimeline.RevealStart)
					return TwinStage.False;
				if (t < TwinsTimeline.RevealEnd)
					return TwinStage.Reveal;
				return TwinStage.True;
			}
		}

		/// <summary>Spawn both sisters around a point (server / single player only).</summary>
		public static void SpawnTwins(Vector2 around, int target)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || NPC.AnyNPCs(RequiemType))
				return;
			int a = NPC.NewNPC(NPC.GetBossSpawnSource(target), (int)around.X - 150, (int)around.Y - 220, RequiemType, 0, Target: target);
			int b = NPC.NewNPC(NPC.GetBossSpawnSource(target), (int)around.X + 150, (int)around.Y - 220, RefrainType, 0, Target: target);
			if (a >= 0 && a < Main.maxNPCs)
				Main.npc[a].netUpdate = true;
			if (b >= 0 && b < Main.maxNPCs)
				Main.npc[b].netUpdate = true;
		}

		public override void OnWorldLoad()
		{
			returnHomeTimer = -1;
			lastDialogueTick = -1;
			Subtitles.Clear();
		}

		public override void PreUpdateEntities()
		{
			Leader = null;
			Sister = null;
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.ModNPC is Requiem r)
					Leader = r;
				else if (n.ModNPC is Refrain f)
					Sister = f;
			}
			bool was = AnyTwinAlive;
			AnyTwinAlive = Leader != null || Sister != null;
			if (!AnyTwinAlive && was) {
				Revealed.Clear();
				AttackReveal.ResetCount();
			}
			if (!AnyTwinAlive && SpawnOnArrival && DimensionBridge.InDimension && !Main.gameMenu && Main.netMode == NetmodeID.SinglePlayer) {
				SpawnOnArrival = false;
				SpawnTwins(Main.LocalPlayer.Bottom, Main.myPlayer);
			}
		}

		public override void PostUpdateEverything()
		{
			if (returnHomeTimer > 0 && --returnHomeTimer == 0) {
				returnHomeTimer = -1;
				if (DimensionBridge.InDimension)
					DimensionBridge.ReturnHome();
			}
			if (Main.dedServ)
				return;
			if (!AnyTwinAlive) {
				lastDialogueTick = -1;
				return;
			}
			RunScript();
		}

		/// <summary>Client side: dialogue and cinematic beats keyed to the fight clock.</summary>
		private static void RunScript()
		{
			int t = FightTicks;
			if (t == lastDialogueTick)
				return;
			if (t - lastDialogueTick > 60 || t < lastDialogueTick)
				lastDialogueTick = t - 1;   // the clock was skipped: don't replay every beat at once
			for (int k = lastDialogueTick + 1; k <= t; k++)
				Beat(k);
			lastDialogueTick = t;
		}

		private static void Say(int speaker, string key, int duration = 300) => Subtitles.Push(speaker, "Mods.Replicant.Twins.Lines." + key, duration);

		private static void Beat(int t)
		{
			switch (t) {
				case 60: Say(Subtitles.Refrain, "Intro1", 240); break;
				case 200: AttackReveal.Show(Subtitles.Both, "Intro", MidPoint, bigCard: true); break;
				case 330: Say(Subtitles.Requiem, "Intro2", 220); break;
				case 580: Say(Subtitles.Refrain, "Intro3", 260); break;
				case 30 * 60: Say(Subtitles.Requiem, "False1"); break;
				case 50 * 60: Say(Subtitles.Refrain, "False2", 240); break;
				case 70 * 60: Say(Subtitles.Requiem, "False3", 330); break;
				case 90 * 60: Say(Subtitles.Refrain, "False4"); break;
				case 108 * 60: Say(Subtitles.Requiem, "False5", 300); break;
				case TwinsTimeline.RevealStart + 160: Say(Subtitles.Refrain, "Reveal1", 360); break;
				case TwinsTimeline.RevealStart + 560: Say(Subtitles.Requiem, "Reveal2", 400); break;
				case TwinsTimeline.RevealStart + 980: Say(Subtitles.Both, "Reveal3", 320); break;
				case TwinsTimeline.RevealEnd + 120: Say(Subtitles.Refrain, "True1", 220); break;
				case TwinsTimeline.RevealEnd + 60 * 40: Say(Subtitles.Requiem, "True2", 260); break;
				case TwinsTimeline.RevealEnd + 60 * 90: Say(Subtitles.Refrain, "True3", 260); break;
				case TwinsTimeline.RevealEnd + 60 * 150: Say(Subtitles.Requiem, "True4", 300); break;
			}
			if (t == TwinsTimeline.RevealStart) {
				Vector2 mid = MidPoint;
				Cinematic.Begin(mid, TwinsTimeline.RevealEnd - TwinsTimeline.RevealStart);
				SoundEngine.PlaySound(ReSounds.EyesClose with { Volume = 0.6f });
			}
			if (t == TwinsTimeline.TransformAt + 40)
				AttackReveal.Show(Subtitles.Both, "Awakening", MidPoint, bigCard: true);
			if (t == TwinsTimeline.TransformAt) {
				SoundEngine.PlaySound(TwinSounds.Transform);
				DimensionSystem.AwakenTarget = 1f;
				DimensionSystem.LilyGlowTarget = 1f;
				ScreenFX.Shake(30f);
				ScreenFX.ZoomPunch(0.2f);
				Cinematic.Flash(0.9f);
				Vector2 m = MidPoint;
				for (int i = 0; i < 4; i++)
					ParticleSystem.Ring(m, i % 2 == 0 ? ReUtils.PaleGold : ReUtils.Ember, 0.2f + i * 0.1f, 1.07f + i * 0.02f, 50 + i * 10);
				ParticleSystem.GlyphBurst(m, 120, 20f, 1.4f);
			}
		}

		public static Vector2 MidPoint {
			get {
				if (Leader != null && Sister != null)
					return (Leader.NPC.Center + Sister.NPC.Center) / 2f;
				return Leader?.NPC.Center ?? Sister?.NPC.Center ?? Main.LocalPlayer.Center;
			}
		}

		/// <summary>Called when the last sister falls.</summary>
		public static void OnBothDefeated()
		{
			ReDownedSystem.DownedTwins = true;
			if (!Main.dedServ) {
				Main.LocalPlayer.GetModPlayer<ReplicantPlayer>().PendingReward = true;
				if (DimensionBridge.InDimension)
					returnHomeTimer = 60 * 8;
			}
		}
	}

	/// <summary>Per-player bookkeeping: rewards are handed out in the home world (the dimension isn't kept).</summary>
	public class ReplicantPlayer : ModPlayer
	{
		public bool PendingReward;

		public override void SaveData(TagCompound tag)
		{
			if (PendingReward)
				tag["pendingReward"] = true;
		}

		public override void LoadData(TagCompound tag) => PendingReward = tag.ContainsKey("pendingReward");

		public override void PostUpdate()
		{
			if (!PendingReward || Player.whoAmI != Main.myPlayer || DimensionSystem.InDimension || TwinsFight.AnyTwinAlive)
				return;
			PendingReward = false;
			ReDownedSystem.DownedRe = true;
			ReDownedSystem.DownedTwins = true;
			// placeholder rewards until the loot pool is designed
			Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.GreaterHealingPotion, 10);
			Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.PlatinumCoin, 2);
		}
	}

	public static class TwinSounds
	{
		private const string P = "Replicant/Assets/Sounds/Twins/";
		public static readonly Terraria.Audio.SoundStyle Shatter = new(P + "OrbShatter", 3) { Volume = 0.55f, PitchVariance = 0.2f, MaxInstances = 8 };
		public static readonly Terraria.Audio.SoundStyle Clink = new(P + "OrbClink") { Volume = 0.4f, PitchVariance = 0.3f, MaxInstances = 4 };
		public static readonly Terraria.Audio.SoundStyle Bell = new(P + "BellToll") { Volume = 0.8f, MaxInstances = 4 };
		public static readonly Terraria.Audio.SoundStyle Dash = new(P + "TwinDash") { Volume = 0.7f, PitchVariance = 0.1f, MaxInstances = 3 };
		public static readonly Terraria.Audio.SoundStyle Cast = new(P + "TwinCast") { Volume = 0.6f, MaxInstances = 3 };
		public static readonly Terraria.Audio.SoundStyle Hurt = new(P + "TwinHurt", 3) { Volume = 0.45f, PitchVariance = 0.1f, MaxInstances = 3 };
		public static readonly Terraria.Audio.SoundStyle Chain = new(P + "ChainForm") { Volume = 0.6f, MaxInstances = 3 };
		public static readonly Terraria.Audio.SoundStyle Beam = new(P + "BeamFire") { Volume = 0.7f, MaxInstances = 2 };
		public static readonly Terraria.Audio.SoundStyle Transform = new(P + "Transform") { Volume = 1f };
		public static readonly Terraria.Audio.SoundStyle Death = new(P + "TwinDeath") { Volume = 0.9f };
		public static Terraria.Audio.SoundStyle Note(int i) => new(P + "Note" + (Math.Abs(i) % 5 + 1)) { Volume = 0.45f, MaxInstances = 6 };
	}

	/// <summary>Song of the Ancients while the twins live.</summary>
	public class TwinsMusic : ModSceneEffect
	{
		public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;
		public override int Music => MusicLoader.GetMusicSlot(Mod, "Assets/Music/SongOfTheAncients");
		public override bool IsSceneEffectActive(Player player) => TwinsFight.AnyTwinAlive && !RiddleCutscene.Active;
	}
}
