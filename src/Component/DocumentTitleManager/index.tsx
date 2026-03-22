"use client";

import { useEffect } from "react";

interface DocumentTitleManagerProps {
  defaultTitle: string;
  blurTitle: string;
}

export default function DocumentTitleManager({
  defaultTitle,
  blurTitle,
}: DocumentTitleManagerProps) {
  useEffect(() => {
    const handleFocus = () => {
      document.title = defaultTitle;
    };

    const handleBlur = () => {
      document.title = blurTitle;
    };

    window.addEventListener("focus", handleFocus);
    window.addEventListener("blur", handleBlur);

    return () => {
      window.removeEventListener("focus", handleFocus);
      window.removeEventListener("blur", handleBlur);
    };
  }, [defaultTitle, blurTitle]);

  return null;
}
