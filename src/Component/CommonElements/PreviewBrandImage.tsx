"use client";

import React from "react";

interface PreviewBrandImageProps {
  src: string;
  alt: string;
  className?: string;
  badgeClassName?: string;
  lightBadge?: boolean;
}

const baseBadgeStyle: React.CSSProperties = {
  position: "absolute",
  top: "14px",
  left: "14px",
  zIndex: 2,
  padding: "6px 12px",
  borderRadius: "999px",
  fontSize: "12px",
  fontWeight: 700,
  lineHeight: 1,
  letterSpacing: "0.4px",
  boxShadow: "0 10px 25px rgba(0, 0, 0, 0.12)",
};

export default function PreviewBrandImage({
  src,
  alt,
  className,
  badgeClassName,
  lightBadge = false,
}: PreviewBrandImageProps) {
  return (
    <div className={`position-relative ${badgeClassName || ""}`}>
      <span
        style={{
          ...baseBadgeStyle,
          background: lightBadge
            ? "rgba(38, 37, 55, 0.9)"
            : "rgba(255, 255, 255, 0.96)",
          color: lightBadge ? "#ffffff" : "#262537",
        }}
      >
        PolytronX
      </span>
      <img src={src} alt={alt} className={className} />
    </div>
  );
}
