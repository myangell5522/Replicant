using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Replicant.Common
{
	/// <summary>Central texture handles. Requested lazily so nothing loads on a dedicated server.</summary>
	public static class ReTex
	{
		public const string Root = "Replicant/Assets/Textures/";

		private static Asset<Texture2D> Get(string path) => ModContent.Request<Texture2D>(Root + path, AssetRequestMode.ImmediateLoad);

		// Boss parts
		public static Texture2D Body => Get("NPCs/Re/Re").Value;
		public static Texture2D BodyGlow => Get("NPCs/Re/Re_Glow").Value;
		public static Texture2D Crown => Get("NPCs/Re/ReCrown").Value;
		public static Texture2D CrownGlow => Get("NPCs/Re/ReCrown_Glow").Value;
		public static Texture2D Skirt => Get("NPCs/Re/ReSkirt").Value;
		public static Texture2D SkirtGlow => Get("NPCs/Re/ReSkirt_Glow").Value;
		public static Texture2D Tentacle => Get("NPCs/Re/ReTentacle").Value;
		public static Texture2D TentacleGlow => Get("NPCs/Re/ReTentacle_Glow").Value;

		// VFX
		public static Texture2D Bloom => Get("VFX/Bloom").Value;
		public static Texture2D BloomSharp => Get("VFX/BloomSharp").Value;
		public static Texture2D Ring => Get("VFX/Ring").Value;
		public static Texture2D Streak => Get("VFX/Streak").Value;
		public static Texture2D Flare => Get("VFX/Flare").Value;
		public static Texture2D Trail => Get("VFX/Trail").Value;
		public static Texture2D Noise => Get("VFX/Noise").Value;
		public static Texture2D Smoke => Get("VFX/Smoke").Value;
		public static Texture2D GodRay => Get("VFX/GodRay").Value;
		public static Texture2D GlyphAtlas => Get("VFX/GlyphAtlas").Value;
		public static Texture2D GlyphAtlasGlow => Get("VFX/GlyphAtlas_Glow").Value;
		public static Texture2D GlyphStrip => Get("VFX/GlyphStrip").Value;
		public static Texture2D GooChunk => Get("VFX/GooChunk").Value;
		public static Texture2D Thread => Get("VFX/Thread").Value;
		public static Texture2D Fog => Get("VFX/Fog").Value;
		public static Texture2D Vignette => Get("VFX/Vignette").Value;
		public static Texture2D Grain => Get("VFX/Grain").Value;
		public static Texture2D Ash => Get("VFX/Ash").Value;

		// Projectiles that other code draws
		public static Texture2D SigilOuter => Get("Projectiles/SigilOuter").Value;
		public static Texture2D SigilOuterGlow => Get("Projectiles/SigilOuter_Glow").Value;
		public static Texture2D SigilInner => Get("Projectiles/SigilInner").Value;
		public static Texture2D SigilInnerGlow => Get("Projectiles/SigilInner_Glow").Value;
		public static Texture2D SigilCore => Get("Projectiles/SigilCore").Value;
		public static Texture2D SigilCoreGlow => Get("Projectiles/SigilCore_Glow").Value;
		public static Texture2D GlyphLanceGlow => Get("Projectiles/GlyphLance_Glow").Value;
		public static Texture2D GooOrbGlow => Get("Projectiles/GooOrb_Glow").Value;

		// Phase 2
		public static Texture2D GlyphTree => Get("VFX/GlyphTree").Value;
		public static Texture2D Lily => Get("Tiles/GlyphLily").Value;
		public static Texture2D LilyGlow => Get("Tiles/GlyphLily_Glow").Value;
		public static Texture2D OilStone => Get("Tiles/OilStone").Value;
		public static Texture2D Halo => Get("NPCs/Twins/Halo").Value;
		public static Texture2D SongOrbGold => Get("Projectiles/SongOrbGold").Value;
		public static Texture2D SongOrbDark => Get("Projectiles/SongOrbDark").Value;
		public static Texture2D LilyPetal => Get("Projectiles/LilyPetal").Value;
		public static Texture2D RequiemSheet => Get("NPCs/Twins/Requiem").Value;
		public static Texture2D RequiemGlow => Get("NPCs/Twins/Requiem_Glow").Value;
		public static Texture2D RefrainSheet => Get("NPCs/Twins/Refrain").Value;
		public static Texture2D RefrainGlow => Get("NPCs/Twins/Refrain_Glow").Value;

		// Cutscene
		public static Texture2D Eyes => Get("Cutscene/Eyes").Value;
		public static Texture2D EyesGlow => Get("Cutscene/Eyes_Glow").Value;
		public static Texture2D Divider => Get("Cutscene/Divider").Value;

		/// <summary>Number of glyphs in GlyphAtlas (one row, 14 px wide cells).</summary>
		public const int GlyphCount = 40;
		public const int GlyphCellW = 14;
		public const int GlyphCellH = 18;

		public static Microsoft.Xna.Framework.Rectangle GlyphFrame(int index)
		{
			index = ((index % GlyphCount) + GlyphCount) % GlyphCount;
			return new Microsoft.Xna.Framework.Rectangle(index * GlyphCellW, 0, GlyphCellW, GlyphCellH);
		}
	}

	/// <summary>All sound styles. Files live in Assets/Sounds.</summary>
	public static class ReSounds
	{
		private const string R = "Replicant/Assets/Sounds/Re/";
		private const string C = "Replicant/Assets/Sounds/Cutscene/";

		public static readonly SoundStyle Awaken = new(R + "ReAwaken") { Volume = 1.1f };
		public static readonly SoundStyle Hurt = new(R + "ReHurt", 3) { Volume = 0.55f, PitchVariance = 0.15f, MaxInstances = 3 };
		public static readonly SoundStyle GlyphShoot = new(R + "ReGlyphShoot", 2) { Volume = 0.5f, PitchVariance = 0.2f, MaxInstances = 6 };
		public static readonly SoundStyle GooSpit = new(R + "ReGooSpit") { Volume = 0.8f, PitchVariance = 0.15f, MaxInstances = 4 };
		public static readonly SoundStyle Dash = new(R + "ReDash") { Volume = 0.9f, PitchVariance = 0.1f, MaxInstances = 2 };
		public static readonly SoundStyle Charge = new(R + "ReCharge") { Volume = 0.7f, MaxInstances = 2 };
		public static readonly SoundStyle LanceTelegraph = new(R + "ReLanceTelegraph") { Volume = 0.45f, PitchVariance = 0.1f, MaxInstances = 4 };
		public static readonly SoundStyle LanceFire = new(R + "ReLanceFire") { Volume = 0.6f, PitchVariance = 0.15f, MaxInstances = 6 };
		public static readonly SoundStyle Sigil = new(R + "ReSigil") { Volume = 0.7f, MaxInstances = 3 };
		public static readonly SoundStyle Explode = new(R + "ReExplode") { Volume = 1f, MaxInstances = 2 };
		public static readonly SoundStyle DeathBuildup = new(R + "ReDeathBuildup") { Volume = 1f };

		public static readonly SoundStyle EyesOpen = new(C + "EyesOpen") { Volume = 0.9f };
		public static readonly SoundStyle EyesClose = new(C + "EyesClose") { Volume = 0.9f };
		public static readonly SoundStyle Heartbeat = new(C + "Heartbeat") { Volume = 0.7f };
		public static readonly SoundStyle Burst = new(C + "Burst") { Volume = 1.15f };
		public static readonly SoundStyle GlyphStreamLoop = new(C + "GlyphStreamLoop") { Volume = 0.6f, IsLooped = true };

		/// <summary>level 1..5</summary>
		public static SoundStyle RiddleSelect(int level) => new(C + "RiddleSelect" + System.Math.Clamp(level, 1, 5)) { Volume = 0.85f };
	}
}
