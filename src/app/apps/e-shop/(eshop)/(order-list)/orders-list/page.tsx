import React from "react";
import OrderListPage from "@/app/apps/e-shop/(eshop)/(order-list)/_components/OrderListPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Orders List - PolytronX",
    description:
      "View and manage all your orders with our order management system.",
    keywords: [
      "orders list",
      "order management",
      "order tracking",
      "order history",
      "order status",
      "order details",
      "order filtering",
      "order sorting",
      "order search",
      "order export",
      "order statistics",
      "order timeline",
      "order categories",
      "order reporting",
      "order analytics",
    ],
    openGraph: {
      title: "Orders List - PolytronX",
      description:
        "View and manage all your orders with our order management system.",
      url: "/apps/e-shop/orders-list",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <OrderListPage />
    </div>
  );
};

export default Page;
