import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, of } from 'rxjs';
import { concatMap, map, switchMap } from 'rxjs/operators';
import { InitiateUploadResponse, VideoResponse } from './video.models';

@Injectable({ providedIn: 'root' })
export class VideoService {
  private readonly http = inject(HttpClient);

  list(): Observable<VideoResponse[]> {
    return this.http.get<VideoResponse[]>('/videos');
  }

  // Runs the whole flow: reserve an upload, send the file straight to storage, then
  // tell the API it finished. Emits upload progress as a percentage (0-100) and a
  // final 100 once the video is confirmed.
  upload(file: File): Observable<number> {
    return this.initiate(file).pipe(
      switchMap((reservation) =>
        this.http
          .post(reservation.uploadUrl, this.buildForm(reservation, file), {
            reportProgress: true,
            observe: 'events',
          })
          .pipe(
            concatMap((event) => {
              if (event.type === HttpEventType.UploadProgress) {
                return of(event.total ? Math.round((100 * event.loaded) / event.total) : 0);
              }
              if (event.type === HttpEventType.Response) {
                return this.complete(reservation.videoId).pipe(map(() => 100));
              }
              return EMPTY;
            }),
          ),
      ),
    );
  }

  private initiate(file: File): Observable<InitiateUploadResponse> {
    return this.http.post<InitiateUploadResponse>('/videos', {
      title: file.name,
      fileName: file.name,
      contentType: file.type || 'video/mp4',
      sizeBytes: file.size,
    });
  }

  private complete(videoId: string): Observable<VideoResponse> {
    return this.http.post<VideoResponse>(`/videos/${videoId}/complete`, {});
  }

  private buildForm(reservation: InitiateUploadResponse, file: File): FormData {
    const form = new FormData();
    for (const [name, value] of Object.entries(reservation.fields)) {
      form.append(name, value);
    }
    form.append('file', file);
    return form;
  }
}
