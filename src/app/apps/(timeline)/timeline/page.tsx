import React from "react";
import TimelinePage from "@/app/apps/(timeline)/_components/TimelinePage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Timeline - PolytronX",
    description:
      "Track your activities and progress with our interactive timeline visualization.",
    keywords: [
      "timeline",
      "activity timeline",
      "progress tracking",
      "event timeline",
      "timeline view",
      "activity log",
      "timeline visualization",
      "event tracking",
      "timeline management",
      "activity history",
      "timeline filtering",
      "event categorization",
      "timeline search",
      "activity reporting",
      "timeline analytics",
    ],
    openGraph: {
      title: "Timeline - PolytronX",
      description:
        "Track your activities and progress with our interactive timeline visualization.",
      url: "/apps/timeline",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TimelinePage />
    </div>
  );
};

export default Page;
