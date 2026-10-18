using Microsoft.Xna.Framework;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Content.NPCs.ReBoss;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Items
{
	/// <summary>Summons Re. A black tablet that is warm to the touch.</summary>
	public class SealedVerse : ModItem
	{
		public override string Texture => "Replicant/Assets/Textures/Items/SealedVerse";

		public override void SetStaticDefaults()
		{
			Item.ResearchUnlockCount = 1;
			ItemID.Sets.SortingPriorityBossSpawns[Type] = 12;
		}

		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 32;
			Item.maxStack = 1;
			Item.rare = ItemRarityID.Red;
			Item.useAnimation = 40;
			Item.useTime = 40;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.consumable = false;
		}

		public override bool CanUseItem(Player player) => !NPC.AnyNPCs(ModContent.NPCType<Re>());

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI == Main.myPlayer) {
				SoundEngine.PlaySound(ReSounds.Sigil, player.Center);
				int type = ModContent.NPCType<Re>();
				if (Main.netMode != NetmodeID.MultiplayerClient)
					NPC.SpawnOnPlayer(player.whoAmI, type);
				else
					NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
			}
			return true;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Book)
				.AddIngredient(ItemID.Ectoplasm, 5)
				.AddIngredient(ItemID.SoulofNight, 10)
				.AddTile(TileID.Bookcases)
				.Register();
		}
	}

	/// <summary>
	/// Testing tool: spawns Re already at its last breath so the riddle cutscene can be reviewed quickly.
	/// Only obtainable from Journey mode / cheat mods (no recipe).
	/// </summary>
	public class CrackedVerse : ModItem
	{
		public override string Texture => "Replicant/Assets/Textures/Items/CrackedVerse";

		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 32;
			Item.rare = ItemRarityID.Gray;
			Item.useAnimation = 30;
			Item.useTime = 30;
			Item.useStyle = ItemUseStyleID.HoldUp;
		}

		public override bool CanUseItem(Player player) => !RiddleCutscene.Active && Main.netMode == NetmodeID.SinglePlayer;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return true;
			Re re = Re.FindActive();
			if (re == null) {
				int idx = NPC.NewNPC(player.GetSource_ItemUse(Item), (int)player.Center.X, (int)player.Center.Y - 300, ModContent.NPCType<Re>());
				if (idx < 0 || idx >= Main.maxNPCs)
					return true;
				re = Main.npc[idx].ModNPC as Re;
			}
			if (re != null) {
				re.NPC.Opacity = 1f;
				re.NPC.Center = player.Center + new Vector2(0, -260);
				re.NPC.dontTakeDamage = false;
				re.NPC.life = 1;
				re.CheckDead();
			}
			return true;
		}
	}

	/// <summary>
	/// Testing tool for phase 2: with no sisters present it wakes them in place; while they fight it skips
	/// their clock ahead to the next story beat (1:45 → the revelation → the true fight).
	/// </summary>
	public class FrayedVerse : ModItem
	{
		private static int debugStep;

		public override string Texture => "Replicant/Assets/Textures/Items/CrackedVerse";

		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 32;
			Item.rare = ItemRarityID.Gray;
			Item.useAnimation = 30;
			Item.useTime = 30;
			Item.useStyle = ItemUseStyleID.HoldUp;
		}

		public override bool CanUseItem(Player player) => !RiddleCutscene.Active && Main.netMode == NetmodeID.SinglePlayer;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return true;
			// the third verse: each use takes Recursion to its next threshold (rewind → execute → death)
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.ModNPC is Content.NPCs.Recursion.RecursionHead h) {
					if (h.State == Content.NPCs.Recursion.RecursionHead.StScripted || h.State == Content.NPCs.Recursion.RecursionHead.StDying)
						return true;
					float k = n.life / (float)n.lifeMax;
					n.life = k > 0.58f ? (int)(n.lifeMax * 0.55f) : k > 0.26f ? (int)(n.lifeMax * 0.24f) : 1;
					if (n.life == 1)
						n.SimpleStrikeNPC(9999, 0);
					return true;
				}
			}
			if (DevourCutscene.Active)
				return true;
			if (!TwinsFight.AnyTwinAlive) {
				// go to the dimension first when possible; the sisters wake there
				if (!Common.Dimension.DimensionBridge.InDimension) {
					string why = Common.Dimension.DimensionBridge.WhyNot();
					if (why == null) {
						TwinsFight.SpawnOnArrival = true;
						if (Common.Dimension.DimensionBridge.Travel())
							return true;
						TwinsFight.SpawnOnArrival = false;
					}
					else
						Common.Dimension.DimensionBridge.Report(why);
				}
				TwinsFight.SpawnTwins(player.Bottom, player.whoAmI);
				return true;
			}
			int now = TwinsFight.FightTicks;
			if (now >= TwinsTimeline.RevealEnd) {
				// in the true fight: each use calls the next set piece (Aria → Intrusion → one sister falls)
				TwinBase req = null, refr = null;
				foreach (NPC n in Main.ActiveNPCs) {
					if (n.ModNPC is Requiem r) req = r;
					if (n.ModNPC is Refrain f) refr = f;
				}
				bool alone = req == null || refr == null;
				switch (alone ? 2 : debugStep++ % 3) {
					case 0:
						if (req != null && refr != null)
							req.DebugStartAria(refr);
						break;
					case 1:
						refr?.DebugForce(TwinBase.StHack);
						break;
					case 2:
						// one sister falls; with only one left, she falls too (and the third verse begins)
						if (req != null && refr != null)
							req.NPC.SimpleStrikeNPC(req.NPC.life + 10, 0);
						else
							(req ?? refr)?.NPC.SimpleStrikeNPC((req ?? refr).NPC.life + 10, 0);
						break;
				}
				return true;
			}
			int jump = now < TwinsTimeline.RevealStart - 900 ? TwinsTimeline.RevealStart - 900
				: now < TwinsTimeline.RevealEnd ? TwinsTimeline.RevealEnd
				: now;
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.ModNPC is TwinBase t) {
					n.ai[3] = jump;
					if (jump >= TwinsTimeline.RevealEnd) {
						t.Awakened = true;
						n.life = n.lifeMax;
						Common.Dimension.DimensionSystem.AwakenTarget = 1f;
						Common.Dimension.DimensionSystem.LilyGlowTarget = 1f;
					}
				}
			}
			return true;
		}
	}
}
