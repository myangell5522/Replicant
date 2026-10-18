using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common.VFX;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// Short in-world cinematics (the boss entrance): letterbox bars, a camera that glides to a point and back,
	/// a player lock, a full-screen flash, and optional music silence.
	/// </summary>
	public class Cinematic : ModSystem
	{
		private static int timer = -1, duration;
		private static Vector2 focus, focusTarget;
		private static float bars;
		private static float flash;
		private static int silenceUntil;

		public static bool Running => timer >= 0;
		/// <summary>The local player can't act during the cinematic (released a little before the end).</summary>
		public static bool Locked => timer >= 0 && timer < duration - 25;
		/// <summary>True while the cinematic wants the music held back (until the roar).</summary>
		public static bool SilenceMusic => timer >= 0 && timer < silenceUntil;

		public static void Begin(Vector2 worldFocus, int length, int silenceTicks = 0)
		{
			if (Main.dedServ)
				return;
			if (Main.LocalPlayer.Distance(worldFocus) > 3000f)
				return;
			focusTarget = worldFocus;
			if (Running) {
				// already framing something: keep the camera where it is and glide on — never snap back to the
				// player and in again (that read as a teleport)
				int held = System.Math.Min(timer, 50);
				timer = held;
				duration = held + length;
				silenceUntil = held + silenceTicks;
				return;
			}
			focus = worldFocus;
			duration = length;
			timer = 0;
			silenceUntil = silenceTicks;
		}

		public static void SetFocus(Vector2 worldFocus) => focusTarget = worldFocus;

		public static void Flash(float amount) => flash = MathHelper.Max(flash, amount * ModContent.GetInstance<ReClientConfig>().FlashIntensity);

		public override void OnWorldUnload()
		{
			timer = -1;
			bars = 0f;
		}

		public override void PostUpdateEverything()
		{
			flash *= 0.9f;
			if (timer < 0) {
				bars = MathHelper.Lerp(bars, 0f, 0.12f);
				return;
			}
			timer++;
			bool closing = timer > duration - 50;
			bars = MathHelper.Lerp(bars, closing ? 0f : 1f, closing ? 0.08f : 0.06f);
			// camera: glide in, hold, glide back
			float strength = ReUtils.EaseInOutSine(timer / 50f) * ReUtils.EaseInOutSine((duration - timer) / 50f);
			// the focus itself glides (critically damped), so a moving subject is followed, not jumped to
			focus = Vector2.Lerp(focus, focusTarget, 0.075f);
			ScreenFX.Focus(focus, strength);
			if (Locked)
				Main.playerInventory = false;
			if (timer >= duration)
				timer = -1;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (bars < 0.01f && flash < 0.01f)
				return;
			int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("Replicant: Cinematic", () => {
				Texture2D px = TextureAssets.MagicPixel.Value;
				int h = (int)(Main.screenHeight * 0.11f * ReUtils.EaseOutCubic(bars));
				if (h > 0) {
					Main.spriteBatch.Draw(px, new Rectangle(0, 0, Main.screenWidth, h), Color.Black);
					Main.spriteBatch.Draw(px, new Rectangle(0, Main.screenHeight - h, Main.screenWidth, h), Color.Black);
				}
				if (flash > 0.01f)
					Main.spriteBatch.Draw(px, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), ReUtils.PaleGold.Additive(flash));
				return true;
			}, InterfaceScaleType.None));
		}
	}
}
