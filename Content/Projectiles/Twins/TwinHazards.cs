using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles.Twins
{
	/// <summary>
	/// Refrain's echo: replays one of her dashes a moment later as a burning afterimage.
	/// Spawned at the dash start with velocity = dash velocity; ai[0] = delay, ai[1] = dash ticks.
	/// </summary>
	public class EchoGhost : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/NPCs/Twins/Refrain";
		private ref float Timer => ref Projectile.localAI[0];
		private float Delay => Projectile.ai[0];
		private float DashTicks => Projectile.ai[1];

		public override void SetStaticDefaults()
		{
			ProjectileID.Sets.TrailCacheLength[Type] = 10;
			ProjectileID.Sets.TrailingMode[Type] = 0;
		}

		public override void SetDefaults()
		{
			Projectile.width = 40;
			Projectile.height = 80;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 400;
		}

		public override void AI()
		{
			if (Timer == 0) {
				Projectile.localAI[1] = Projectile.velocity.X;
				Projectile.localAI[2] = Projectile.velocity.Y;
				Projectile.timeLeft = (int)(Delay + DashTicks + 30);
			}
			Timer++;
			if (Timer < Delay) {
				Projectile.velocity = Vector2.Zero;
			}
			else if (Timer < Delay + DashTicks) {
				Projectile.velocity = new Vector2(Projectile.localAI[1], Projectile.localAI[2]);
				if (Timer == (int)Delay && !Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Dash with { Pitch = 0.4f, Volume = 0.4f }, Projectile.Center);
			}
			else {
				Projectile.velocity *= 0.85f;
			}
		}

		public override bool CanHitPlayer(Player target) => Timer >= Delay && Timer < Delay + DashTicks;

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D tex = ReTex.RefrainSheet;
			Rectangle src = tex.VFrame(14, 24); // dash pose
			Vector2 origin = new(72, 104);
			Vector2 pos = Projectile.Center - Main.screenPosition;
			Vector2 dir = new(Projectile.localAI[1], Projectile.localAI[2]);
			SpriteEffects fx = dir.X < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			if (Timer < Delay) {
				// the path she will take, written in faint glyph light
				float k = Timer / Delay;
				FX.Beam(sb, pos, dir.ToRotation(), dir.Length() * DashTicks, 18f, ReUtils.Gold, 0.25f * k);
				sb.Draw(tex, pos, src, ReUtils.Gold.Additive(0.25f * k), 0f, origin, 1f, fx, 0f);
				return false;
			}
			float fade = ReUtils.InverseLerp(Delay + DashTicks + 30, Delay + DashTicks, Timer);
			for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
				if (Projectile.oldPos[i] == Vector2.Zero)
					continue;
				float t = i / (float)Projectile.oldPos.Length;
				Vector2 p = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
				sb.Draw(tex, p, src, ReUtils.GoldRamp(1f - t).Additive((1f - t) * 0.5f * fade), 0f, origin, 1f, fx, 0f);
			}
			sb.Draw(tex, pos, src, ReUtils.PaleGold.Additive(0.8f * fade), 0f, origin, 1f, fx, 0f);
			FX.Bloom(sb, pos, ReUtils.Gold, 70f, 0.3f * fade);
			return false;
		}
	}

	/// <summary>
	/// Requiem's bell: a small sigil that tolls after ai[0] ticks, ringing out ai[1] dark orbs with a
	/// gap aimed at the player (edged with breakable gold orbs).
	/// </summary>
	public class BellSigil : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/Projectiles/SigilInner";
		private ref float Timer => ref Projectile.localAI[0];
		private float Delay => Projectile.ai[0];
		private float Count => Projectile.ai[1];

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 600;
		}

		public override bool? CanDamage() => false;

		public override void AI()
		{
			if (Timer == 0)
				Projectile.timeLeft = (int)Delay + 40;
			Timer++;
			Projectile.velocity = Vector2.Zero;
			if (Timer >= Delay && Projectile.localAI[1] == 0f) {
				Projectile.localAI[1] = 1f;
				Player p = Main.player[Player.FindClosest(Projectile.position, 1, 1)];
				float gap = Projectile.Center.SafeDirectionTo(p.Center).ToRotation();
				int n = Math.Max(8, (int)Count);
				if (Main.netMode != NetmodeID.MultiplayerClient) {
					for (int i = 0; i < n; i++) {
						float a = gap + MathHelper.TwoPi * i / n;
						float d = MathHelper.WrapAngle(a - gap);
						if (Math.Abs(d) < MathHelper.TwoPi / n * 1.6f)
							continue;   // the way through
						bool edge = Math.Abs(d) < MathHelper.TwoPi / n * 2.7f;
						Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, a.ToRotationVector2() * 3.4f, ModContent.ProjectileType<SongOrb>(),
							Projectile.damage, 0f, Main.myPlayer, edge ? SongOrb.Gold : SongOrb.Dark, SongOrb.Straight, 1.012f);
					}
				}
				if (!Main.dedServ) {
					SoundEngine.PlaySound(TwinSounds.Bell, Projectile.Center);
					ParticleSystem.Ring(Projectile.Center, ReUtils.Ember, 0.1f, 1.1f, 30);
					ScreenFX.Shake(5f);
				}
			}
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float appear = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 20, Timer));
			float charge = ReUtils.InverseLerp(0, Delay, Timer);
			float vanish = ReUtils.InverseLerp(Delay + 40, Delay + 10, Timer);
			float s = 0.32f * MathHelper.Max(appear, 0.01f);
			float a = vanish * MathHelper.Clamp(appear, 0f, 1f);
			FX.Smoke(sb, pos, Color.Black * (0.45f * a), 110f * s / 0.32f);
			sb.Draw(ReTex.SigilInnerGlow, pos, null, ReUtils.Ember.Additive(0.5f * a * charge), Timer * 0.02f, ReTex.SigilInnerGlow.Size() / 2f, s, SpriteEffects.None, 0f);
			sb.Draw(ReTex.SigilInner, pos, null, Color.Lerp(ReUtils.DeepGold, ReUtils.PaleGold, charge).Additive(a), Timer * 0.02f, ReTex.SigilInner.Size() / 2f, s, SpriteEffects.None, 0f);
			// the bell: a swinging glyph in the centre
			float swing = MathF.Sin(Timer * 0.25f) * 0.35f * charge;
			sb.Draw(ReTex.GlyphAtlasGlow, pos, ReTex.GlyphFrame(17), ReUtils.PaleGold.Additive(a), swing, new Vector2(7, 9), 2.4f, SpriteEffects.None, 0f);
			if (Timer >= Delay) {
				float k = ReUtils.InverseLerp(Delay, Delay + 30, Timer);
				FX.Ring(sb, pos, ReUtils.Gold, 40f + k * 160f, 1f - k);
			}
			return false;
		}
	}

	/// <summary>
	/// A line of verse: telegraphs for ai[0] ticks, burns for ai[1] ticks, slowly turning by ai[2] rad/tick.
	/// Spawned with velocity = unit direction; position = origin of the line.
	/// </summary>
	public class VerseChain : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/GlyphStrip";
		private ref float Timer => ref Projectile.localAI[0];
		private float Tele => Projectile.ai[0];
		private float Burn => Projectile.ai[1];
		private float Spin => Projectile.ai[2];
		public const float Length = 2200f;
		private static readonly List<Vector2> pts = new(40);

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 600;
		}

		public override void AI()
		{
			if (Timer == 0) {
				Projectile.rotation = Projectile.velocity.ToRotation();
				Projectile.timeLeft = (int)(Tele + Burn + 20);
			}
			Timer++;
			Projectile.velocity = Vector2.Zero;
			if (Timer > Tele)
				Projectile.rotation += Spin;
			if (Timer == (int)Tele && !Main.dedServ) {
				SoundEngine.PlaySound(TwinSounds.Beam with { Volume = 0.4f, Pitch = 0.2f }, Projectile.Center);
				ScreenFX.Shake(3f);
			}
		}

		public override bool CanHitPlayer(Player target) => Timer > Tele && Timer < Tele + Burn;

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
		{
			float _ = 0f;
			Vector2 end = Projectile.Center + Projectile.rotation.ToRotationVector2() * Length;
			return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, end, 22f, ref _);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 pos = Projectile.Center - Main.screenPosition;
			float rot = Projectile.rotation;
			if (Timer <= Tele) {
				float k = Timer / Tele;
				float flick = 0.6f + 0.4f * MathF.Sin(Timer * (0.3f + k * 0.6f));
				FX.Beam(sb, pos, rot, Length, 4f + k * 6f, ReUtils.Gold, 0.5f * flick, ReTex.Thread);
				// glyphs crawling along where the chain will be
				for (float d = (Timer * 9f) % 60f; d < Length; d += 60f)
					sb.Draw(ReTex.GlyphAtlasGlow, pos + rot.ToRotationVector2() * d, ReTex.GlyphFrame((int)(d / 60f) * 5), ReUtils.Gold.Additive(0.5f * k * (1f - d / Length)), rot + MathHelper.PiOver2, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
				return false;
			}
			float fade = ReUtils.InverseLerp(Tele + Burn + 20, Tele + Burn, Timer) * ReUtils.InverseLerp(Tele, Tele + 6, Timer);
			Vector2 start = Projectile.Center;
			Vector2 dir = rot.ToRotationVector2();
			float scroll = -Timer * 0.03f;
			Primitives.WithWorldBatchPaused(sb, () => {
				pts.Clear();
				for (int i = 0; i < 30; i++)
					pts.Add(start + dir * Length * (i / 29f));
				Primitives.DrawStrip(pts, ReTex.Trail, _ => 54f, t => new Color(255, 150, 50) * (0.6f * fade * (1f - t * 0.5f)), scroll * 0.5f, 4f);
				Primitives.DrawStrip(pts, ReTex.GlyphStrip, _ => 30f, t => new Color(255, 230, 160) * (fade * (1f - t * 0.4f)), scroll, Length / (ReTex.GlyphStrip.Width * 30f / ReTex.GlyphStrip.Height));
				Primitives.DrawStrip(pts, ReTex.Thread, _ => 6f, t => Color.White * (0.8f * fade), 0f, 1f);
			});
			FX.Bloom(sb, pos, ReUtils.PaleGold, 60f, 0.6f * fade);
			return false;
		}
	}

	/// <summary>
	/// The duet: a beam strung between the two sisters with one gap the player must slip through.
	/// ai[0] = Requiem whoAmI, ai[1] = Refrain whoAmI, ai[2] = telegraph ticks. localAI[1] = gap position (0..1).
	/// </summary>
	public class DuetBeam : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/GlyphStrip";
		private ref float Timer => ref Projectile.localAI[0];
		private static readonly List<Vector2> pts = new(48);
		public const float GapWidth = 150f;

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 330;
		}

		private bool Ends(out Vector2 a, out Vector2 b)
		{
			NPC na = Main.npc[(int)Projectile.ai[0]];
			NPC nb = Main.npc[(int)Projectile.ai[1]];
			a = na.Center;
			b = nb.Center;
			return na.active && nb.active && na.ModNPC is TwinBase && nb.ModNPC is TwinBase;
		}

		/// <summary>0..1 along the beam: drifts back and forth so the safe spot keeps moving.</summary>
		private float Gap => 0.5f + 0.32f * MathF.Sin(Timer * 0.018f + 1.3f);

		public override void AI()
		{
			Timer++;
			if (!Ends(out Vector2 a, out Vector2 b)) {
				Projectile.Kill();
				return;
			}
			Projectile.Center = (a + b) / 2f;
			if (Timer == (int)Projectile.ai[2] && !Main.dedServ)
				SoundEngine.PlaySound(TwinSounds.Beam, Projectile.Center);
		}

		public override bool CanHitPlayer(Player target) => Timer > Projectile.ai[2];

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
		{
			if (!Ends(out Vector2 a, out Vector2 b))
				return false;
			Vector2 d = b - a;
			float L = d.Length();
			float g = Gap;
			float half = GapWidth / 2f / L;
			float _ = 0f;
			Vector2 g0 = a + d * (g - half);
			Vector2 g1 = a + d * (g + half);
			return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), a, g0, 26f, ref _)
				|| Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), g1, b, 26f, ref _);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			if (!Ends(out Vector2 a, out Vector2 b))
				return false;
			SpriteBatch sb = Main.spriteBatch;
			float tele = Projectile.ai[2];
			float live = ReUtils.InverseLerp(tele, tele + 8, Timer);
			float fade = ReUtils.InverseLerp(330, 300, Timer);
			Vector2 d = b - a;
			float L = d.Length();
			float half = GapWidth / 2f / L;
			float g = Gap;
			float scroll = -Timer * 0.025f;
			Primitives.WithWorldBatchPaused(sb, () => {
				DrawSegment(a, a + d * (g - half), live, fade, scroll);
				DrawSegment(a + d * (g + half), b, live, fade, -scroll);
			});
			// the gap shines so it can be read at a glance
			Vector2 gc = a + d * g - Main.screenPosition;
			FX.Bloom(sb, gc, Color.White, 50f, 0.35f * fade);
			FX.Flare(sb, gc, ReUtils.PaleGold, 40f, 0.6f * fade, d.ToRotation() + MathHelper.PiOver2, 0.3f);
			return false;
		}

		private static void DrawSegment(Vector2 s, Vector2 e, float live, float fade, float scroll)
		{
			pts.Clear();
			for (int i = 0; i < 24; i++)
				pts.Add(Vector2.Lerp(s, e, i / 23f));
			float len = (e - s).Length();
			if (live <= 0f) {
				Primitives.DrawStrip(pts, ReTex.Thread, _ => 6f, _ => ReUtils.Gold * (0.6f * fade), 0f, 1f);
				return;
			}
			Primitives.DrawStrip(pts, ReTex.Trail, _ => 64f * live, _ => new Color(255, 140, 60) * (0.55f * fade), scroll * 0.5f, len / 400f);
			Primitives.DrawStrip(pts, ReTex.GlyphStrip, _ => 34f * live, _ => new Color(255, 232, 170) * fade, scroll, len / (ReTex.GlyphStrip.Width * 34f / ReTex.GlyphStrip.Height));
			Primitives.DrawStrip(pts, ReTex.Thread, _ => 8f, _ => Color.White * (0.8f * fade), 0f, 1f);
		}
	}
}
