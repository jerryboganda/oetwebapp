import React from "react";
import EcommerceDashboard from "@/Component/Dashboard/EcommerceDashboard";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "E-commerce Dashboard - PolytronX",
    description:
      "Monitor and manage your e-commerce operations with our comprehensive dashboard featuring real-time analytics and insights.",
    keywords: [
      "e-commerce dashboard",
      "dashboard",
      "react dashboard",
      "UI dashboard",
      "dashboard component",
      "e-commerce analytics",
      "sales dashboard",
      "business dashboard",
      "dashboard customization",
      "dashboard styles",
      "dashboard effects",
      "dashboard integration",
      "dashboard library",
      "react dashboard app",
      "dashboard optimization",
      "dashboard performance",
      "business analytics",
    ],
    openGraph: {
      title: "E-commerce Dashboard - PolytronX",
      description:
        "Monitor and manage your e-commerce operations with our comprehensive dashboard featuring real-time analytics and insights.",
      url: "/dashboard/ecommerce",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <EcommerceDashboard />
    </div>
  );
};

export default Page;
