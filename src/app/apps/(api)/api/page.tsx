import React from "react";
import ApiPage from "@/app/apps/(api)/_components/ApiPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "API - PolytronX",
    description:
      "Explore our comprehensive API documentation and integration options for developers.",
    keywords: [
      "API documentation",
      "REST API",
      "API endpoints",
      "API integration",
      "developer tools",
      "API reference",
      "API documentation",
      "API endpoints",
      "API integration",
      "developer tools",
      "API reference",
      "API documentation",
      "API endpoints",
      "API integration",
      "developer tools",
      "API reference",
    ],
    openGraph: {
      title: "API - PolytronX",
      description:
        "Comprehensive API documentation and integration options for developers.",
      url: "/apps/api",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return <ApiPage />;
};

export default Page;
