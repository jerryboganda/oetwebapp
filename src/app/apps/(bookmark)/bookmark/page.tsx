import React from "react";
import BookmarkPage from "@/app/apps/(bookmark)/_components/BookmarkPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Bookmarks - PolytronX",
    description:
      "Explore advanced bookmark management and organization features for your web applications.",
    keywords: [
      "bookmarks",
      "bookmark management",
      "react bookmarks",
      "UI bookmarks",
      "bookmark component",
      "bookmark organization",
      "bookmark customization",
      "bookmark styles",
      "bookmark effects",
      "bookmark integration",
      "bookmark library",
      "react bookmark app",
      "bookmark optimization",
      "bookmark performance",
      "bookmark system",
      "bookmark categories",
      "bookmark tags",
      "bookmark storage",
      "bookmark sharing",
      "bookmark sync",
    ],
    openGraph: {
      title: "Bookmarks - PolytronX",
      description:
        "Organize and manage your bookmarks with our intuitive bookmark management system.",
      url: "/apps/bookmark",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BookmarkPage />
    </div>
  );
};

export default Page;
