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
}
