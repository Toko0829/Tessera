import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import Hls from 'hls.js';
import { fromEvent } from 'rxjs';
import { take, throttleTime } from 'rxjs/operators';
import { AuthService } from '../../core/auth/auth.service';
import { VideoResponse } from '../../core/video/video.models';
import { VideoService } from '../../core/video/video.service';
import { attachPlaybackToken } from './attach-playback-token';
import { resumePosition } from './resume-position';

// The player page. State (the video, error and support flags) is signals; the API
// call is an RxJS stream (charter section 3). hls.js drives MSE playback of the
// API-served HLS ladder; the native <video> controls keep the player fully
// keyboard operable.
@Component({
  selector: 'app-watch',
  imports: [RouterLink],
  templateUrl: './watch.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Watch {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly videos = inject(VideoService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly player = viewChild<ElementRef<HTMLVideoElement>>('player');
  private hls: Hls | null = null;

  protected readonly video = signal<VideoResponse | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly playbackError = signal<string | null>(null);
  protected readonly unsupported = signal(false);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id')!;

    this.videos
      .get(id)
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (video) => this.video.set(video),
        error: () => this.loadError.set('Could not load this video.'),
      });

    // Starts playback once the video is known Ready and its element is in the DOM.
    effect(() => {
      const element = this.player()?.nativeElement;
      if (
        element &&
        this.video()?.status === 'Ready' &&
        this.playbackError() === null &&
        this.hls === null
      ) {
        this.startPlayback(element, id);
      }
    });

    this.destroyRef.onDestroy(() => {
      // Leaving the page is the last chance to record where the viewer stopped.
      const element = this.player()?.nativeElement;
      if (element && this.hls !== null && element.currentTime > 0) {
        this.saveProgress(id, element.currentTime);
      }
      this.hls?.destroy();
    });
  }

  private startPlayback(element: HTMLVideoElement, id: string): void {
    if (!Hls.isSupported()) {
      // Native HLS (iOS Safari) cannot attach the Authorization header the API
      // requires, so those browsers are declined honestly instead of failing with
      // a broken player. They are served once CDN-signed delivery lands.
      this.unsupported.set(true);
      return;
    }

    this.hls = new Hls({
      xhrSetup: (xhr, url) =>
        attachPlaybackToken(xhr, url, this.auth.token(), window.location.origin),
    });
    this.hls.on(Hls.Events.ERROR, (_event, data) => {
      if (data.fatal) {
        this.playbackError.set('Playback failed. Reload the page to try again.');
        this.hls?.destroy();
        this.hls = null;
      }
    });
    this.hls.loadSource(`/videos/${id}/hls/master.m3u8`);
    this.hls.attachMedia(element);

    // Resume from the saved position once the real duration is known.
    fromEvent(element, 'loadedmetadata')
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const start = resumePosition(this.video()?.positionSeconds ?? null, element.duration);
        if (start > 0) {
          element.currentTime = start;
        }
      });

    // Record the position every few seconds while playing, and on every pause.
    // A lost tab still costs at most one cadence interval of progress.
    fromEvent(element, 'timeupdate')
      .pipe(throttleTime(5000), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.saveProgress(id, element.currentTime));
    fromEvent(element, 'pause')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.saveProgress(id, element.currentTime));
  }

  private saveProgress(id: string, positionSeconds: number): void {
    this.videos.saveProgress(id, positionSeconds).subscribe({
      // A failed save is recovered by the next cadence tick, so playback is never
      // interrupted over it; the position is convenience state, not user data loss.
      error: () => undefined,
    });
  }
}
