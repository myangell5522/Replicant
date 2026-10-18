using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace Replicant.Common.VFX
{
	public enum PType : byte
	{
		Glyph,      // rune from the glyph atlas, glowing
		Bloom,      // soft additive orb
		Spark,      // streak stretched along velocity
		Smoke,      // dark alpha-blended goo smoke
		GooChunk,   // falling chunk of black slime
		Ring,       // expanding shockwave ring
		Flare,      // 4-point star
		Ash,        // grey flake, alpha blended, drifts down
	}

	public class Particle
	{
		public PType Type;
		public Vector2 Position, Velocity;
		public float Rotation, RotationSpeed;
		public float Scale, ScaleSpeed = 1f;
		public Color Color;
		public float Opacity = 1f;
		public float Time;
		public int Lifetime;
		public float Drag = 0.96f;
		public float Gravity;
		public int Variant;
		public bool ScreenSpace;   // drawn by the cutscene in screen coordinates
		public bool Additive = true;
		public float Life01 => Lifetime <= 0 ? 1f : Time / (float)Lifetime;
	}

	/// <summary>
	/// Lightweight pooled particle system. World particles draw after dust (over NPCs);
	/// screen-space particles are drawn by the riddle cutscene and obey its slow-motion time scale.
	/// </summary>
	public class ParticleSystem : ModSystem
	{
		private const int Max = 3000;
		private static readonly List<Particle> world = new();
		private static readonly List<Particle> screen = new();

		public override void Load()
		{
			if (Main.dedServ)
				return;
			On_Main.DrawDust += DrawAfterDust;
		}

		public override void Unload()
		{
			world.Clear();
			screen.Clear();
		}

		public override void OnWorldUnload()
		{
			world.Clear();
			// the riddle burst keeps its screen-space particles across the jump into the dimension
			if (!Cutscene.RiddleCutscene.TravelingToDimension)
				screen.Clear();
		}

		private static void DrawAfterDust(On_Main.orig_DrawDust orig, Main self)
		{
			orig(self);
			if (world.Count == 0)
				return;
			SpriteBatch sb = Main.spriteBatch;
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
			DrawList(sb, world, Main.screenPosition);
			sb.End();
		}

		// ------------------------------------------------------------------ spawning
		public static Particle Spawn(Particle p)
		{
			if (Main.dedServ)
				return p;
			List<Particle> list = p.ScreenSpace ? screen : world;
			if (list.Count >= Max)
				list.RemoveAt(0);
			list.Add(p);
			return p;
		}

		public static Particle Glyph(Vector2 pos, Vector2 vel, Color color, float scale = 1f, int life = 40, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Glyph, Position = pos, Velocity = vel, Color = color, Scale = scale, Lifetime = life, Variant = Main.rand.Next(ReTex.GlyphCount), Rotation = 0f, Drag = 0.95f, ScreenSpace = screenSpace });

		public static Particle Bloom(Vector2 pos, Vector2 vel, Color color, float scale = 1f, int life = 20, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Bloom, Position = pos, Velocity = vel, Color = color, Scale = scale, Lifetime = life, Drag = 0.92f, ScreenSpace = screenSpace });

		public static Particle Spark(Vector2 pos, Vector2 vel, Color color, float scale = 1f, int life = 24, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Spark, Position = pos, Velocity = vel, Color = color, Scale = scale, Lifetime = life, Drag = 0.93f, ScreenSpace = screenSpace });

		public static Particle Smoke(Vector2 pos, Vector2 vel, Color color, float scale = 1f, int life = 50, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Smoke, Position = pos, Velocity = vel, Color = color, Scale = scale, ScaleSpeed = 1.012f, Lifetime = life, Rotation = Main.rand.NextFloat(MathHelper.TwoPi), RotationSpeed = Main.rand.NextFloat(-0.03f, 0.03f), Drag = 0.95f, Additive = false, ScreenSpace = screenSpace });

		public static Particle Goo(Vector2 pos, Vector2 vel, float scale = 1f, int life = 60)
			=> Spawn(new Particle { Type = PType.GooChunk, Position = pos, Velocity = vel, Color = Color.White, Scale = scale, Lifetime = life, Gravity = 0.25f, Drag = 0.99f, Variant = Main.rand.Next(3), Rotation = Main.rand.NextFloat(MathHelper.TwoPi), RotationSpeed = Main.rand.NextFloat(-0.2f, 0.2f), Additive = false });

		public static Particle Ash(Vector2 pos, Vector2 vel, Color color, float scale = 1f, int life = 300, bool screenSpace = true)
			=> Spawn(new Particle { Type = PType.Ash, Position = pos, Velocity = vel, Color = color, Scale = scale, Lifetime = life, Drag = 1f, Variant = Main.rand.Next(100), Rotation = Main.rand.NextFloat(MathHelper.TwoPi), RotationSpeed = Main.rand.NextFloat(-0.02f, 0.02f), Additive = false, ScreenSpace = screenSpace });

		public static Particle Ring(Vector2 pos, Color color, float startScale, float growth, int life = 30, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Ring, Position = pos, Color = color, Scale = startScale, ScaleSpeed = growth, Lifetime = life, Drag = 1f, ScreenSpace = screenSpace });

		public static Particle Flare(Vector2 pos, Color color, float scale = 1f, int life = 18, bool screenSpace = false)
			=> Spawn(new Particle { Type = PType.Flare, Position = pos, Color = color, Scale = scale, Lifetime = life, Rotation = Main.rand.NextFloat(-0.2f, 0.2f), ScreenSpace = screenSpace });

		/// <summary>Standard "glyph burst" used on impacts: runes, sparks, bloom.</summary>
		public static void GlyphBurst(Vector2 pos, int count, float speed, float scale = 1f)
		{
			for (int i = 0; i < count; i++) {
				Vector2 v = Main.rand.NextVector2Unit() * Main.rand.NextFloat(0.3f, 1f) * speed;
				Glyph(pos, v, ReUtils.GoldRamp(Main.rand.NextFloat(0.4f, 1f)), Main.rand.NextFloat(0.7f, 1.2f) * scale, Main.rand.Next(25, 50));
				if (i % 2 == 0)
					Spark(pos, v * 1.6f, ReUtils.Gold, Main.rand.NextFloat(0.5f, 1f) * scale, Main.rand.Next(14, 26));
			}
			Bloom(pos, Vector2.Zero, ReUtils.Gold, 1.2f * scale, 18);
		}

		// ------------------------------------------------------------------ update
		public override void PostUpdateDusts()
		{
			if (Main.dedServ)
				return;
			UpdateList(world, 1f);
		}

		/// <summary>Called by the cutscene each frame with its (slow-motion) time scale.</summary>
		public static void UpdateScreen(float timeScale)
		{
			UpdateList(screen, timeScale);
		}

		private static void UpdateList(List<Particle> list, float dt)
		{
			for (int i = list.Count - 1; i >= 0; i--) {
				Particle p = list[i];
				p.Time += dt;
				if (p.Time >= p.Lifetime) {
					list.RemoveAt(i);
					continue;
				}
				p.Velocity.Y += p.Gravity * dt;
				p.Velocity *= MathF.Pow(p.Drag, dt);
				p.Position += p.Velocity * dt;
				p.Rotation += p.RotationSpeed * dt;
				p.Scale *= MathF.Pow(p.ScaleSpeed, dt);
			}
		}

		// ------------------------------------------------------------------ drawing
		public static void DrawScreen(SpriteBatch sb) => DrawList(sb, screen, Vector2.Zero);

		public static void ClearScreen() => screen.Clear();

		/// <summary>Move every world particle (the dimension's loop wrapping around).</summary>
		public static void ShiftWorld(Vector2 d)
		{
			foreach (Particle p in world)
				p.Position += d;
		}

		private static void DrawList(SpriteBatch sb, List<Particle> list, Vector2 offset)
		{
			// Alpha-blended (smoke, goo) first so additive glows sit on top of them.
			for (int pass = 0; pass < 2; pass++) {
				foreach (Particle p in list) {
					if (p.Additive == (pass == 0))
						continue;
					DrawParticle(sb, p, p.Position - offset);
				}
			}
		}

		private static void DrawParticle(SpriteBatch sb, Particle p, Vector2 pos)
		{
			float t = p.Life01;
			float fade = MathF.Min(1f, (1f - t) * 3f) * MathF.Min(1f, p.Time / 3f + 0.34f) * p.Opacity;
			switch (p.Type) {
				case PType.Glyph: {
					// a crisp rune with a small, soft halo — never a big disc
					Texture2D tex = ReTex.GlyphAtlasGlow;
					// alive: it rewrites itself every so often, breathes, and sways on the air
					int rewrite = (int)(p.Time / (16f + p.Variant % 9));
					Rectangle src = ReTex.GlyphFrame(p.Variant + rewrite * 7 + (t > 0.7f && Main.rand.NextBool(6) ? 1 : 0));
					Vector2 origin = src.Size() / 2f;
					float breathe = 0.7f + 0.3f * MathF.Sin(p.Time * 0.15f + p.Variant);
					fade *= breathe;
					pos += new Vector2(MathF.Sin(p.Time * 0.045f + p.Variant) * 3f, 0f);
					FX.Bloom(sb, pos, p.Color, 16f * p.Scale, 0.16f * fade);
					sb.Draw(tex, pos, src, p.Color.Additive(fade), p.Rotation, origin, p.Scale, SpriteEffects.None, 0f);
					sb.Draw(tex, pos, src, Color.White.Additive(0.25f * fade), p.Rotation, origin, p.Scale, SpriteEffects.None, 0f);
					break;
				}
				case PType.Bloom:
					FX.Bloom(sb, pos, p.Color, 64f * p.Scale * (1f + t * 0.5f), fade);
					break;
				case PType.Spark: {
					Texture2D tex = ReTex.Streak;
					float len = MathHelper.Clamp(p.Velocity.Length() * 0.045f, 0.08f, 0.8f) * p.Scale;
					float rot = p.Velocity.ToRotation();
					Vector2 scale = new(len, 0.12f * p.Scale * (1f - t));
					sb.Draw(tex, pos, null, p.Color.Additive(fade), rot, tex.Size() / 2f, scale, SpriteEffects.None, 0f);
					sb.Draw(tex, pos, null, Color.White.Additive(fade * 0.6f), rot, tex.Size() / 2f, scale * new Vector2(0.6f, 0.4f), SpriteEffects.None, 0f);
					break;
				}
				case PType.Smoke: {
					Texture2D tex = ReTex.Smoke;
					sb.Draw(tex, pos, null, p.Color * (fade * 0.8f), p.Rotation, tex.Size() / 2f, p.Scale * 0.375f, SpriteEffects.None, 0f);
					break;
				}
				case PType.Ash: {
					Texture2D tex = ReTex.Ash;
					float sway = MathF.Sin(p.Time * 0.05f + p.Variant) * 0.6f;
					sb.Draw(tex, pos + new Vector2(sway * 6f, 0f), null, p.Color * fade, p.Rotation + sway, tex.Size() / 2f, p.Scale * 0.5f, SpriteEffects.None, 0f);
					break;
				}
				case PType.GooChunk: {
					Texture2D tex = ReTex.GooChunk;
					Rectangle src = tex.VFrame(p.Variant, 3);
					Color light = Lighting.GetColor((p.Position / 16f).ToPoint());
					sb.Draw(tex, pos, src, light * fade, p.Rotation, src.Size() / 2f, p.Scale, SpriteEffects.None, 0f);
					break;
				}
				case PType.Ring: {
					Texture2D tex = ReTex.Ring;
					float a = (1f - t) * (1f - t) * p.Opacity;
					sb.Draw(tex, pos, null, p.Color.Additive(a), 0f, tex.Size() / 2f, p.Scale * 0.5f, SpriteEffects.None, 0f);
					break;
				}
				case PType.Flare: {
					Texture2D tex = ReTex.Flare;
					float s = p.Scale * 0.5f * ReUtils.Bump(MathF.Sqrt(t));
					sb.Draw(tex, pos, null, p.Color.Additive(p.Opacity), p.Rotation, tex.Size() / 2f, s, SpriteEffects.None, 0f);
					sb.Draw(tex, pos, null, Color.White.Additive(p.Opacity * 0.8f), p.Rotation, tex.Size() / 2f, s * 0.5f, SpriteEffects.None, 0f);
					break;
				}
			}
		}
	}
}
