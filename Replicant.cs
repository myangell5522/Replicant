using System.IO;
using Replicant.Content.NPCs.ReBoss;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant
{
	public class Replicant : Mod
	{
		public override void Load()
		{
			if (!Main.dedServ)
				BackgroundTextureLoader.AddBackgroundTexture(this, Common.Dimension.VoidSurfaceBackground.TexturePath);
		}


		public enum PacketType : byte
		{
			/// <summary>Client → server: this client finished the riddle cutscene.</summary>
			CutsceneDone,
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			PacketType type = (PacketType)reader.ReadByte();
			switch (type) {
				case PacketType.CutsceneDone:
					if (Main.netMode == NetmodeID.Server) {
						Re re = Re.FindActive();
						if (re != null && re.State == Re.AIState.Dying && !re.CutsceneFinished)
							re.FinishDeathSequence();
					}
					break;
			}
		}
	}
}
