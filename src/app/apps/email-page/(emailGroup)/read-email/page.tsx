import React from "react";
import ReadEmailPage from "@/app/apps/email-page/(emailGroup)/_components/ReadEmailPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Read Email - PolytronX",
    description:
      "Explore advanced email reading and message viewing features for your web applications.",
    keywords: [
      "read email",
      "email reader",
      "message viewing",
      "react email reader",
      "UI email reader",
      "email component",
      "email viewing",
      "email customization",
      "email styles",
      "email effects",
      "email integration",
      "email library",
      "react email app",
      "email optimization",
      "email performance",
    ],
    openGraph: {
      title: "Read Email - PolytronX",
      description:
        "Explore advanced email reading and message viewing features for your web applications.",
      url: "/apps/email/read-email",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ReadEmailPage />
    </div>
  );
};

export default Page;
