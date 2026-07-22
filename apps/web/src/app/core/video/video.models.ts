export interface InitiateUploadResponse {
  readonly videoId: string;
  readonly uploadUrl: string;
  readonly fields: Record<string, string>;
}

export interface VideoResponse {
  readonly id: string;
  readonly title: string;
  readonly status: string;
  readonly createdAt: string;
  // Null until the worker has measured the video.
  readonly durationSeconds: number | null;
  // Null until the caller has watched some of it.
  readonly positionSeconds: number | null;
}
