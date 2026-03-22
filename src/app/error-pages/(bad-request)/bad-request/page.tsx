import React from "react";
import BadRequestPage from "@/app/error-pages/(bad-request)/_components/BadRequestPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "400 Bad Request - PolytronX",
    description:
      "Oops! Something went wrong with your request. Please check the URL and try again.",
    keywords: [
      "400 error",
      "bad request",
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
      "error handling",
    ],
    openGraph: {
      title: "400 Bad Request - PolytronX",
      description:
        "Oops! Something went wrong with your request. Please check the URL and try again.",
      url: "/error-pages/bad-request",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BadRequestPage />
    </div>
  );
};

export default Page;
