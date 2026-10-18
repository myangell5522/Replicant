using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Recursion;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles.Recursion
{
	/// <summary>
	/// A needle of verse shed by one of Recursion's plates. It wakes where it was shed, turns to face you while a
	/// short sight-line burns, then flies straight. ai[0] = wake ticks, ai[1] = speed.
	/// </summary>
	public class VerseNeedle : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/Thread";
		private ref float Timer => ref Projectile.localAI[0];
		private float Wake => Projectile.ai[0];
		private float Speed => Projectile.ai[1];

		public override void SetStaticDefaults()
		{
			ProjectileID.Sets.TrailCacheLength[Type] = 8;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

		public override void SetDefaults()
		{
			Projectile.width = 14;
			Projectile.height = 14;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 260;
		}

		public override void AI()
		{
			Timer++;
			Player p = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
			if (Timer < Wake) {
				Projectile.velocity = Vector2.Zero;
				Projectile.rotation = Projectile.rotation.AngleLerp(Projectile.Center.SafeDirectionTo(p.Center).ToRotation(), Timer < 3 ? 1f : 0.3f);
				return;
			}
			if (Timer == (int)Wake) {
				Projectile.velocity = Projectile.rotation.ToRotationVector2() * Speed * 0.6f;
				if (!Main.dedServ) {
					SoundEngine.PlaySound(RecursionSounds.Needle, Projectile.Center);
					ParticleSystem.Flare(Projectile.Center, ReUtils.PaleGold, 0.6f, 8);
				}
			}
			if (Projectile.velocity.Length() < Speed)
				Projectile.velocity *= 1.08f;
			Projectile.rotation = Projectile.velocity.ToRotation();
		}

		public override bool CanHitPlayer(Player target) => Timer >= Wake;

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D px = TextureAssets.MagicPixel.Value;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float rot = Projectile.rotation;
			Vector2 d = rot.ToRotationVector2();
			float wake = ReUtils.InverseLerp(0, 8, Timer);
			if (Timer < Wake) {
				// sight line
				float k = Timer / Wake;
				for (float s = 16f; s < 60f + 220f * k; s += 12f)
					sb.Draw(px, pos + d * s, new Rectangle(0, 0, 1, 1), ReUtils.Ember.Additive(0.6f * k * (1f - s / (60f + 220f * k))), rot, new Vector2(0.5f), new Vector2(6f, 2f), SpriteEffects.None, 0f);
			}
			// ember trail
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float k = 1f - i / (float)Projectile.oldPos.Length;
				Vector2 q = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				sb.Draw(px, q, new Rectangle(0, 0, 1, 1), ReUtils.Ember.Additive(0.5f * k), rot, new Vector2(0.5f), new Vector2(10f, 3f * k + 1f), SpriteEffects.None, 0f);
			}
			FX.Bloom(sb, pos, ReUtils.Ember, 26f, 0.4f * wake);
			// the needle: an obsidian thorn, its ember pulse running to the tip (4 frames)
			Texture2D tex = ModContent.Request<Texture2D>("Replicant/Assets/Textures/Projectiles/VerseNeedle", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			Texture2D glow = ModContent.Request<Texture2D>("Replicant/Assets/Textures/Projectiles/VerseNeedle_Glow", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			int fh = tex.Height / 4;
			Rectangle src = new(0, ((int)(Timer / 3) % 4) * fh, tex.Width, fh);
			Vector2 origin = new(tex.Width * 0.6f, fh / 2f);
			float sc = 0.9f + 0.1f * wake;
			sb.Draw(tex, pos, src, Color.White * wake, rot, origin, sc, SpriteEffects.None, 0f);
			sb.Draw(glow, pos, src, Color.White.Additive(wake), rot, origin, sc, SpriteEffects.None, 0f);
			return false;
		}
	}

	/// <summary>
	/// The rewind: a ghost of Recursion replaying its own recent path backwards, faster. First its route is
	/// traced across the arena (so you can see where it will run), then the ghost runs it.
	/// </summary>
	public class RewindEcho : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/Thread";
		private readonly List<Vector2> path = new();
		private ref float Timer => ref Projectile.localAI[0];
		private float head;              // index along the path
		private const int Warn = 45;
		private const int Ghosts = 18;
		private const float Step = 1.6f;

		public void SetPath(List<Vector2> pts)
		{
			path.Clear();
			path.AddRange(pts);
		}

		public override void SetDefaults()
		{
			Projectile.width = 60;
			Projectile.height = 60;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 600;
		}

		private Vector2 At(float idx)
		{
			if (path.Count == 0)
				return Projectile.Center;
			idx = MathHelper.Clamp(idx, 0, path.Count - 1);
			int a = (int)idx;
			int b = Math.Min(path.Count - 1, a + 1);
			return Vector2.Lerp(path[a], path[b], idx - a);
		}

		public override void AI()
		{
			Timer++;
			if (path.Count < 2) {
				Projectile.Kill();
				return;
			}
			if (Timer > Warn)
				head += Step;
			if (!Main.dedServ)
				RecursionOverlay.Rewind = 1f;
			Projectile.Center = At(head);
			if (head - Ghosts * 3f > path.Count)
				Projectile.Kill();
			if (!Main.dedServ && Timer == Warn) {
				SoundEngine.PlaySound(RecursionSounds.Rush with { Pitch = 0.4f }, Projectile.Center);
				SoundEngine.PlaySound(Common.ReSounds.EyesClose with { Pitch = 0.6f, Volume = 0.5f }, Projectile.Center);
			}
		}

		public override bool CanHitPlayer(Player target) => Timer > Warn;

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
		{
			for (int g = 0; g < Ghosts; g++) {
				float idx = head - g * 3f;
				if (idx < 0 || idx >= path.Count)
					continue;
				Vector2 c = At(idx);
				float r = g == 0 ? 44f : 28f;
				if (new Rectangle((int)(c.X - r), (int)(c.Y - r), (int)(r * 2), (int)(r * 2)).Intersects(targetHitbox))
					return true;
			}
			return false;
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float time = Main.GlobalTimeWrappedHourly;
			// the route, traced in rewind glyphs
			float trace = Timer < Warn ? ReUtils.InverseLerp(0, Warn * 0.6f, Timer) : 1f - ReUtils.InverseLerp(Warn, Warn + 160, Timer) * 0.6f;
			for (int i = (int)head; i < path.Count; i += 4) {
				Vector2 q = path[i] - Main.screenPosition;
				float flick = (i / 4 + (int)(time * 20f)) % 6 == 0 ? 1f : 0.5f;
				sb.Draw(px, q, new Rectangle(0, 0, 1, 1), ReUtils.Gold.Additive(0.35f * trace * flick), 0f, new Vector2(0.5f), 3f, SpriteEffects.None, 0f);
				if (i % 40 == 0)
					sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(i / 40 + 3), ReUtils.PaleGold.Additive(0.5f * trace), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
			}
			if (Timer <= Warn)
				return false;
			// the ghost: translucent gold silhouettes with scanline tearing
			Texture2D body = RTex.Body, headTex = RTex.Head;
			int bfh = body.Height / RecursionHead.BodyFrames, hfh = headTex.Height / RecursionHead.HeadFrames;
			for (int g = Ghosts - 1; g >= 0; g--) {
				float idx = head - g * 3f;
				if (idx < 1 || idx >= path.Count - 1)
					continue;
				Vector2 c = At(idx);
				Vector2 ahead = At(idx + 1);
				// it runs backwards along the path: faces toward where it's going
				float rot = (ahead - c).ToRotation();
				Vector2 pos = c - Main.screenPosition + new Vector2(ReUtils.Hash01(g, (int)(time * 30f)) > 0.85f ? 6f : 0f, 0f);
				float a = 0.55f * (1f - g / (float)Ghosts * 0.5f);
				if (g == 0) {
					Rectangle src = new(0, (RecursionHead.HeadFrames - 1) * hfh, headTex.Width, hfh);
					sb.Draw(headTex, pos, src, new Color(255, 200, 110).Additive(a), rot, new Vector2(headTex.Width * 0.42f, hfh / 2f), 1.35f, SpriteEffects.None, 0f);
					FX.Bloom(sb, pos, ReUtils.Gold, 90f, 0.35f);
				}
				else {
					Rectangle src = new(0, ((int)(time * 20f) + g) % RecursionHead.BodyFrames * bfh, body.Width, bfh);
					sb.Draw(body, pos, src, new Color(255, 180, 90).Additive(a), rot, new Vector2(body.Width / 2f, bfh / 2f), RecursionHead.SegScale(g * 2), SpriteEffects.None, 0f);
				}
			}
			return false;
		}
	}
}
