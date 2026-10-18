using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Replicant.Common;
using Replicant.Common.Cutscene;
using Replicant.Common.Dimension;
using Replicant.Common.VFX;
using Replicant.Content.Projectiles.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace Replicant.Content.NPCs.Twins
{
	/// <summary>
	/// The set pieces: the Aria (shielded choreography under a split sky, ending in a Verdict you can only
	/// survive inside a sanctuary), the Intrusion (Refrain hacks the player), and the Frenzy (the sister left
	/// behind goes into a bullet-hell rage with her sister's ghost beside her).
	/// </summary>
	public abstract partial class TwinBase
	{
		public const int StHack = 24, StAria = 35, StFrenzy = 60;

		/// <summary>Testing: jump straight into a state.</summary>
		public void DebugForce(int state)
		{
			Awakened = true;
			Enter(state);
		}

		/// <summary>Testing: start the Aria now.</summary>
		public void DebugStartAria(TwinBase other)
		{
			Awakened = other.Awakened = true;
			TwinsFight.LastAria = FightTicks;
			StartDuo(StAria, other, Target);
		}

		public bool IsSpecialState => State == StAria || State == StHack || State == StFrenzy;

		private float shellAlpha;     // the Aria's shield shell
		private float frenzyAlpha;

		/// <summary>Runs the set pieces. Unveilings (negative timer) are handled here too.</summary>
		private void RunSpecial(Player p)
		{
			switch ((int)State) {
				case StAria:
					if (Timer < 0)
						DoAnnounce(p);
					else
						Aria(p);
					break;
				case StHack:
					if (Timer < 0)
						DoAnnounce(p);
					else
						Intrusion(p);
					break;
				case StFrenzy:
					Frenzy(p);
					break;
			}
		}

		// =================================================================== the Aria
		private void Aria(Player p)
		{
			int t = (int)Timer;
			TwinBase other = Sister;
			// positions: high above the player, either side of the rift
			Vector2 spot = Anchor + new Vector2(Side * 280f, -330f + MathF.Sin(t * 0.05f + Side) * 18f);
			Hover(spot, t < 70 ? 18f : 8f, t < 70 ? 6f : 14f);
			SetPose(t < 40 ? TwinPose.Cast : t >= AriaTimeline.Shatter ? TwinPose.Hurt : TwinPose.Sing);
			NPC.dontTakeDamage = false;
			Shielded = t >= 20 && t < AriaTimeline.Shatter;

			if (t == 0)
				Lean(90, 0.45f);
			if (t == 20 && !Main.dedServ) {
				SoundEngine.PlaySound(TwinSounds.Chain with { Pitch = -0.2f }, NPC.Center);
				ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.12f, 26);
			}
			if (this is Requiem && t == AriaTimeline.RiftSpawn && Server)
				Projectile.NewProjectile(NPC.GetSource_FromAI(), Anchor - new Vector2(0, AriaTimeline.RiftHeight), Vector2.Zero, ModContent.ProjectileType<SkyRift>(), 0, 0f, Main.myPlayer, NPC.whoAmI);
			// the camera looks up at the wound as it opens
			if (!Main.dedServ && t >= AriaTimeline.RiftSpawn && t < AriaTimeline.OpenEnd + 30 && p.whoAmI == Main.myPlayer)
				ScreenFX.Focus(Anchor - new Vector2(0, AriaTimeline.RiftHeight * 0.55f), ReUtils.Bump(ReUtils.InverseLerp(AriaTimeline.RiftSpawn, AriaTimeline.OpenEnd + 30, t)) * 0.6f);

			// the whole stage pulls back to one fixed framing while the wound is open, then comes home
			if (!Main.dedServ && this is Requiem && t >= AriaTimeline.RiftSpawn && t < AriaTimeline.End) {
				float zin = ReUtils.EaseInOutSine(ReUtils.InverseLerp(AriaTimeline.RiftSpawn, AriaTimeline.RiftSpawn + 60, t));
				float zout = 1f - ReUtils.EaseInOutSine(ReUtils.InverseLerp(AriaTimeline.Shatter, AriaTimeline.End, t));
				ScreenFX.HoldFixedZoom(0.86f, zin * zout);
			}

			Projectile rift = SkyRift.Find();
			// the wound pours straight down: a widening fall of light splits the arena — get to a side
			if (this is Requiem && rift != null && t == AriaTimeline.DescentStart && Server)
				Projectile.NewProjectile(NPC.GetSource_FromAI(), rift.Center, Vector2.Zero, ModContent.ProjectileType<RiftFall>(), ReUtils.ProjDamage(150), 0f, Main.myPlayer, NPC.whoAmI, AriaTimeline.DescentEnd - AriaTimeline.DescentStart);
			// ---- the Descent: lances poured from the rift in a sweeping, rhythmic curtain
			if (this is Requiem && rift != null && t >= AriaTimeline.DescentStart && t < AriaTimeline.DescentEnd && (t - AriaTimeline.DescentStart) % 10 == 0 && Server) {
				float k = (t - AriaTimeline.DescentStart) / (float)(AriaTimeline.DescentEnd - AriaTimeline.DescentStart);
				float sweep = k < 0.5f ? k * 2f : 2f - k * 2f;              // left → right → left
				for (int j = 0; j < 3; j++) {
					float u = MathHelper.Clamp(0.5f + (p.Center.X - rift.Center.X) / AriaTimeline.RiftWidth + (sweep - 0.5f) * 0.55f + (j - 1) * 0.06f, 0.02f, 0.98f);
					// never inside the fall itself: the lances own the flanks
					float off = (u - 0.5f) * AriaTimeline.RiftWidth;
					float keep = RiftFall.MaxHalf + 80f;
					if (MathF.Abs(off) < keep)
						u = 0.5f + MathF.CopySign(keep + Main.rand.NextFloat(0f, 260f), off == 0f ? (j - 1 + 0.5f) : off) / AriaTimeline.RiftWidth;
					Vector2 from = SkyRift.Point(rift, u);
					Vector2 dir = new Vector2((sweep - 0.5f) * 0.3f, 1f);
					dir.Normalize();
					Projectile.NewProjectile(NPC.GetSource_FromAI(), from, dir, ModContent.ProjectileType<Content.Projectiles.GlyphLance>(), ReUtils.ProjDamage(120), 0f, Main.myPlayer, 34, 24f);
				}
			}
			// Refrain sings rings of breakable notes around herself during the descent
			if (this is Refrain && t >= AriaTimeline.DescentStart && t < AriaTimeline.Flood && t % 50 == 0) {
				int n = 12;
				for (int i = 0; i < n; i++)
					Orb(NPC.Center, (MathHelper.TwoPi * i / n + t * 0.1f).ToRotationVector2() * 4.5f, i % 4 == 0 ? SongOrb.Dark : SongOrb.Gold, SongOrb.Curve, 0.008f);
				if (!Main.dedServ)
					SoundEngine.PlaySound(TwinSounds.Note(t / 50) with { Volume = 0.4f }, NPC.Center);
			}
			// ---- the countdown: a sanctuary falls; the rift widens; dark rain keeps the pressure on
			if (this is Requiem && t == AriaTimeline.ShelterAt && Server) {
				float side = Main.rand.NextBool() ? -1f : 1f;
				Vector2 at = FindGround(p.Center + new Vector2(side * Main.rand.NextFloat(420f, 620f), -200f));
				Projectile.NewProjectile(NPC.GetSource_FromAI(), at, Vector2.Zero, ModContent.ProjectileType<Sanctuary>(), 0, 0f, Main.myPlayer, AriaTimeline.Flood + 22 - AriaTimeline.ShelterAt);
			}
			if (this is Requiem && t == AriaTimeline.ShelterAt && !Main.dedServ) {
				Subtitles.Push(Subtitles.Both, "Mods.Replicant.Twins.Lines.Verdict", 220);
				Main.NewText(Language.GetTextValue("Mods.Replicant.Twins.ShelterHint"), ReUtils.PaleGold);
			}
			if (this is Requiem && rift != null && t > AriaTimeline.ShelterAt && t < AriaTimeline.Flood - 20 && t % 9 == 0) {
				Vector2 from = SkyRift.Point(rift, Main.rand.NextFloat(0.15f, 0.85f));
				Orb(from, new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), 4f), SongOrb.Dark, SongOrb.Straight, 1.01f);
			}
			// ---- the Verdict
			if (t == AriaTimeline.Flood && !Main.dedServ && this is Requiem) {
				SoundEngine.PlaySound(ReSounds.Burst with { Volume = 1f, Pitch = -0.2f });
				SoundEngine.PlaySound(ReSounds.Explode with { Volume = 0.7f, Pitch = -0.5f });
			}
			if (t == AriaTimeline.FloodHit && this is Requiem && !Main.dedServ) {
				ScreenFX.Shake(40f);
				ScreenFX.ZoomPunch(0.18f);
				Cinematic.Flash(1f);
				Player lp = Main.LocalPlayer;
				if (lp.active && !lp.dead && lp.Distance(Anchor) < 3200f) {
					if (Sanctuary.IsSheltered(lp)) {
						ParticleSystem.Ring(lp.Center, ReUtils.PaleGold, 0.05f, 1.15f, 30);
					}
					else {
						int dmg = (int)(lp.statLifeMax2 * 0.7f) + 60;
						lp.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromKey("Mods.Replicant.Twins.VerdictDeath", lp.name)), dmg, 0, dodgeable: false, armorPenetration: 9999f);
					}
				}
			}
			// ---- the shields shatter: a window to strike back
			if (t == AriaTimeline.Shatter) {
				Stunned = 200;
				NPC.velocity = new Vector2(0, 3f);
				if (!Main.dedServ) {
					SoundEngine.PlaySound(TwinSounds.Shatter with { Volume = 1f, Pitch = -0.4f }, NPC.Center);
					SoundEngine.PlaySound(TwinSounds.Shatter with { Volume = 0.8f, Pitch = 0.2f }, NPC.Center);
					ParticleSystem.GlyphBurst(NPC.Center, 50, 12f, 1.2f);
					for (int i = 0; i < 24; i++)
						ParticleSystem.Spark(NPC.Center, Main.rand.NextVector2CircularEdge(13f, 13f), ReUtils.PaleGold, 1.1f, 30);
					ScreenFX.Shake(12f);
					Lean(60, 0.3f);
				}
			}
			if (t >= AriaTimeline.Shatter)
				NPC.velocity *= 0.95f;
			if (t >= AriaTimeline.End) {
				Shielded = false;
				Enter(StIdle);
			}
		}

		protected static Vector2 FindGround(Vector2 from)
		{
			int x = (int)(from.X / 16f);
			int y0 = Math.Max(10, (int)(from.Y / 16f));
			for (int y = y0; y < Math.Min(Main.maxTilesY - 10, y0 + 60); y++) {
				if (!WorldGen.InWorld(x, y))
					break;
				Tile tile = Main.tile[x, y];
				if (tile.HasTile && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]))
					return new Vector2(from.X, y * 16f - 4f);
			}
			return from + new Vector2(0, 200f);
		}

		// =================================================================== the Intrusion
		private void Intrusion(Player p)
		{
			int t = (int)Timer;
			SetPose(TwinPose.Cast);
			if (t < 50) {
				Hover(p.Center + new Vector2(Side * 190f, -60f), 16f, 6f);
				if (!Main.dedServ && t % 3 == 0)
					ParticleSystem.Glyph(StaffTip, StaffTip.SafeDirectionTo(p.Center) * 9f, Accent, 1f, 22);
				if (t == 20)
					Lean(80, 0.4f);
			}
			else
				NPC.velocity *= 0.85f;
			if (t == 50) {
				if (!Main.dedServ && p.whoAmI == Main.myPlayer && Main.netMode == NetmodeID.SinglePlayer)
					HackSequence.Begin(this);
			}
			if (t > 52 && !HackSequence.Active)
				Enter(StIdle);
		}

		/// <summary>Draw the tendrils of script reaching from her staff into the player.</summary>
		private void DrawIntrusion(SpriteBatch sb, float op)
		{
			if (State != StHack || Timer < 0 || Timer > 52)
				return;
			Player p = Target;
			Vector2 a = StaffTip - Main.screenPosition, b = p.Center - Main.screenPosition;
			float k = ReUtils.InverseLerp(0, 50, Timer);
			Texture2D th = ReTex.Thread;
			for (int i = 0; i < 3; i++) {
				Vector2 prev = a;
				for (int s = 1; s <= 12; s++) {
					float u = s / 12f * k;
					Vector2 q = Vector2.Lerp(a, b, u) + new Vector2(0, MathF.Sin(u * 9f + Timer * 0.3f + i * 2f) * 18f * (1f - u));
					sb.Draw(th, prev, null, Accent.Additive(0.8f * op), (q - prev).ToRotation(), new Vector2(0, th.Height / 2f), new Vector2((q - prev).Length() / th.Width, 2f / th.Height), SpriteEffects.None, 0f);
					prev = q;
				}
			}
		}

		// =================================================================== the Frenzy
		/// <summary>The sister left behind: grief, then a storm with her sister's ghost singing beside her.</summary>
		private void Frenzy(Player p)
		{
			int t = (int)Timer;
			NPC.dontTakeDamage = false;
			if (t < 0) {
				// grief: she freezes, then breaks
				NPC.velocity *= 0.9f;
				SetPose(TwinPose.Hurt);
				if (!Main.dedServ) {
					glitch = MathF.Max(glitch, 0.3f + 0.4f * ReUtils.InverseLerp(-150, 0, t));
					if (t == -50) {
						AttackReveal.Show(Speaker, this is Requiem ? "FrenzyRequiem" : "FrenzyRefrain", NPC.Center, bigCard: true);
						SoundEngine.PlaySound(TwinSounds.Transform with { Pitch = 0.3f }, NPC.Center);
						ScreenFX.Shake(25f);
						Cinematic.Flash(0.6f);
						for (int i = 0; i < 5; i++)
							ParticleSystem.Ring(NPC.Center, i % 2 == 0 ? new Color(255, 90, 40) : ReUtils.PaleGold, 0.1f + i * 0.08f, 1.08f + i * 0.015f, 50);
						ParticleSystem.GlyphBurst(NPC.Center, 140, 20f, 1.4f);
						Lean(90, 0.5f);
					}
				}
				return;
			}
			frenzyPose();
			const int Span = 330;
			int pattern = (t / Span) % 5;
			int local = t % Span;
			Vector2 ghost = GhostPosition(p);
			float fast = 1f + MathF.Min(1f, t / 3000f) * 0.35f;     // it only gets worse
			// she never stops moving: a tight figure-eight above the player
			float ft = t * 0.03f;
			Hover(p.Center + new Vector2(MathF.Sin(ft) * 260f, -250f + MathF.Sin(ft * 2f) * 70f), 16f, 5f);
			if (local == 0 && !Main.dedServ) {
				SoundEngine.PlaySound(TwinSounds.Cast with { Volume = 0.9f, Pitch = 0.2f }, NPC.Center);
				ParticleSystem.Ring(NPC.Center, Accent, 0.05f, 1.15f, 24);
				ParticleSystem.Ring(ghost, ReUtils.EyeBlue, 0.05f, 1.15f, 24);
				Lean(40, 0.25f);
			}
			switch (pattern) {
				case 0: // Blossom: counter-turning rings of curving petals
					if (local % (int)(9 / fast) == 0) {
						int n = 14;
						float dir = (local / 9) % 2 == 0 ? 1f : -1f;
						for (int i = 0; i < n; i++)
							Orb(NPC.Center, (MathHelper.TwoPi * i / n + local * 0.05f).ToRotationVector2() * 4.2f, (local / 9) % 3 == 0 ? SongOrb.Dark : SongOrb.Gold, SongOrb.Curve, 0.013f * dir);
					}
					break;
				case 1: // Twin spirals: hers and the ghost's, turning against each other
					if (local % 3 == 0) {
						for (int k = 0; k < 2; k++) {
							float a = local * 0.12f + MathHelper.Pi * k;
							Orb(NPC.Center, a.ToRotationVector2() * 5f, SongOrb.Dark, SongOrb.Straight, 1.004f);
							Orb(ghost, (-a).ToRotationVector2() * 5f, SongOrb.Gold, SongOrb.Straight, 1.004f);
						}
					}
					if (local % 60 == 30) {
						Vector2 d = NPC.Center.SafeDirectionTo(p.Center);
						for (int i = -2; i <= 2; i++)
							Orb(NPC.Center, d.RotatedBy(i * 0.14f) * 8f, SongOrb.Gold, SongOrb.Straight, 0f);
					}
					break;
				case 2: // Lament: four rotating lines of verse, rings of dark between them
					if (local == 10 && Server) {
						for (int i = 0; i < 4; i++) {
							Vector2 d = (MathHelper.PiOver2 * i + 0.4f).ToRotationVector2();
							Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, d, ModContent.ProjectileType<VerseChain>(), ReUtils.ProjDamage(125), 0f, Main.myPlayer, 50, 240, 0.0075f * (Side < 0 ? 1f : -1f));
						}
					}
					if (local > 60 && local % 34 == 0) {
						int n = 18;
						for (int i = 0; i < n; i++)
							Orb(NPC.Center, (MathHelper.TwoPi * i / n).ToRotationVector2() * 3.2f, i % 3 == 0 ? SongOrb.Gold : SongOrb.Dark, SongOrb.Straight, 1.01f);
					}
					NPC.velocity *= local > 10 && local < 300 ? 0.6f : 1f;   // she holds still while the lines turn
					break;
				case 3: // Petals and bells
					if (local % 9 == 0)
						Orb(p.Center + new Vector2(Main.rand.NextFloat(-900f, 900f), Main.rand.NextFloat(-560f, -380f)), new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(4f, 6f)), SongOrb.Petal, SongOrb.Homing, 0f, ReUtils.ProjDamage(85));
					if (local % 75 == 20 && Server)
						Projectile.NewProjectile(NPC.GetSource_FromAI(), p.Center + Main.rand.NextVector2CircularEdge(1f, 1f) * 280f, Vector2.Zero, ModContent.ProjectileType<BellSigil>(), OrbDamage, 0f, Main.myPlayer, 50, 20);
					break;
				case 4: // Waltz: she and the ghost cross through the player, scattering notes
					if (local % 55 == 0) {
						aim = NPC.Center.SafeDirectionTo(p.Center);
						NPC.velocity = aim * 30f;
						if (!Main.dedServ)
							SoundEngine.PlaySound(TwinSounds.Dash, NPC.Center);
					}
					if (local % 55 < 18) {
						SetPose(TwinPose.Dash);
						if (local % 3 == 0) {
							Orb(NPC.Center, aim.RotatedBy(MathHelper.PiOver2) * 2f, SongOrb.Gold, SongOrb.Delayed, 40);
							Orb(ghost, -aim.RotatedBy(MathHelper.PiOver2) * 2f, SongOrb.Dark, SongOrb.Delayed, 40);
						}
					}
					break;
			}
			if (!Main.dedServ && t % 2 == 0)
				ParticleSystem.Glyph(ghost + Main.rand.NextVector2Circular(20, 50), new Vector2(0, -1.5f), ReUtils.EyeBlue * 0.8f, 0.8f, 30);
		}

		private void frenzyPose()
		{
			// the animation itself goes wild: poses snap between casting and singing
			int phase = (int)(visualTime / 14) % 3;
			SetPose(phase == 0 ? TwinPose.Cast : phase == 1 ? TwinPose.Sing : TwinPose.Cast);
		}

		/// <summary>Where the dead sister's ghost stands: mirrored across the player.</summary>
		private Vector2 GhostPosition(Player p) => new(p.Center.X * 2f - NPC.Center.X, NPC.Center.Y + MathF.Sin(FightTicks * 0.05f) * 30f);

		// =================================================================== drawing
		private void DrawSpecialOverlay(SpriteBatch sb, Vector2 pos, float op)
		{
			float time = Main.GlobalTimeWrappedHourly;
			shellAlpha = MathHelper.Lerp(shellAlpha, Shielded ? 1f : 0f, 0.12f);
			frenzyAlpha = MathHelper.Lerp(frenzyAlpha, State == StFrenzy && Timer >= 0 ? 1f : 0f, 0.05f);
			DrawIntrusion(sb, op);

			// the Aria's shell: a gyroscope of rings and a turning sigil (they are untouchable inside it)
			if (shellAlpha > 0.02f) {
				Texture2D ring = ReTex.Ring;
				float a = shellAlpha * op;
				for (int i = 0; i < 3; i++) {
					float rot = time * (0.8f + i * 0.35f) * (i % 2 == 0 ? 1f : -1f) + i;
					float squash = 0.35f + 0.25f * MathF.Sin(time * 1.3f + i * 2f);
					float s = 190f / (ring.Width * 0.9f);
					sb.Draw(ring, pos, null, Accent.Additive(0.55f * a), rot, ring.Size() / 2f, new Vector2(s, s * squash), SpriteEffects.None, 0f);
				}
				Texture2D sig = ReTex.SigilOuter;
				sb.Draw(sig, pos, null, Accent.Additive(0.35f * a), -time * 0.4f, sig.Size() / 2f, 220f / sig.Width, SpriteEffects.None, 0f);
				// dashed outer circle with drifting triangles
				for (int k = 0; k < 10; k++) {
					float ang = time * 0.6f + MathHelper.TwoPi * k / 10f;
					Vector2 q = pos + ang.ToRotationVector2() * 120f;
					sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(k * 4), ReUtils.PaleGold.Additive(0.8f * a), ang + MathHelper.PiOver2, new Vector2(7, 9), 1f, SpriteEffects.None, 0f);
				}
				FX.Bloom(sb, pos, Accent, 120f, 0.18f * a);
			}

			// the Frenzy: wings of streaming glyphs, and the ghost of the sister she lost
			if (frenzyAlpha > 0.02f) {
				float a = frenzyAlpha * op;
				for (int side = -1; side <= 1; side += 2) {
					for (int f = 0; f < 6; f++) {
						float baseAng = -MathHelper.PiOver2 + side * (0.5f + f * 0.22f) + MathF.Sin(time * 3f + f) * 0.06f;
						for (int g = 0; g < 9; g++) {
							float d = 20f + g * 16f + (time * 60f % 16f);
							Vector2 q = pos + new Vector2(0, -30f) + baseAng.ToRotationVector2() * d;
							float fade = 1f - g / 9f;
							sb.Draw(ReTex.GlyphAtlasGlow, q, ReTex.GlyphFrame(f * 9 + g + (int)(time * 8f)), Color.Lerp(new Color(255, 90, 40), ReUtils.PaleGold, g / 9f).Additive(0.7f * a * fade), baseAng + MathHelper.PiOver2, new Vector2(7, 9), 1f - g * 0.05f, SpriteEffects.None, 0f);
						}
					}
				}
				Player p = Target;
				Vector2 gp = GhostPosition(p) - Main.screenPosition;
				Texture2D ghostSheet = this is Requiem ? ReTex.RefrainSheet : ReTex.RequiemSheet;
				Rectangle src = new(0, ((int)(visualTime / 9) % 4 + 18) * FrameH, FrameW, FrameH);   // she sings
				float flick = ReUtils.Hash01((int)(visualTime / 4), NPC.whoAmI) > 0.85f ? 0.3f : 1f;
				for (int i = 0; i < 3; i++) {
					Vector2 off = new Vector2(MathF.Sin(time * 7f + i * 2f) * 5f, 0f);
					Color c = i == 0 ? ReUtils.EyeBlue : i == 1 ? new Color(255, 80, 40) : Color.White;
					sb.Draw(ghostSheet, gp + off, src, c.Additive(0.32f * a * flick), 0f, Origin, NPC.scale, gp.X < pos.X ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0f);
				}
				FX.Bloom(sb, gp, ReUtils.EyeBlue, 90f, 0.2f * a);
			}
		}
	}
}
