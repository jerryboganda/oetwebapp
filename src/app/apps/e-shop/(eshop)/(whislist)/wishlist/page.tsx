import React from "react";
import WishlistPage from "@/app/apps/e-shop/(eshop)/(whislist)/_components/WishlistPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Wishlist - PolytronX",
    description:
      "Save and organize products you love in your personal wishlist.",
    keywords: [
      "wishlist",
      "favorites",
      "saved products",
      "wish list",
      "product wishlist",
      "wishlist management",
      "wishlist sharing",
      "wishlist organization",
      "wishlist categories",
      "wishlist sync",
      "wishlist export",
      "wishlist history",
      "wishlist updates",
      "wishlist features",
      "wishlist integration",
    ],
    openGraph: {
      title: "Wishlist - PolytronX",
      description:
        "Save and organize products you love in your personal wishlist.",
      url: "/apps/e-shop/wishlist",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <WishlistPage />
    </div>
  );
};

export default Page;
