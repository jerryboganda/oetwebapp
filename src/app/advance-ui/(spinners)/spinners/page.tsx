import React from "react";
import SpinnersPage from "@/app/advance-ui/(spinners)/_components/SpinnersPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Spinners - PolytronX",
    description:
      "Explore advanced spinner components and loading indicators for your web applications.",
    keywords: [
      "spinners",
      "loading",
      "react spinners",
      "loading indicators",
      "spinner component",
      "loading animation",
      "spinner customization",
      "spinner styles",
      "spinner effects",
      "spinner integration",
      "spinner library",
      "react loading",
      "spinner animation",
      "loading optimization",
      "spinner performance",
    ],
    openGraph: {
      title: "Spinners - PolytronX",
      description:
        "Explore advanced spinner components and loading indicators for your web applications.",
      url: "/advance-ui/spinners",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SpinnersPage />
    </div>
  );
};

export default Page;
