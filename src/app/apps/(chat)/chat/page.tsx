import React from "react";
import ChatPage from "@/app/apps/(chat)/_components/ChatPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Chat - PolytronX",
    description:
      "Real-time messaging and communication with team members and clients.",
    keywords: [
      "chat",
      "messaging",
      "real-time chat",
      "team communication",
      "instant messaging",
      "group chat",
      "private chat",
      "chat interface",
      "chat history",
      "chat notifications",
      "chat search",
      "chat attachments",
      "chat integration",
    ],
    openGraph: {
      title: "Chat - PolytronX",
      description:
        "Real-time messaging and communication with team members and clients.",
      url: "/apps/chat",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ChatPage />
    </div>
  );
};

export default Page;
