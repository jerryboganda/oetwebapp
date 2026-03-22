import React from "react";
import AddBlogPage from "@/app/apps/blog-page/(blog-page)/(add-blog)/_components/AddBlogPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Add Blog - PolytronX",
    description:
      "Create and publish new blog articles with our intuitive blog management system.",
    keywords: [
      "add blog",
      "create blog",
      "blog management",
      "article creation",
      "content creation",
      "blog publishing",
      "blog editing",
      "blog categories",
      "blog tags",
      "blog SEO",
      "blog images",
      "blog formatting",
      "blog preview",
      "blog scheduling",
      "blog analytics",
    ],
    openGraph: {
      title: "Add Blog - PolytronX",
      description:
        "Create and publish new blog articles with our intuitive blog management system.",
      url: "/apps/add-blog",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AddBlogPage />
    </div>
  );
};

export default Page;
