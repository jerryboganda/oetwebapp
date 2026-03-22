import React from "react";
import BlogPage from "@/app/apps/blog-page/(blog-page)/(blog)/_components/BlogPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Blog - PolytronX",
    description:
      "Read our latest articles and insights on technology, development, and industry trends.",
    keywords: [
      "blog",
      "articles",
      "technology",
      "development",
      "industry trends",
      "technical articles",
      "web development",
      "software engineering",
      "programming",
      "tech news",
      "coding tutorials",
      "web design",
      "software architecture",
      "development best practices",
      "tech insights",
    ],
    openGraph: {
      title: "Blog - PolytronX",
      description:
        "Read our latest articles and insights on technology, development, and industry trends.",
      url: "/apps/blog",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BlogPage />
    </div>
  );
};

export default Page;
