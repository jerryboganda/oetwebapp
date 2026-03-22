export async function GET() {
  const res = await fetch("https://api.ipify.org?format=json", {
    cache: "force-cache",
  });
  if (!res.ok) {
    return new Response("Failed to fetch IP", { status: 500 });
  }
  const data = await res.json();
  return new Response(JSON.stringify(data), {
    headers: {
      "Cache-Control": "public, max-age=3600, stale-while-revalidate=30",
    },
  });
}
