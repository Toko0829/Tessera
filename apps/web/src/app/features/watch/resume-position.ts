// How close to the end a saved position may be before resuming stops making
// sense: within this window the viewer effectively finished the video.
const nearEndSeconds = 10;

// Where playback should start given the saved position. Starts over when there is
// no save or the save is inside the final stretch (resuming into the credits helps
// no one; watching again from the top is what the viewer wants there).
export function resumePosition(
  positionSeconds: number | null,
  durationSeconds: number,
): number {
  if (positionSeconds === null || positionSeconds <= 0) {
    return 0;
  }

  if (positionSeconds >= durationSeconds - nearEndSeconds) {
    return 0;
  }

  return positionSeconds;
}
