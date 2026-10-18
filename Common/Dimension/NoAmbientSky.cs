using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.Skies;
using Terraria.ModLoader;

namespace Replicant.Common.Dimension
{
	/// <summary>
	/// The song's dimension has no balloons, birds, butterflies or meteors drifting across its sky: vanilla's ambient
	/// sky entities are neither spawned nor drawn while the veil is up (they also stay away during the fight at home).
	/// </summary>
	public class NoAmbientSky : ModSystem
	{
		private static bool Suppress => DimensionSystem.Veil || DimensionBridge.InDimension;

		public override void Load()
		{
			On_AmbientSky.Draw += Draw;
			On_AmbientSky.Spawn += Spawn;
		}

		private static void Draw(On_AmbientSky.orig_Draw orig, AmbientSky self, SpriteBatch spriteBatch, float minDepth, float maxDepth)
		{
			if (!Suppress)
				orig(self, spriteBatch, minDepth, maxDepth);
		}

		private static void Spawn(On_AmbientSky.orig_Spawn orig, AmbientSky self, Player player, Terraria.GameContent.Ambience.SkyEntityType type, int seed)
		{
			if (!Suppress)
				orig(self, player, type, seed);
		}
	}
}
