import React from "react";
import ServiceUnavailablePage from "@/app/error-pages/(service-unavailable)/_components/ServiceUnavailablePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "503 Service Unavailable - PolytronX",
    description:
      "The service is temporarily unavailable. Please try again later.",
    keywords: [
      "503 error",
      "service unavailable",
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
      "service error",
    ],
    openGraph: {
      title: "503 Service Unavailable - PolytronX",
      description:
        "The service is temporarily unavailable. Please try again later.",
      url: "/error-pages/service-unavailable",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ServiceUnavailablePage />
    </div>
  );
};

export default Page;
