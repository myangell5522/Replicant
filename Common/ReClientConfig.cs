using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace Replicant.Common
{
	public class ReClientConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(true)]
		public bool ScreenShake;

		[Range(0f, 1f)]
		[DefaultValue(1f)]
		public float FlashIntensity;

		[DefaultValue(false)]
		public bool SkipRiddleCutscene;
	}
}
