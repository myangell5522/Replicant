using Replicant.Common.Dimension;
using Replicant.Content.NPCs.Twins;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Replicant.Common.Cutscene
{
	/// <summary>
	/// The fight is choreographed to the song (the reveal lands at 2:00, cutscenes are cut to the beat). When the
	/// game is paused the world stops — so the song must stop with it, or it would run ahead of the fight.
	/// While paused the current track is paused in place (not restarted) and the audio update is held, so vanilla
	/// doesn't "helpfully" restart it; unpausing resumes it from the same sample.
	/// </summary>
	public class MusicPause : ModSystem
	{
		private static IAudioTrack held;

		private static bool Choreographed =>
			DimensionBridge.InDimension || DimensionBridge.IsTraveling || RiddleCutscene.Active || TwinsFight.AnyTwinAlive || EpilogueCutscene.Active
			|| DevourCutscene.Active || NPC.AnyNPCs(ModContent.NPCType<Content.NPCs.Recursion.RecursionHead>());

		public override void Load() => On_Main.UpdateAudio += UpdateAudio;

		public override void Unload() => Release();

		private static void UpdateAudio(On_Main.orig_UpdateAudio orig, Main self)
		{
			bool hold = !Main.gameMenu && Main.gamePaused && Main.netMode == Terraria.ID.NetmodeID.SinglePlayer && Choreographed;
			if (hold) {
				if (held == null && Main.audioSystem is LegacyAudioSystem las && Main.curMusic > 0 && Main.curMusic < las.AudioTracks.Length) {
					IAudioTrack track = las.AudioTracks[Main.curMusic];
					if (track != null && track.IsPlaying) {
						track.Pause();
						held = track;
					}
				}
				if (held != null)
					return;
			}
			else
				Release();
			orig(self);
		}

		private static void Release()
		{
			if (held == null)
				return;
			try {
				if (held.IsPaused)
					held.Resume();
			}
			catch {
				// the track may have been disposed while unloading
			}
			held = null;
		}
	}
}
