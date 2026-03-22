export async function POST(request: Request) {
  try {
    const { username } = await request.json();
    if (!username) {
      return new Response("Username is required", { status: 400 });
    }
    const response = await fetch(`https://api.github.com/users/${username}`);
    if (!response.ok) {
      return new Response(`GitHub error: ${response.statusText}`, {
        status: response.status,
      });
    }
    const data = await response.json();
    return new Response(JSON.stringify(data), {
      headers: {
        "Content-Type": "application/json",
        "Cache-Control": "public, max-age=3600, stale-while-revalidate=30",
      },
    });
  } catch (error) {
    return new Response("Internal server error", { status: 500 });
  }
}
