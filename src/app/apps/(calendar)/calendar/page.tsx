import React from "react";
import CalendarPage from "@/app/apps/(calendar)/_components/CalendarPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Calendar - PolytronX",
    description:
      "Explore advanced calendar and scheduling features for your web applications.",
    keywords: [
      "calendar",
      "scheduling",
      "react calendar",
      "UI calendar",
      "calendar component",
      "event management",
      "calendar customization",
      "calendar styles",
      "calendar effects",
      "calendar integration",
      "calendar library",
      "react calendar app",
      "calendar optimization",
      "calendar performance",
      "scheduling system",
      "calendar view",
      "time management",
      "daily planner",
      "monthly calendar",
      "weekly view",
      "calendar sharing",
      "calendar sync",
    ],
    openGraph: {
      title: "Calendar - PolytronX",
      description:
        "Manage your schedule with our interactive calendar featuring events, appointments, and reminders.",
      url: "/apps/calendar",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return <CalendarPage />;
};

export default Page;
