import React from "react";
import CountDownPage from "@/app/advance-ui/(count_down)/_components/CountDownPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Count Down - PolytronX",
    description:
      "Explore count down components for event timers and countdowns in your React application.",
    keywords: [
      "count down",
      "react components",
      "UI components",
      "time counter",
      "date countdown",
      "event timer",
      "countdown animation",
      "react countdown",
      "timer customization",
      "timer styles",
      "countdown effects",
      "timer integration",
      "countdown library",
    ],
    openGraph: {
      title: "Count Down - PolytronX",
      description:
        "Explore advanced countdown timers and timers for your web applications.",
      url: "/advance-ui/count-down",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CountDownPage />
    </div>
  );
};

export default Page;
