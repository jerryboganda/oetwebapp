"use client";
import NotFoundPage from "@/app/error-pages/(not-found)/_components/NotFoundPage";
import { useEffect } from "react";

export default function Error({ error }: { error: Error; reset: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return <NotFoundPage />;
}
