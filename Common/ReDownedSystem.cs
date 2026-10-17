using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace Replicant.Common
{
	public class ReDownedSystem : ModSystem
	{
		public static bool DownedRe;
		public static bool DownedTwins;
		/// <summary>Whether the riddle was answered correctly on the first pick (for future rewards).</summary>
		public static bool AnsweredRiddleFirstTry;

		public override void ClearWorld()
		{
			DownedRe = false;
			DownedTwins = false;
			AnsweredRiddleFirstTry = false;
		}

		public override void SaveWorldData(TagCompound tag)
		{
			if (DownedRe)
				tag["downedRe"] = true;
			if (AnsweredRiddleFirstTry)
				tag["riddleFirstTry"] = true;
			if (DownedTwins)
				tag["downedTwins"] = true;
		}

		public override void LoadWorldData(TagCompound tag)
		{
			DownedRe = tag.ContainsKey("downedRe");
			AnsweredRiddleFirstTry = tag.ContainsKey("riddleFirstTry");
			DownedTwins = tag.ContainsKey("downedTwins");
		}

		public override void NetSend(BinaryWriter writer)
		{
			writer.Write(DownedRe);
		}

		public override void NetReceive(BinaryReader reader)
		{
			DownedRe = reader.ReadBoolean();
		}
	}
}
