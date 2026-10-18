using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Replicant.Common;
using Replicant.Common.VFX;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace Replicant.Content.Projectiles.Twins
{
	/// <summary>Shared timing of the Aria (ticks since it began, after its unveiling).</summary>
	public static class AriaTimeline
	{
		public const int RiftSpawn = 40;
		public const int OpenStart = 70, OpenEnd = 130;
		public const int DescentStart = 130, DescentEnd = 360;
		public const int ShelterAt = 360;
		public const int Flood = 600, FloodHit = 622;
		public const int CollapseStart = 640, CollapseEnd = 700;
		public const int Shatter = 700, End = 800;
		public const float RiftHeight = 560f;   // above the anchor
		public const float RiftWidth = 2600f;
	}

	/// <summary>
	/// The sky splitting open: a jagged wound across the heavens, black inside, burning at the lips, full of
	/// falling script. Attacks are poured out of it; at the Verdict a wall of light floods down from it.
	/// ai[0] = Requiem's whoAmI. Draw-only (the Verdict's damage is dealt by the sisters' choreography).
	/// </summary>
	public class SkyRift : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/Thread";
		private ref float Timer => ref Projectile.localAI[0];
		private float AriaT => Timer + AriaTimeline.RiftSpawn;

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = AriaTimeline.End - AriaTimeline.RiftSpawn + 30;
			Projectile.hide = false;
		}

		public override bool? CanDamage() => false;
		public override bool ShouldUpdatePosition() => false;

		public override void AI()
		{
			Timer++;
			float t = AriaT;
			if (Main.dedServ)
				return;
			if (t == AriaTimeline.OpenStart) {
				Terraria.Audio.SoundEngine.PlaySound(TwinSounds.Transform with { Volume = 0.7f, Pitch = -0.3f }, Projectile.Center);
				ScreenFX.Shake(14f);
			}
			// glass-like shards falling from the lips while it is open
			if (Open > 0.3f && Main.rand.NextBool(2)) {
				Vector2 p = Point(Projectile, Main.rand.NextFloat());
				ParticleSystem.Glyph(p, new Vector2(Main.rand.NextFloat(-0.5f, 0.5f), Main.rand.NextFloat(1f, 3f)), ReUtils.GoldRamp(Main.rand.NextFloat(0.4f, 1f)), Main.rand.NextFloat(0.8f, 1.3f), 70);
			}
			if (t >= AriaTimeline.CollapseStart && t < AriaTimeline.CollapseEnd && Main.rand.NextBool()) {
				Vector2 p = Point(Projectile, Main.rand.NextFloat());
				ParticleSystem.Spark(p, new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(2f, 8f)), ReUtils.PaleGold, 1f, 40);
			}
		}

		public float Open {
			get {
				float t = AriaT;
				return ReUtils.EaseOutCubic(ReUtils.InverseLerp(AriaTimeline.OpenStart, AriaTimeline.OpenEnd, t))
					* (1f - ReUtils.EaseInCubic(ReUtils.InverseLerp(AriaTimeline.CollapseStart, AriaTimeline.CollapseEnd, t)));
			}
		}

		private float Wide => ReUtils.EaseInOutSine(ReUtils.InverseLerp(AriaTimeline.ShelterAt + 40, AriaTimeline.Flood, AriaT)) * (1f - ReUtils.InverseLerp(AriaTimeline.Flood + 20, AriaTimeline.CollapseStart, AriaT));

		/// <summary>World point along the rift (u = 0..1 from left to right).</summary>
		public static Vector2 Point(Projectile rift, float u)
		{
			float x = (u - 0.5f) * AriaTimeline.RiftWidth;
			float y = MathF.Sin(u * 13f) * 18f + MathF.Sin(u * 31f + 1f) * 9f;
			return rift.Center + new Vector2(x, y);
		}

		public static Projectile Find()
		{
			int type = ModContent.ProjectileType<SkyRift>();
			foreach (Projectile p in Main.ActiveProjectiles) {
				if (p.type == type)
					return p;
			}
			return null;
		}

		/// <summary>
		/// The Verdict's flood: hundreds of falling streams of light and script, each its own width and speed,
		/// a white-hot churning front that sprays upward where it lands, and a heat shimmer above it.
		/// </summary>
		private void DrawFlood(SpriteBatch sb, Texture2D px, Vector2 mid, float t, float time)
		{
			float d = ReUtils.EaseInCubic(ReUtils.InverseLerp(AriaTimeline.Flood, AriaTimeline.FloodHit, t)) * 2600f;
			float a = 1f - ReUtils.InverseLerp(AriaTimeline.FloodHit + 10, AriaTimeline.CollapseEnd + 20, t);
			if (a <= 0f || d <= 1f)
				return;
			float left = Point(Projectile, 0f).X - Main.screenPosition.X;
			float w = AriaTimeline.RiftWidth;
			float top = mid.Y;
			// only what is on screen
			int x0 = (int)MathF.Max(left, -40f), x1 = (int)MathF.Min(left + w, Main.screenWidth / Main.GameViewMatrix.Zoom.X + 40f);
			// a soft body of light behind the streams
			sb.Draw(px, new Rectangle(x0, (int)top, x1 - x0, (int)d), new Color(255, 190, 90).Additive(0.16f * a));
			sb.Draw(px, new Rectangle(x0, (int)(top + d * 0.5f), x1 - x0, (int)(d * 0.5f)), new Color(255, 230, 170).Additive(0.12f * a));
			// streams
			for (int x = x0 - x0 % 6; x < x1; x += 6) {
				int col = (int)((x - left) / 6f);
				float hw = ReUtils.Hash01(col, 50), hs = ReUtils.Hash01(col, 51);
				float sw = 2f + hw * 5f;
				float flick = 0.55f + 0.45f * MathF.Sin(time * (10f + hs * 8f) + col);
				float len = d * (0.85f + 0.15f * hs);
				Color c = Color.Lerp(new Color(255, 170, 60), Color.White, hw * 0.7f);
				sb.Draw(px, new Rectangle(x, (int)top, (int)sw, (int)len), c.Additive(0.22f * a * flick));
				// script riding down the stream
				if (col % 4 == 0) {
					float y = (time * (700f + hs * 500f) + hw * 3000f) % MathF.Max(1f, len);
					sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x + sw / 2f, top + y), ReTex.GlyphFrame(col + (int)(time * 8f)), Color.White.Additive(0.75f * a), 0f, new Vector2(7, 9), 1.3f, SpriteEffects.None, 0f);
				}
			}
			// the churning front
			float front = top + d;
			for (int x = x0 - x0 % 4; x < x1; x += 4) {
				float j = MathF.Sin(x * 0.05f + time * 20f) * 6f + MathF.Sin(x * 0.013f - time * 9f) * 10f;
				sb.Draw(px, new Rectangle(x, (int)(front + j - 10), 4, 20), Color.White.Additive(0.85f * a));
				sb.Draw(px, new Rectangle(x, (int)(front + j - 26), 4, 16), new Color(255, 214, 140).Additive(0.4f * a));
			}
			FX.Bloom(sb, new Vector2((x0 + x1) / 2f, front), Color.White, (x1 - x0) * 0.6f, 0.4f * a, 1f, 0.08f);
			// spray thrown up where it lands
			if (!Main.dedServ && t >= AriaTimeline.FloodHit - 4 && t < AriaTimeline.FloodHit + 20) {
				for (int k = 0; k < 6; k++) {
					Vector2 wp = new(Main.screenPosition.X + Main.rand.NextFloat(x0, x1), Main.screenPosition.Y + front);
					ParticleSystem.Spark(wp, new Vector2(Main.rand.NextFloat(-4f, 4f), -Main.rand.NextFloat(6f, 16f)), ReUtils.PaleGold, 1.2f, 30);
				}
			}
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float open = Open;
			float wide = Wide;
			float time = Main.GlobalTimeWrappedHourly;
			Vector2 sp = Main.screenPosition;
			const int col = 8;
			int n = (int)(AriaTimeline.RiftWidth / col);
			// grow from the middle outward
			float reach = ReUtils.EaseOutCubic(ReUtils.InverseLerp(AriaTimeline.RiftSpawn, AriaTimeline.OpenEnd, AriaT));
			for (int i = 0; i < n; i++) {
				float u = i / (float)n;
				if (MathF.Abs(u - 0.5f) * 2f > reach)
					continue;
				Vector2 c = Point(Projectile, u) - sp;
				float edgeFade = MathF.Min(1f, (1f - MathF.Abs(u - 0.5f) * 2f) * 6f);
				float jag = 0.6f + 0.4f * ReUtils.Hash01(i, 3) + 0.25f * MathF.Sin(time * 3f + i * 0.4f);
				float half = (2f + open * (22f + wide * 80f)) * jag * edgeFade;
				// the black inside
				sb.Draw(px, new Rectangle((int)c.X, (int)(c.Y - half), col, (int)(half * 2f)), new Color(4, 2, 2) * MathF.Min(1f, open * 1.4f + 0.2f));
				// burning lips
				Color lip = Color.Lerp(ReUtils.Gold, Color.White, 0.3f + 0.3f * MathF.Sin(time * 5f + i));
				sb.Draw(px, new Rectangle((int)c.X, (int)(c.Y - half - 2), col, 3), lip.Additive(0.9f * edgeFade));
				sb.Draw(px, new Rectangle((int)c.X, (int)(c.Y + half - 1), col, 3), lip.Additive(0.9f * edgeFade));
				// script falling inside the wound
				if (open > 0.2f && i % 3 == 0) {
					float fall = (time * 60f + ReUtils.Hash01(i, 7) * 200f) % (half * 2f + 1f);
					sb.Draw(ReTex.GlyphAtlasGlow, c + new Vector2(col / 2f, -half + fall), ReTex.GlyphFrame(i + (int)(time * 4f)), ReUtils.Gold.Additive(0.7f * open), 0f, new Vector2(7, 9), 0.8f, SpriteEffects.None, 0f);
				}
			}
			// light bleeding out along its length
			Vector2 mid = Projectile.Center - sp;
			FX.Bloom(sb, mid, ReUtils.Gold, AriaTimeline.RiftWidth * 0.55f, (0.12f + wide * 0.25f) * open, 1f, 0.06f + wide * 0.1f);
			// cracks spreading through the sky from the wound
			if (open > 0.05f) {
				Texture2D th = ReTex.Thread;
				for (int k = 0; k < 16; k++) {
					float u = ReUtils.Hash01(k, 11);
					Vector2 a = Point(Projectile, u) - sp;
					float ang = (ReUtils.Hash01(k, 12) < 0.5f ? -1f : 1f) * MathHelper.PiOver2 + (ReUtils.Hash01(k, 13) - 0.5f) * 1.6f;
					float len = (60f + ReUtils.Hash01(k, 14) * 220f) * open;
					for (int s = 0; s < 3; s++) {
						float a2 = ang + (ReUtils.Hash01(k, s + 20) - 0.5f) * 0.8f;
						Vector2 b = a + a2.ToRotationVector2() * len / 3f;
						sb.Draw(th, a, null, ReUtils.PaleGold.Additive(0.6f * open), (b - a).ToRotation(), new Vector2(0, th.Height / 2f), new Vector2((b - a).Length() / th.Width, 1.5f / th.Height), SpriteEffects.None, 0f);
						a = b;
					}
				}
			}
			// the Verdict: a wall of light floods down out of it
			float t = AriaT;
			if (t >= AriaTimeline.Flood && t < AriaTimeline.CollapseEnd + 20)
				DrawFlood(sb, px, mid, t, time);
			return false;
		}
	}

	/// <summary>
	/// A sanctuary: a bell of light that falls from the rift onto the ground. During the Verdict it is the only
	/// place in the world where the light does not reach. ai[0] = ticks until the Verdict.
	/// </summary>
	public class Sanctuary : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/Thread";
		public const float Radius = 120f;
		private ref float Timer => ref Projectile.localAI[0];
		private float Until => Projectile.ai[0];

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
		public override bool ShouldUpdatePosition() => false;

		public override void AI()
		{
			if (Timer == 0)
				Projectile.timeLeft = (int)Until + 70;
			Timer++;
			Lighting.AddLight(Projectile.Center - new Vector2(0, 60), 1.0f, 0.85f, 0.5f);
			if (!Main.dedServ && Timer == 1) {
				Terraria.Audio.SoundEngine.PlaySound(TwinSounds.Bell with { Pitch = 0.2f }, Projectile.Center);
				ParticleSystem.Ring(Projectile.Center, ReUtils.PaleGold, 0.05f, 1.12f, 30);
			}
			if (!Main.dedServ && Timer < Until && (int)(Until - Timer) % 60 == 0) {
				// the countdown tolls
				Terraria.Audio.SoundEngine.PlaySound(TwinSounds.Bell with { Volume = 0.7f, Pitch = -0.2f + 0.2f * (3 - (Until - Timer) / 60f) });
				ParticleSystem.Ring(Projectile.Center - new Vector2(0, 60), ReUtils.Gold, 0.3f, 1.06f, 24);
			}
		}

		/// <summary>The bell's interior: a dome resting on the ground.</summary>
		public static bool IsSheltered(Player p)
		{
			int type = ModContent.ProjectileType<Sanctuary>();
			foreach (Projectile proj in Main.ActiveProjectiles) {
				if (proj.type != type)
					continue;
				Vector2 d = p.Center - proj.Center;
				if (d.Y < 20f && new Vector2(d.X, d.Y * 1.25f).Length() < Radius)
					return true;
			}
			return false;
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Vector2 c = Projectile.Center - Main.screenPosition;
			float time = Main.GlobalTimeWrappedHourly;
			float appear = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 30, Timer));
			float remaining = Until - Timer;
			bool sheltering = remaining <= 0;
			float pulse = 0.8f + 0.2f * MathF.Sin(time * 6f);
			// beacon pillar up to the rift, so it can be found
			Texture2D ray = ReTex.GodRay;
			sb.Draw(ray, c, null, ReUtils.PaleGold.Additive(0.35f * appear * pulse), MathHelper.Pi, new Vector2(ray.Width / 2f, 0f), new Vector2(1.2f, 900f / ray.Height), SpriteEffects.None, 0f);
			// the dome: rings squashed into a bell, glyphs running along its rim
			Texture2D ring = ReTex.Ring;
			for (int i = 0; i < 3; i++) {
				float r = Radius * (1f - i * 0.12f) * appear;
				float s = r * 2f / (ring.Width * 0.9f);
				sb.Draw(ring, c, null, ReUtils.Gold.Additive((0.55f - i * 0.12f) * (sheltering ? 1.4f : 1f)), 0f, ring.Size() / 2f, new Vector2(s, s * 0.8f), SpriteEffects.None, 0f);
			}
			FX.Bloom(sb, c - new Vector2(0, 30), ReUtils.PaleGold, Radius * 1.3f, (sheltering ? 0.45f : 0.22f) * appear, 1f, 0.85f);
			for (int g = 0; g < 18; g++) {
				float a = MathHelper.Pi + MathHelper.Pi * g / 17f + MathF.Sin(time) * 0.05f;
				Vector2 p = c + new Vector2(MathF.Cos(a) * Radius, MathF.Sin(a) * Radius * 0.8f) * appear;
				sb.Draw(ReTex.GlyphAtlasGlow, p, ReTex.GlyphFrame(g + (int)(time * 3f)), ReUtils.PaleGold.Additive(0.9f * appear), a + MathHelper.PiOver2, new Vector2(7, 9), 1.1f, SpriteEffects.None, 0f);
			}
			// ground sigil
			Texture2D sig = ReTex.SigilInner;
			sb.Draw(sig, c + new Vector2(0, 2), null, ReUtils.Gold.Additive(0.7f * appear), time * 0.5f, sig.Size() / 2f, new Vector2(Radius * 2f / sig.Width, Radius * 0.5f / sig.Height), SpriteEffects.None, 0f);
			// countdown
			if (remaining > 0) {
				int n = (int)MathF.Ceiling(remaining / 60f);
				DynamicSpriteFont font = FontAssets.DeathText.Value;
				string txt = n.ToString();
				float beat = 1f + 0.35f * MathF.Max(0f, 1f - (remaining % 60f) / 12f) * -1f + 0.35f;
				Vector2 size = font.MeasureString(txt);
				Vector2 tp = c - new Vector2(0, Radius + 70f);
				sb.DrawString(font, txt, tp + new Vector2(3, 3), Color.Black * 0.8f, 0f, size / 2f, 1.6f * beat, SpriteEffects.None, 0f);
				sb.DrawString(font, txt, tp, Color.Lerp(ReUtils.PaleGold, Color.White, 0.4f), 0f, size / 2f, 1.6f * beat, SpriteEffects.None, 0f);
				// off-screen pointer toward the sanctuary
				Player lp = Main.LocalPlayer;
				Vector2 screenC = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
				Vector2 rel = c - screenC;
				Vector2 half = new(Main.screenWidth / 2f / Main.GameViewMatrix.Zoom.X - 60f, Main.screenHeight / 2f / Main.GameViewMatrix.Zoom.Y - 60f);
				if (MathF.Abs(rel.X) > half.X || MathF.Abs(rel.Y) > half.Y) {
					float k = MathF.Min(half.X / MathF.Max(1f, MathF.Abs(rel.X)), half.Y / MathF.Max(1f, MathF.Abs(rel.Y)));
					Vector2 edge = screenC + rel * k;
					float rot = rel.ToRotation();
					FX.Flare(sb, edge, ReUtils.PaleGold, 46f * pulse, 0.9f, rot, 0.35f);
					sb.Draw(ReTex.GlyphAtlasGlow, edge, ReTex.GlyphFrame(9), Color.White.Additive(1f), rot + MathHelper.PiOver2, new Vector2(7, 9), 2f, SpriteEffects.None, 0f);
				}
			}
			return false;
		}
	}
}

namespace Replicant.Content.Projectiles.Twins
{
	/// <summary>Refrain's music stand, standing in the field while the score is played. Visual only.</summary>
	public class MusicStand : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/Projectiles/MusicStand";
		private ref float Timer => ref Projectile.localAI[0];

		public override void SetDefaults()
		{
			Projectile.width = 40;
			Projectile.height = 80;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 3600;
		}

		public override bool? CanDamage() => false;
		public override bool ShouldUpdatePosition() => false;

		public override void AI()
		{
			Timer++;
			Lighting.AddLight(Projectile.Center, 0.9f, 0.7f, 0.35f);
			if (Timer > 30 && !Common.Cutscene.RhythmStand.Active && !Main.dedServ) {
				for (int i = 0; i < 16; i++)
					ParticleSystem.Glyph(Projectile.Center + Main.rand.NextVector2Circular(30, 50), Main.rand.NextVector2Circular(3f, 3f) - Vector2.UnitY * 2f, ReUtils.Gold, 1f, 40);
				Projectile.Kill();
			}
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D tex = TextureAssets.Projectile[Type].Value;
			Texture2D glow = ModContent.Request<Texture2D>(Texture + "_Glow").Value;
			float appear = ReUtils.EaseOutBack(ReUtils.InverseLerp(0, 24, Timer));
			Vector2 pos = Projectile.Center - Main.screenPosition + new Vector2(0, (1f - appear) * 40f);
			float time = Main.GlobalTimeWrappedHourly;
			FX.Bloom(sb, pos + new Vector2(0, 40), ReUtils.Gold, 90f, 0.18f * appear, 1.6f, 0.4f);
			sb.Draw(tex, pos, null, Color.White * appear, MathF.Sin(time * 1.5f) * 0.02f, tex.Size() / 2f, 1.6f, SpriteEffects.None, 0f);
			sb.Draw(glow, pos, null, Color.White.Additive(appear * (0.8f + 0.2f * MathF.Sin(time * 4f))), MathF.Sin(time * 1.5f) * 0.02f, tex.Size() / 2f, 1.6f, SpriteEffects.None, 0f);
			// notes drifting off the page
			for (int i = 0; i < 4; i++) {
				float k = (time * 0.4f + i * 0.25f) % 1f;
				Vector2 q = pos + new Vector2(-20f + i * 13f + MathF.Sin(time * 2f + i) * 6f, -50f - k * 60f);
				sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(i * 9 + (int)(time)), ReUtils.PaleGold.Additive((1f - k) * 0.8f * appear), 0f, new Vector2(7, 9), 0.9f, SpriteEffects.None, 0f);
			}
			return false;
		}
	}
}

namespace Replicant.Content.Projectiles.Twins
{
	/// <summary>
	/// The Descent: a waterfall of light and script pouring straight down out of the middle of the rift and
	/// widening — the ground beneath the wound is taken from you, push out to the left or the right.
	/// ai[0] = Requiem whoAmI (anchor of the rift), ai[1] = duration.
	/// </summary>
	public class RiftFall : ModProjectile, IReProjectile
	{
		public override string Texture => "Replicant/Assets/Textures/VFX/Thread";
		private ref float Timer => ref Projectile.localAI[0];
		private float Duration => Projectile.ai[1];
		public const int Warn = 40;
		public const float MaxHalf = 300f;

		public override void SetDefaults()
		{
			Projectile.width = 10;
			Projectile.height = 10;
			Projectile.hostile = true;
			Projectile.tileCollide = false;
			Projectile.penetrate = -1;
			Projectile.timeLeft = 600;
		}

		public override bool ShouldUpdatePosition() => false;

		public override void AI()
		{
			if (Timer == 0)
				Projectile.timeLeft = (int)Duration + 30;
			Timer++;
			if (!Main.dedServ && Timer == Warn) {
				Terraria.Audio.SoundEngine.PlaySound(TwinSounds.Beam with { Pitch = -0.4f, Volume = 0.9f }, Projectile.Center);
				ScreenFX.Shake(12f);
			}
			if (!Main.dedServ && Timer > Warn && Main.rand.NextBool(2)) {
				float hh = Half;
				Vector2 at = new(Projectile.Center.X + Main.rand.NextFloat(-hh, hh), Projectile.Center.Y + 1300f);
				ParticleSystem.Spark(at, new Vector2(Main.rand.NextFloat(-6f, 6f), -Main.rand.NextFloat(4f, 12f)), ReUtils.PaleGold, 1f, 26);
			}
		}

		/// <summary>Half-width of the fall: widening, then closing at the end.</summary>
		public float Half => MaxHalf * ReUtils.EaseInOutSine(ReUtils.InverseLerp(Warn, Warn + 120, Timer)) * (1f - ReUtils.InverseLerp(Duration - 20, Duration + 10, Timer)) + (Timer > Warn ? 40f : 0f);

		public override bool CanHitPlayer(Player target) => Timer > Warn + 6 && Timer < Duration;

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
		{
			float hh = Half - 10f;
			Rectangle fall = new((int)(Projectile.Center.X - hh), (int)Projectile.Center.Y, (int)(hh * 2f), 2600);
			return fall.Intersects(targetHitbox);
		}

		public override bool PreDraw(ref Color lightColor)
		{
			SpriteBatch sb = Main.spriteBatch;
			Texture2D px = TextureAssets.MagicPixel.Value;
			float time = Main.GlobalTimeWrappedHourly;
			Vector2 c = Projectile.Center - Main.screenPosition;
			float fade = ReUtils.InverseLerp(Duration + 20, Duration, Timer);
			const float len = 2600f;
			if (Timer < Warn) {
				// warning: the edges it will fill are traced, flickering faster and faster
				float k = Timer / Warn;
				float flick = MathF.Sin(Timer * (0.4f + k)) > 0f ? 1f : 0.4f;
				for (int side = -1; side <= 1; side += 2) {
					float x = c.X + side * (40f + MaxHalf * 0.5f * k);
					sb.Draw(px, new Rectangle((int)x - 1, (int)c.Y, 2, (int)len), ReUtils.Gold.Additive(0.6f * flick));
					for (float y = (time * 200f) % 60f; y < len; y += 60f)
						sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(x, c.Y + y), ReTex.GlyphFrame((int)(y / 60f)), ReUtils.PaleGold.Additive(0.5f * flick), 0f, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
				}
				return false;
			}
			float hh = Half;
			// body
			sb.Draw(px, new Rectangle((int)(c.X - hh), (int)c.Y, (int)(hh * 2f), (int)len), new Color(255, 180, 80).Additive(0.18f * fade));
			for (float x = -hh; x < hh; x += 5f) {
				int col = (int)((x + MaxHalf) / 5f);
				float hs = ReUtils.Hash01(col, 61);
				float flick = 0.6f + 0.4f * MathF.Sin(time * (12f + hs * 10f) + col);
				sb.Draw(px, new Rectangle((int)(c.X + x), (int)c.Y, 2 + (int)(hs * 3f), (int)len), Color.Lerp(new Color(255, 190, 90), Color.White, hs).Additive(0.25f * fade * flick));
				if (col % 3 == 0) {
					float y = (time * (900f + hs * 700f) + hs * 2000f) % len;
					sb.Draw(ReTex.GlyphAtlasGlow, new Vector2(c.X + x, c.Y + y), ReTex.GlyphFrame(col + (int)(time * 10f)), Color.White.Additive(0.8f * fade), 0f, new Vector2(7, 9), 1.2f, SpriteEffects.None, 0f);
				}
			}
			// burning edges
			for (int side = -1; side <= 1; side += 2) {
				float x = c.X + side * hh;
				sb.Draw(px, new Rectangle((int)x - 2, (int)c.Y, 4, (int)len), Color.White.Additive(0.9f * fade));
				FX.Bloom(sb, new Vector2(x, c.Y + 500f), ReUtils.Gold, 120f, 0.3f * fade, 0.25f, 6f);
			}
			return false;
		}
	}
}
