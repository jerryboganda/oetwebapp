import React from "react";
import RatingPage from "@/app/advance-ui/(rating)/_components/RatingPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Rating - PolytronX",
    description:
      "Explore advanced rating components and star rating systems for your web applications.",
    keywords: [
      "rating",
      "star rating",
      "react rating",
      "UI rating",
      "rating system",
      "rating component",
      "rating scale",
      "rating customization",
      "rating styles",
      "rating integration",
      "rating library",
      "react rating system",
      "rating feedback",
      "rating display",
      "rating customization",
    ],
    openGraph: {
      title: "Rating - PolytronX",
      description:
        "Explore advanced rating components and star rating systems for your web applications.",
      url: "/advance-ui/rating",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <RatingPage />
    </div>
  );
};

export default Page;
