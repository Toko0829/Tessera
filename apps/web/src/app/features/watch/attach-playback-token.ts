// hls.js performs its own XHRs, so the Angular auth interceptor never sees playback
// requests. This applies the same rule the interceptor enforces: the bearer token
// goes only to our own origin, never to the storage host that segment redirects
// land on (and browsers strip the header on the cross-origin redirect hop anyway).
export function attachPlaybackToken(
  xhr: XMLHttpRequest,
  url: string,
  token: string | null,
  origin: string,
): void {
  if (token === null) {
    return;
  }

  if (new URL(url, origin).origin === origin) {
    xhr.setRequestHeader('Authorization', `Bearer ${token}`);
  }
}
