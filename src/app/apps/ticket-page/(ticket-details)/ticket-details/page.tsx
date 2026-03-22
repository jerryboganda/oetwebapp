import React from "react";
import TicketDetails from "@/app/apps/ticket-page/(ticket-details)/_components/TicketDetails";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Ticket Details - PolytronX",
    description:
      "Explore advanced ticket details and support management features for your web applications.",
    keywords: [
      "ticket details",
      "ticket management",
      "support",
      "react ticket",
      "UI ticket",
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
      title: "Ticket Details - PolytronX",
      description:
        "Explore advanced ticket details and support management features for your web applications.",
      url: "/apps/tickets/details",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TicketDetails />
    </div>
  );
};

export default Page;
