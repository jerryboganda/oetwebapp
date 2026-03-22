import React from "react";
import ProductDetailsPage from "@/app/apps/e-shop/(eshop)/(product-details)/_components/ProductDetailsPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Product Details - PolytronX",
    description:
      "View detailed information about our products including specifications, pricing, and availability.",
    keywords: [
      "product details",
      "product information",
      "product specifications",
      "product pricing",
      "product availability",
      "product images",
      "product variants",
      "product attributes",
      "product reviews",
      "product ratings",
      "product comparison",
      "product features",
      "product stock",
      "product categories",
      "product tags",
    ],
    openGraph: {
      title: "Product Details - PolytronX",
      description:
        "View detailed information about our products including specifications, pricing, and availability.",
      url: "/apps/e-shop/product-details",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProductDetailsPage />
    </div>
  );
};

export default Page;
