import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { VideoResponse } from './video.models';
import { VideoService } from './video.service';

describe('VideoService', () => {
  let service: VideoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(VideoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the caller videos', () => {
    let result: VideoResponse[] | undefined;
    service.list().subscribe((videos) => (result = videos));

    http.expectOne('/videos').flush([{ id: '1', title: 'a', status: 'Uploaded', createdAt: '' }]);

    expect(result?.length).toBe(1);
  });

  it('reserves, uploads straight to storage, then completes', () => {
    const file = new File([new Uint8Array(8)], 'clip.mp4', { type: 'video/mp4' });
    let lastPercent = 0;
    let completed = false;

    service.upload(file).subscribe({ next: (p) => (lastPercent = p), complete: () => (completed = true) });

    const initiate = http.expectOne('/videos');
    expect(initiate.request.method).toBe('POST');
    initiate.flush({ videoId: 'v1', uploadUrl: 'http://storage.local/bucket', fields: { key: 'uploads/v1' } });

    const storage = http.expectOne('http://storage.local/bucket');
    expect(storage.request.body instanceof FormData).toBe(true);
    storage.flush(null, { status: 204, statusText: 'No Content' });

    http.expectOne('/videos/v1/complete').flush({ id: 'v1', title: 'clip.mp4', status: 'Uploaded', createdAt: '' });

    expect(completed).toBe(true);
    expect(lastPercent).toBe(100);
  });
});
