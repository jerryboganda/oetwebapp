import React from "react";
import ForbiddenPage from "@/app/error-pages/(forbidden)/_components/ForbiddenPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "403 Forbidden - PolytronX",
    description: "Access denied. You don't have permission to view this page.",
    keywords: [
      "403 error",
      "forbidden",
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
      "access denied",
    ],
    openGraph: {
      title: "403 Forbidden - PolytronX",
      description:
        "Access denied. You don't have permission to view this page.",
      url: "/error-pages/forbidden",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ForbiddenPage />
    </div>
  );
};

export default Page;
