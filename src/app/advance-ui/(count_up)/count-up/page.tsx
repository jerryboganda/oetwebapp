import React from "react";
import CountUpPage from "@/app/advance-ui/(count_up)/_components/CountUpPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Count Up - PolytronX",
    description:
      "Explore advanced count-up animations and number counters for your web applications.",
    keywords: [
      "count up",
      "count up animation",
      "number counter",
      "react counter",
      "count animation",
      "number animation",
      "statistics counter",
      "achievement counter",
      "progress counter",
      "dynamic counter",
      "counter effects",
      "react count",
      "counter customization",
      "counter styles",
      "count up library",
    ],
    openGraph: {
      title: "Count Up - PolytronX",
      description:
        "Explore advanced count-up animations and number counters for your web applications.",
      url: "/advance-ui/count-up",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CountUpPage />
    </div>
  );
};

export default Page;
