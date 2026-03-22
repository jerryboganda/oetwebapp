import React from "react";
import OffcanvasPage from "@/app/advance-ui/(offcanvas_toggle)/_components/OffcanvasPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Offcanvas Toggle - PolytronX",
    description:
      "Explore advanced offcanvas components and navigation toggles for your web applications.",
    keywords: [
      "offcanvas",
      "offcanvas toggle",
      "navigation",
      "mobile menu",
      "react offcanvas",
      "sidebar",
      "slide menu",
      "offcanvas menu",
      "offcanvas navigation",
      "offcanvas customization",
      "offcanvas styles",
      "offcanvas effects",
      "offcanvas integration",
      "offcanvas library",
      "react navigation",
    ],
    openGraph: {
      title: "Offcanvas Toggle - PolytronX",
      description:
        "Explore advanced offcanvas components and navigation toggles for your web applications.",
      url: "/advance-ui/offcanvas-toggle",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <OffcanvasPage />
    </div>
  );
};

export default Page;
