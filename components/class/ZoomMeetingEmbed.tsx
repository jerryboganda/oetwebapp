"use client";
import { useEffect, useRef } from "react";
import { safeZoomUrl } from "@/lib/zoom-url";

// Type-only import to avoid SSR issues with the Zoom SDK
type ZoomClient = {
  init: (opts: Record<string, unknown>) => Promise<void>;
  join: (opts: Record<string, unknown>) => Promise<void>;
  leaveMeeting?: () => void;
};

type JoinTokenResponse = {
  sdkKey?: string | null;
  signature?: string | null;
  meetingNumber: string;
  userName: string;
  userEmail?: string | null;
  passWord?: string | null;
  role: number;
  zak?: string | null;
  joinUrl?: string | null;
};

type Props = {
  joinToken: JoinTokenResponse;
  onLeave?: () => void;
};

export function ZoomMeetingEmbed({ joinToken, onLeave }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const clientRef = useRef<ZoomClient | null>(null);
  const fallbackUrl = safeZoomUrl(joinToken.joinUrl);

  useEffect(() => {
    if (!containerRef.current) return;
    let mounted = true;

    const initZoom = async () => {
      try {
        // Dynamic import avoids SSR issues with Zoom SDK
        const { ZoomMtgEmbedded } = await import("@zoom/meetingsdk/embedded");
        if (!mounted || !containerRef.current) return;

        const client = ZoomMtgEmbedded.createClient() as unknown as ZoomClient;
        clientRef.current = client;

        await client.init({
          zoomAppRoot: containerRef.current,
          language: "en-US",
          customize: {
            meetingInfo: ["topic", "host", "mn", "pwd", "participant"],
            toolbar: {
              buttons: [
                {
                  text: "Leave",
                  className: "leave-btn",
                  onClick: () => {
                    onLeave?.();
                  },
                },
              ],
            },
            video: {
              isResizable: true,
              viewSizes: { default: { width: 1000, height: 600 } },
            },
          },
        });

        if (!mounted) return;

        await client.join({
          signature: joinToken.signature ?? "",
          sdkKey: joinToken.sdkKey ?? "",
          meetingNumber: joinToken.meetingNumber,
          password: joinToken.passWord ?? "",
          userName: joinToken.userName,
          userEmail: joinToken.userEmail ?? "",
          zak: joinToken.zak ?? undefined,
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : "unknown error";
        console.error("[ZoomMeetingEmbed] Failed to initialize or join Zoom meeting", { message });
      }
    };

    initZoom();

    return () => {
      mounted = false;
      try {
        clientRef.current?.leaveMeeting?.();
      } catch {
        // ignore cleanup errors
      }
      clientRef.current = null;
    };
  }, [joinToken, onLeave]);

  return (
    <div
      className="relative w-full h-[80vh] rounded-2xl overflow-hidden bg-gray-900"
    >
      <div ref={containerRef} className="h-full w-full" />
      {fallbackUrl ? (
        <a
          href={fallbackUrl}
          target="_blank"
          rel="noreferrer"
          className="absolute bottom-4 right-4 rounded-lg bg-white px-4 py-2 text-sm font-semibold text-gray-950 shadow-lg hover:bg-gray-100"
        >
          Open in Zoom
        </a>
      ) : null}
    </div>
  );
}
