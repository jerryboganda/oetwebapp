import React from "react";
import PrismjsPage from "@/app/advance-ui/(prismjs)/_components/PrismjsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "PrismJS - PolytronX",
    description:
      "Explore advanced code syntax highlighting with PrismJS for your web applications.",
    keywords: [
      "prismjs",
      "syntax highlighting",
      "code highlighting",
      "react prismjs",
      "code syntax",
      "programming languages",
      "code snippets",
      "code formatting",
      "syntax colors",
      "code themes",
      "language support",
      "code customization",
      "highlight styles",
      "code integration",
      "prism library",
    ],
    openGraph: {
      title: "PrismJS - PolytronX",
      description:
        "Explore advanced code syntax highlighting with PrismJS for your web applications.",
      url: "/advance-ui/prismjs",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PrismjsPage />
    </div>
  );
};

export default Page;
