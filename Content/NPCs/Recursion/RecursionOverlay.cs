using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace Replicant.Content.NPCs.Recursion
{
	/// <summary>
	/// Everything Recursion puts on the screen outside its own body: the whole-worm draw pass (so a long body
	/// is never culled because its head is off-screen), speed streaks when it tears past, an edge marker that
	/// shows where the head is when it's out of view, and its glitched attack cards (REWIND, EXECUTE).
	/// </summary>
	public class RecursionOverlay : ModSystem
	{
		public static float Speed;
		/// <summary>Set every tick while a rewind echo runs (decays on its own).</summary>
		public static float Rewind;
		/// <summary>Frenzy (EXECUTE) strength, eased by the head.</summary>
		public static float Frenzy;
		private static float rewindShown;
		public static Vector2 SpeedDir = Vector2.UnitX;
		private static string cardKey;
		private static int cardTimer, cardLength;

		public static void Card(string key, int length)
		{
			cardKey = key;
			cardTimer = 0;
			cardLength = length;
		}

		public override void Load() => On_Main.DrawNPCs += DrawNPCs;

		public override void OnWorldUnload()
		{
			Speed = 0f;
			cardKey = null;
		}

		private static void DrawNPCs(On_Main.orig_DrawNPCs orig, Main self, bool behindTiles)
		{
			orig(self, behindTiles);
			if (behindTiles)
				return;
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.ModNPC is RecursionHead h) {
					try {
						h.DrawWorm(Main.spriteBatch);
					}
					catch (Exception e) {
						ModContent.GetInstance<Replicant>().Logger.Warn("Recursion draw failed: " + e.Message);
					}
				}
			}
		}

		public override void PostUpdateEverything()
		{
			if (cardKey != null && ++cardTimer > cardLength)
				cardKey = null;
			if (!NPC.AnyNPCs(ModContent.NPCType<RecursionHead>())) {
				Speed *= 0.9f;
				Frenzy *= 0.9f;
			}
			rewindShown = MathHelper.Lerp(rewindShown, Rewind, 0.15f);
			Rewind = 0f;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int i = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
			if (i < 0)
				i = 0;
			layers.Insert(i, new LegacyGameInterfaceLayer("Replicant: Recursion", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private static void Draw(SpriteBatch sb)
		{
			Texture2D px = TextureAssets.MagicPixel.Value;
			int W = Main.screenWidth, H = Main.screenHeight;
			float t = Main.GlobalTimeWrappedHourly;
			// speed streaks: long thin lines racing across the screen along its motion
			if (Speed > 0.02f) {
				float rot = SpeedDir.ToRotation();
				for (int k = 0; k < 26; k++) {
					float h1 = ReUtils.Hash01(k, 71), h2 = ReUtils.Hash01(k, 72);
					float along = ((t * (2600f + h1 * 1600f) + h2 * 4000f) % (W + H + 800f)) - 400f;
					Vector2 across = new Vector2(-SpeedDir.Y, SpeedDir.X) * ((h2 - 0.5f) * (W + H));
					Vector2 p = new Vector2(W / 2f, H / 2f) + SpeedDir * (along - (W + H) / 2f) + across;
					float len = 160f + 260f * h1;
					sb.Draw(px, p, new Rectangle(0, 0, 1, 1), (k % 4 == 0 ? ReUtils.Ember : ReUtils.PaleGold).Additive(0.22f * Speed), rot, new Vector2(1f, 0.5f), new Vector2(len, 1.5f + h1 * 1.5f), SpriteEffects.None, 0f);
				}
				// a faint radial darkening at the edges sells the velocity
				FX.Smoke(sb, new Vector2(0, H / 2f), Color.Black * (0.25f * Speed), H * 0.5f);
				FX.Smoke(sb, new Vector2(W, H / 2f), Color.Black * (0.25f * Speed), H * 0.5f);
			}

			// where is the head? a pulsing marker on the screen edge
			foreach (NPC n in Main.ActiveNPCs) {
				if (n.ModNPC is not RecursionHead h || h.State == RecursionHead.StScripted || n.dontTakeDamage && h.State != RecursionHead.StDying)
					continue;
				Vector2 s = Vector2.Transform(n.Center - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);
				if (s.X > 0 && s.X < W && s.Y > 0 && s.Y < H)
					continue;
				Vector2 c = new(W / 2f, H / 2f);
				Vector2 d = (s - c).SafeNormalize(Vector2.UnitX);
				float k = MathF.Min((W / 2f - 50f) / MathF.Max(0.001f, MathF.Abs(d.X)), (H / 2f - 50f) / MathF.Max(0.001f, MathF.Abs(d.Y)));
				Vector2 at = c + d * k;
				float pulse = 0.6f + 0.4f * MathF.Sin(t * 10f);
				for (int a = -1; a <= 1; a += 2) {
					Vector2 arm = (d.ToRotation() + MathHelper.Pi + a * 0.55f).ToRotationVector2();
					sb.Draw(px, at, new Rectangle(0, 0, 1, 1), ReUtils.Ember.Additive(0.9f * pulse), arm.ToRotation(), new Vector2(0f, 0.5f), new Vector2(22f, 4f), SpriteEffects.None, 0f);
				}
				FX.Bloom(sb, at, ReUtils.Ember, 50f, 0.35f * pulse);
			}

			// EXECUTE: the edges of the world burn while it is in its frenzy
			if (Frenzy > 0.02f) {
				float pulse = 0.75f + 0.25f * MathF.Sin(t * 4f);
				Texture2D vig = ReTex.Vignette;
				sb.Draw(vig, new Rectangle(0, 0, W, H), new Color(150, 20, 6) * (0.45f * Frenzy * pulse));
				for (int k = 0; k < 12; k++) {
					float x = ReUtils.Hash01(k, 31) * W;
					float y = H - ((t * (60f + ReUtils.Hash01(k, 32) * 80f) + ReUtils.Hash01(k, 33) * H) % H);
					sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x, y), ReTex.GlyphFrame(k * 3 + (int)(t * 3f)), ReUtils.Ember.Additive(0.35f * Frenzy), 0f, new Vector2(7, 9), 1.4f, SpriteEffects.None, 0f);
				}
			}

			// REWIND: the picture tracks like an old tape — rolling bars, a colour slip, the << mark
			if (rewindShown > 0.02f) {
				float r = rewindShown;
				for (int k = 0; k < 4; k++) {
					float y = (t * (420f + k * 90f) + k * H / 4f) % (H + 80f) - 40f;
					sb.Draw(px, new Rectangle(0, (int)y, W, 10 + k * 4), Color.White.Additive(0.06f * r));
					sb.Draw(px, new Rectangle((int)(MathF.Sin(t * 30f + k) * 12f), (int)y + 12, W, 2), new Color(255, 60, 20).Additive(0.18f * r));
				}
				for (int y = 0; y < H; y += 4)
					sb.Draw(px, new Rectangle(0, y, W, 1), Color.Black * (0.12f * r));
				if ((int)(t * 3f) % 2 == 0) {
					DynamicSpriteFont font = FontAssets.DeathText.Value;
					sb.DrawString(font, "<<", new Vector2(40f, 40f), ReUtils.PaleGold * (0.85f * r), 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
				}
			}

			if (cardKey != null)
				DrawCard(sb, W, H, t);
		}

		private static void DrawCard(SpriteBatch sb, int W, int H, float t)
		{
			DynamicSpriteFont big = FontAssets.DeathText.Value;
			string text = Language.GetTextValue(cardKey);
			float a = ReUtils.InverseLerp(0, 8, cardTimer) * (1f - ReUtils.InverseLerp(cardLength - 20, cardLength, cardTimer));
			float s = MathHelper.Clamp(H / 1080f, 0.7f, 1.6f) * 1.3f;
			Vector2 size = big.MeasureString(text) * s;
			Vector2 p = new(W / 2f - size.X / 2f, H * 0.2f - size.Y / 2f);
			float g = ReUtils.Hash01((int)(t * 24f), 9) > 0.7f ? 10f : 2f;
			// a black band behind it
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(0, (int)(p.Y - 6), W, (int)(size.Y + 12)), Color.Black * (0.55f * a));
			sb.DrawString(big, text, p + new Vector2(-g, 0), new Color(255, 40, 20).Additive(0.6f * a), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			sb.DrawString(big, text, p + new Vector2(g, 0), new Color(255, 210, 120).Additive(0.4f * a), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			sb.DrawString(big, text, p, ReUtils.PaleGold * a, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
		}
	}
}
