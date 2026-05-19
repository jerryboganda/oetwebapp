export async function GET(_request: Request, context: { params: Promise<{ asset: string }> }) {
  await context.params;
  return new Response('Not found', { status: 404, headers: { 'Cache-Control': 'no-store' } });
}
