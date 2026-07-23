import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Watch } from './watch';

// jsdom has no Media Source Extensions, so Hls.isSupported() is false here. That
// makes the unsupported-browser branch the honest testable path for a Ready video;
// real MSE playback is exercised against the running stack, not simulated.
describe('Watch', () => {
  let fixture: ComponentFixture<Watch>;
  let http: HttpTestingController;

  const video = {
    id: 'abc',
    title: 'My clip',
    status: 'Ready',
    createdAt: '2026-07-23T10:00:00Z',
    durationSeconds: 120,
    positionSeconds: 42.5,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Watch],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'abc' }) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Watch);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('loads the video by the route id and shows its title', async () => {
    http.expectOne('/videos/abc').flush(video);
    await fixture.whenStable();
    fixture.detectChanges();

    const heading = (fixture.nativeElement as HTMLElement).querySelector('h1');
    expect(heading?.textContent).toContain('My clip');
  });

  it('tells a browser without Media Source Extensions that it cannot play', async () => {
    http.expectOne('/videos/abc').flush(video);
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role=alert]');
    expect(alert?.textContent).toContain('cannot play');
  });

  it('shows a waiting message for a video that is not ready', async () => {
    http.expectOne('/videos/abc').flush({ ...video, status: 'Processing' });
    await fixture.whenStable();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('not ready to play');
    expect(root.querySelector('video')).toBeNull();
  });

  it('shows an error when the video cannot be loaded', async () => {
    http.expectOne('/videos/abc').flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role=alert]');
    expect(alert?.textContent).toContain('Could not load');
  });
});
