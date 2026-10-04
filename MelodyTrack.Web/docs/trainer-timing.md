# Shared exercise timing

`TrainerTimingHost` owns one lazy Web Audio clock and one scheduler at page or
document scope. Nested hosts reuse the existing authority. The staff practice
page, portal learning pages, and course workspace provide page hosts;
structured documents provide a host when rendered independently.

Trainers consume the public `useTrainerTiming(onRelease)` contract. They publish
an `ExerciseTimingConfiguration` when starting: BPM, beats per bar, quarter-note
length of a beat, slots in each bar, repetitions, loop, metronome, and progression.
The host publishes `ExerciseTimingPosition` for rendering. Trainer actions do
not determine time; a rest retains its slot.

Starting another exercise cancels the previous scheduler and its queued audio
before handing over ownership. `onRelease` lets the previous trainer finish its
recording and cancel a pending start. A released owner's late async completion
cannot stop the new owner's clock. Unmount releases ownership; unmounting the
host closes the audio context. The clock resumes directly from the user's start
gesture. Lookahead timers schedule audio against Web Audio time; animation
frames present already-due positions, without determining musical tempo.

The cursor supplies count-in, accented first beats, subdivisions, and automatic
progression. BPM changes at completed bar or repetition boundaries and is capped
at 240. The finite-duration calculation uses the same progression function.
Metronome gain feeds both the speakers and the recording destination.

On standalone practice pages, manual playback tempo disables exercise following
and automatic progression without rewriting the exercise configuration. The UI
explicitly identifies that mode. Course document readers have no configuration
or manual-tempo controls: playback uses the teacher's settings. Teachers can
configure exercises in the document editor.

Recordings use the same live timing host. Accelerated offline video export is
not part of this timing implementation. Narrow viewports below 768 CSS pixels
show a placeholder without mounting the trainer runtime.

Regression tests cover count-in, beat accents, subdivisions, progression
boundaries, finite and looping exercises, invalid timing inputs, audio routing,
manual override indication, and ownership transfer. Automated verification
requires the authorization described in the root AGENTS.md.
