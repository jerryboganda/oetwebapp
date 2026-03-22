"use client";
import React, { useEffect } from "react";

export default function PrismCodeWrapper({
  children,
}: {
  children: React.ReactNode;
}) {
  useEffect(() => {
    if (typeof window !== "undefined") {
      import("prismjs").then((Prism) => {
        Prism.highlightAll();
      });
    }
  }, []);

  return <>{children}</>;
}
