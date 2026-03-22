import React from "react";
import InternalServerPage from "@/app/error-pages/(internal-server)/_components/InternalServerPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "500 Internal Server Error - PolytronX",
    description:
      "Oops! Something went wrong on our end. Please try again later or contact support.",
    keywords: [
      "500 error",
      "internal server error",
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
      "server error",
    ],
    openGraph: {
      title: "500 Internal Server Error - PolytronX",
      description:
        "Oops! Something went wrong on our end. Please try again later or contact support.",
      url: "/error-pages/internal-server",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <InternalServerPage />
    </div>
  );
};

export default Page;
