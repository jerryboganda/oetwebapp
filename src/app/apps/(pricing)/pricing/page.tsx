import React from "react";
import PricingPage from "@/app/apps/(pricing)/_components/PricingPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Pricing - PolytronX",
    description:
      "Discover our flexible pricing plans and features tailored for your needs.",
    keywords: [
      "pricing",
      "pricing plans",
      "subscription",
      "pricing tiers",
      "cost calculator",
      "pricing comparison",
      "features",
      "pricing table",
      "subscription plans",
      "pricing options",
      "value proposition",
      "pricing strategy",
      "plan comparison",
      "pricing structure",
      "pricing benefits",
    ],
    openGraph: {
      title: "Pricing - PolytronX",
      description:
        "Discover our flexible pricing plans and features tailored for your needs.",
      url: "/apps/pricing",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PricingPage />
    </div>
  );
};

export default Page;
