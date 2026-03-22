import React from "react";
import TicketPage from "@/app/apps/ticket-page/(ticket)/_components/TicketPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Tickets - PolytronX",
    description:
      "Explore advanced ticket management and support features for your web applications.",
    keywords: [
      "tickets",
      "ticket management",
      "support",
      "react tickets",
      "UI tickets",
      "ticket component",
      "ticket tracking",
      "ticket customization",
      "ticket styles",
      "ticket effects",
      "ticket integration",
      "ticket library",
      "react ticket app",
      "ticket optimization",
      "ticket performance",
      "support system",
    ],
    openGraph: {
      title: "Tickets - PolytronX",
      description:
        "Explore advanced ticket management and support features for your web applications.",
      url: "/apps/tickets",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TicketPage />
    </div>
  );
};

export default Page;
