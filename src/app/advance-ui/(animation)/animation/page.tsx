import React from "react";
import AnimationPage from "@/app/advance-ui/(animation)/_components/AnimationPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Animation - PolytronX",
    description:
      "Explore animation components and effects for enhanced user experience in your React application.",
    keywords: [
      "animation",
      "react components",
      "UI components",
      "react animation",
      "JavaScript animation",
      "transition effects",
      "animation library",
      "motion effects",
      "UI animation",
      "animation examples",
      "animation controls",
      "animation timing",
      "animation sequences",
      "react animation library",
    ],
    openGraph: {
      title: "Animation - PolytronX",
      description:
        "Explore advanced animation components and effects for your web applications.",
      url: "/advance-ui/animation",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AnimationPage />
    </div>
  );
};

export default Page;
