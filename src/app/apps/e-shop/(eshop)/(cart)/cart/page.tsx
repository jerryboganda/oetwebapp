import React from "react";
import CartPage from "@/app/apps/e-shop/(eshop)/(cart)/_components/CartPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Shopping Cart - PolytronX",
    description:
      "View and manage your shopping cart with our e-commerce platform.",
    keywords: [
      "shopping cart",
      "cart",
      "shopping basket",
      "cart management",
      "cart items",
      "cart checkout",
      "cart total",
      "cart discounts",
      "cart shipping",
      "cart coupons",
      "cart removal",
      "cart updates",
      "cart totals",
      "cart summary",
      "cart features",
    ],
    openGraph: {
      title: "Shopping Cart - PolytronX",
      description:
        "View and manage your shopping cart with our e-commerce platform.",
      url: "/apps/e-shop/cart",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CartPage />
    </div>
  );
};

export default Page;
