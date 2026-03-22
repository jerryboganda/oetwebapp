import React from "react";
import BlogDetailsPage from "@/app/apps/blog-page/(blog-page)/(blog-details)/_components/BlogDetailsPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Blog Details - PolytronX",
    description:
      "Read in-depth articles and detailed insights on technology and development topics.",
    keywords: [
      "blog details",
      "article details",
      "technical articles",
      "in-depth analysis",
      "development insights",
      "programming tutorials",
      "web development",
      "software engineering",
      "coding guides",
      "tech tutorials",
      "article comments",
      "author information",
      "related articles",
      "technical documentation",
      "development resources",
    ],
    openGraph: {
      title: "Blog Details - PolytronX",
      description:
        "Read in-depth articles and detailed insights on technology and development topics.",
      url: "/apps/blog-details",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BlogDetailsPage />
    </div>
  );
};

export default Page;
