import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MeResponse } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { VideoResponse } from '../../core/video/video.models';
import { VideoService } from '../../core/video/video.service';

@Component({
  selector: 'app-home',
  imports: [DatePipe, RouterLink],
  templateUrl: './home.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home {
  private readonly auth = inject(AuthService);
  private readonly videos = inject(VideoService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly user = signal<MeResponse | null>(null);
  protected readonly library = signal<VideoResponse[]>([]);
  protected readonly uploading = signal(false);
  protected readonly progress = signal(0);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.auth.me().subscribe({
      next: (me) => this.user.set(me),
      error: () => this.router.navigateByUrl('/login'),
    });
    this.refresh();
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Allow picking the same file again after this run.
    input.value = '';
    if (file) {
      this.upload(file);
    }
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => this.router.navigateByUrl('/login'),
      error: () => this.router.navigateByUrl('/login'),
    });
  }

  protected statusLabel(status: string): string {
    switch (status) {
      case 'PendingUpload':
        return 'Pending';
      case 'Uploaded':
        return 'Queued';
      default:
        return status;
    }
  }

  protected statusClass(status: string): string {
    switch (status) {
      case 'Ready':
        return 'text-accent-hi';
      case 'Rejected':
      case 'Failed':
        return 'text-live';
      default:
        return 'text-premium';
    }
  }

  private upload(file: File): void {
    this.uploading.set(true);
    this.progress.set(0);
    this.error.set(null);

    this.videos
      .upload(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (percent) => this.progress.set(percent),
        error: () => {
          this.uploading.set(false);
          this.error.set('Upload failed. Check the file is a supported video and try again.');
        },
        complete: () => {
          this.uploading.set(false);
          this.refresh();
        },
      });
  }

  private refresh(): void {
    this.videos.list().subscribe({ next: (videos) => this.library.set(videos) });
  }
}
