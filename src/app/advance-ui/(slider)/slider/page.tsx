import React from "react";
import SliderPage from "@/app/advance-ui/(slider)/_components/SliderPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Slider - PolytronX",
    description:
      "Explore advanced slider components and image galleries for your web applications.",
    keywords: [
      "slider",
      "image slider",
      "react slider",
      "carousel",
      "image gallery",
      "slider component",
      "slider customization",
      "slider effects",
      "slider navigation",
      "slider integration",
      "slider library",
      "react carousel",
      "slider animation",
      "slider optimization",
      "slider performance",
    ],
    openGraph: {
      title: "Slider - PolytronX",
      description:
        "Explore advanced slider components and image galleries for your web applications.",
      url: "/advance-ui/slider",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SliderPage />
    </div>
  );
};

export default Page;
