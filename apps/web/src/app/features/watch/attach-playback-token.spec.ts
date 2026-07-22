import { attachPlaybackToken } from './attach-playback-token';

describe('attachPlaybackToken', () => {
  const origin = 'http://localhost:4200';

  function fakeXhr(): { headers: Record<string, string>; xhr: XMLHttpRequest } {
    const headers: Record<string, string> = {};
    const xhr = {
      setRequestHeader: (name: string, value: string) => {
        headers[name] = value;
      },
    } as unknown as XMLHttpRequest;
    return { headers, xhr };
  }

  it('attaches the token to a relative API url', () => {
    const { headers, xhr } = fakeXhr();

    attachPlaybackToken(xhr, '/videos/abc/hls/master.m3u8', 'tok', origin);

    expect(headers['Authorization']).toBe('Bearer tok');
  });

  it('attaches the token to an absolute same-origin url', () => {
    const { headers, xhr } = fakeXhr();

    attachPlaybackToken(xhr, `${origin}/videos/abc/hls/v0_seg000.ts`, 'tok', origin);

    expect(headers['Authorization']).toBe('Bearer tok');
  });

  it('never sends the token to the storage host', () => {
    const { headers, xhr } = fakeXhr();

    attachPlaybackToken(
      xhr,
      'http://localhost:9000/tessera/videos/abc/hls/v0_seg000.ts?X-Amz-Signature=x',
      'tok',
      origin,
    );

    expect(headers).toEqual({});
  });

  it('does nothing when there is no token', () => {
    const { headers, xhr } = fakeXhr();

    attachPlaybackToken(xhr, '/videos/abc/hls/master.m3u8', null, origin);

    expect(headers).toEqual({});
  });
});
