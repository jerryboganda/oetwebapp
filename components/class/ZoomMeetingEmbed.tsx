"use client";
import { useEffect, useRef } from "react";

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
};

type Props = {
  joinToken: JoinTokenResponse;
  onLeave?: () => void;
};

export function ZoomMeetingEmbed({ joinToken, onLeave }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const clientRef = useRef<ZoomClient | null>(null);

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
        console.error("[ZoomMeetingEmbed] Failed to initialize or join:", err);
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
      ref={containerRef}
      className="w-full h-[80vh] rounded-2xl overflow-hidden bg-gray-900"
    />
  );
}
