import React from "react";
import TodoPage from "@/app/apps/(todo)/_components/TodoPage";
import type { Metadata } from "next";

export const dynamic = "force-static";
export const revalidate = false;

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "To-Do - PolytronX",
    description:
      "Manage your tasks and to-do list with our task management system.",
    keywords: [
      "to-do",
      "task management",
      "tasks",
      "to-do list",
      "task tracking",
      "task organization",
      "task prioritization",
      "task categories",
      "task status",
      "task reminders",
      "task assignment",
      "task search",
      "task filtering",
      "task completion",
      "task reporting",
    ],
    openGraph: {
      title: "To-Do - PolytronX",
      description:
        "Manage your tasks and to-do list with our task management system.",
      url: "/apps/todo",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
    alternates: {
      canonical: "/apps/todo",
    },
    other: {
      "Cache-Control": "public, max-age=31536000, immutable",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TodoPage />
    </div>
  );
};

export default Page;
