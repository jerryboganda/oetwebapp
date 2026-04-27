import nextConfig from './next.config';

describe('nextConfig redirects', () => {
  it('permanently redirects legacy admin content routes into the Content Hub', async () => {
    const redirects = await nextConfig.redirects?.();
    expect(redirects).toBeDefined();

    const bySource = new Map(redirects?.map((redirect) => [redirect.source, redirect]));
    const moves = [
      ['/admin/content-papers', '/admin/content/papers'],
      ['/admin/content-hierarchy', '/admin/content/hierarchy'],
      ['/admin/content-import', '/admin/content/import'],
      ['/admin/content-generation', '/admin/content/generation'],
      ['/admin/content-analytics', '/admin/content/analytics'],
      ['/admin/content-quality', '/admin/content/quality'],
      ['/admin/grammar', '/admin/content/grammar'],
      ['/admin/pronunciation', '/admin/content/pronunciation'],
      ['/admin/strategies', '/admin/content/strategies'],
      ['/admin/media', '/admin/content/media'],
      ['/admin/dedup', '/admin/content/dedup'],
      ['/admin/publish-requests', '/admin/content/publish-requests'],
    ] as const;

    for (const [source, destination] of moves) {
      expect(bySource.get(source)).toMatchObject({ destination, permanent: true });
      expect(bySource.get(`${source}/:path*`)).toMatchObject({ destination: `${destination}/:path*`, permanent: true });
    }
  });
});