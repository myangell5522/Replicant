using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics;
using Terraria.ModLoader;
using Replicant.Content.NPCs.ReBoss;

namespace Replicant.Common.VFX
{
	/// <summary>Screen shake, zoom punches, camera focus and the boss's sky tint.</summary>
	public class ScreenFX : ModSystem
	{
		private static float shake;
		private static float zoomPunch;
		private static float zoomHold = 1f;
		private static Vector2 focus;
		private static float focusStrength;
		private static int focusTimer, zoomTimer;
		public static float BossAmbience;   // 0..1, eased while Re is alive

		/// <summary>Adds screen shake (pixels of jitter). Stacks but saturates.</summary>
		public static void Shake(float amount) => shake = MathF.Min(MathF.Max(shake, amount), 60f);

		/// <summary>Brief zoom-in punch (e.g. 0.08 = +8%).</summary>
		public static void ZoomPunch(float amount) => zoomPunch = MathF.Max(zoomPunch, amount);

		/// <summary>Sustained zoom multiplier set every frame by a cutscene (1 = none).</summary>
		public static void HoldZoom(float zoom)
		{
			zoomHold = zoom;
			zoomTimer = 2;
		}

		/// <summary>Pull the camera toward a world position this frame (strength 0..1).</summary>
		public static void Focus(Vector2 worldPos, float strength)
		{
			focus = worldPos;
			focusStrength = MathHelper.Clamp(strength, 0f, 1f);
			focusTimer = 2;
		}

		public override void PostUpdateEverything()
		{
			shake *= 0.88f;
			if (shake < 0.2f)
				shake = 0f;
			zoomPunch *= 0.9f;
			if (focusTimer > 0)
				focusTimer--;
			if (zoomTimer > 0)
				zoomTimer--;
			else
				zoomHold = 1f;

			bool reAlive = NPC.AnyNPCs(ModContent.NPCType<Re>());
			BossAmbience = MathHelper.Lerp(BossAmbience, reAlive ? 1f : 0f, 0.02f);
		}

		public override void ModifyScreenPosition()
		{
			if (focusTimer > 0) {
				Vector2 target = focus - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
				Main.screenPosition = Vector2.Lerp(Main.screenPosition, target, focusStrength);
			}
			if (shake > 0f && ModContent.GetInstance<ReClientConfig>().ScreenShake)
				Main.screenPosition += Main.rand.NextVector2Circular(shake, shake);
		}

		public override void ModifyTransformMatrix(ref SpriteViewMatrix Transform)
		{
			float z = (1f + zoomPunch) * zoomHold;
			if (MathF.Abs(z - 1f) > 0.001f)
				Transform.Zoom *= z;
		}

		public override void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor)
		{
			if (BossAmbience <= 0.01f)
				return;
			// Re smothers the sky: desaturated, dim, faint amber cast.
			Color tint = new(70, 64, 52);
			tileColor = Color.Lerp(tileColor, tint, 0.55f * BossAmbience);
			backgroundColor = Color.Lerp(backgroundColor, new Color(40, 36, 30), 0.65f * BossAmbience);
		}
	}
}
