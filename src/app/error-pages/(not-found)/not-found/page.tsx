import React from "react";
import NotFoundPage from "@/app/error-pages/(not-found)/_components/NotFoundPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "404 Not Found - PolytronX",
    description:
      "The page you're looking for doesn't exist. Please check the URL and try again.",
    keywords: [
      "404 error",
      "not found",
      "error page",
      "react error page",
      "UI error page",
      "error component",
      "error handling",
      "error customization",
      "error styles",
      "error effects",
      "error integration",
      "error library",
      "react error",
      "error optimization",
      "error performance",
      "page not found",
    ],
    openGraph: {
      title: "404 Not Found - PolytronX",
      description:
        "The page you're looking for doesn't exist. Please check the URL and try again.",
      url: "/error-pages/not-found",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <NotFoundPage />
    </div>
  );
};

export default Page;
