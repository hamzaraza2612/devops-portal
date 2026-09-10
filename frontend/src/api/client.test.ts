import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, SESSION_EXPIRED_EVENT, api, describeError, getToken, setToken } from './client';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('api client', () => {
  beforeEach(() => {
    setToken(null);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('attaches the stored bearer token to requests', async () => {
    setToken('test-token');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/whatever');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer test-token');
  });

  it('surfaces the backend error message on a non-2xx response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(400, { error: 'CommitSha must be a valid hex string.' })));

    await expect(api.get('/deployments')).rejects.toMatchObject({
      message: 'CommitSha must be a valid hex string.',
      status: 400,
    });
  });

  it('falls back to a generic message when the error body has no `error` field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })));

    await expect(api.get('/deployments')).rejects.toMatchObject({ status: 500 });
  });

  it('wraps a network failure (fetch throwing) as an ApiError instead of letting it propagate raw', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await api.get('/deployments').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(describeError(error)).toContain('Could not reach the server');
  });

  it('dispatches a session-expired event and rejects on a 401', async () => {
    setToken('expired-token');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })));
    const handler = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, handler);

    await expect(api.get('/deployments')).rejects.toBeInstanceOf(ApiError);
    expect(handler).toHaveBeenCalledTimes(1);

    window.removeEventListener(SESSION_EXPIRED_EVENT, handler);
  });

  it('treats 204 No Content as success with no body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(api.post('/promotions/1/approve')).resolves.toBeUndefined();
  });

  it('round-trips the token through storage', () => {
    expect(getToken()).toBeNull();
    setToken('abc');
    expect(getToken()).toBe('abc');
    setToken(null);
    expect(getToken()).toBeNull();
  });
});

describe('describeError', () => {
  it('returns a plain-language message for a generic Error', () => {
    expect(describeError(new Error('boom'))).toBe('boom');
  });

  it('never returns a stack trace or [object Object] for an unknown thrown value', () => {
    expect(describeError({ some: 'object' })).toBe('Something went wrong. Please try again.');
  });
});
