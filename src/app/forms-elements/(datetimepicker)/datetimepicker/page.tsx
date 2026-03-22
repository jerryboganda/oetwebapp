import React from "react";
import DatetimePickerPage from "@/app/forms-elements/(datetimepicker)/_components/DatetimePickerPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Datetime Picker Forms - PolytronX",
    description:
      "Explore datetime picker components for selecting dates and times in your React forms.",
    keywords: [
      "datetime picker",
      "date picker",
      "time picker",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "datetime customization",
      "date customization",
      "time customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "datetime selection",
      "calendar integration",
    ],
    openGraph: {
      title: "Datetime Picker Forms - PolytronX",
      description:
        "Explore datetime picker components for selecting dates and times in your React forms.",
      url: "/forms-elements/datetimepicker",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <DatetimePickerPage />
    </div>
  );
};

export default Page;
