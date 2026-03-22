import React, { useEffect, useRef, useState } from "react";
import { IconChevronLeft, IconChevronRight } from "@tabler/icons-react";

interface HorizontalNavProps {
  navRef: React.RefObject<HTMLUListElement>;
  onScroll: (translateX: number) => void;
}

const HorizontalNav: React.FC<HorizontalNavProps> = ({ navRef, onScroll }) => {
  const scrollPositionRef = useRef(0);
  const size = 150;
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  const getLayout = () => {
    if (!isClient) return "ltr";
    const layout = localStorage.getItem("PolytronX-Theme-layout-option");
    return layout || "ltr";
  };

  const getSidebarOption = () => {
    if (typeof window === "undefined") return "vertical-sidebar";
    return localStorage.getItem("PolytronX-Theme-sidebar-option");
  };

  const [sidebarOption, setSidebarOption] = useState(() => getSidebarOption());

  useEffect(() => {
    if (!isClient) return;

    setSidebarOption(getSidebarOption());

    const interval = setInterval(() => {
      const currentOption = getSidebarOption();
      if (currentOption !== sidebarOption) {
        setSidebarOption(currentOption);
      }
    }, 100);

    return () => clearInterval(interval);
  }, [isClient, sidebarOption]);

  const scrollNav = (direction: "left" | "right") => {
    const nav = navRef.current;
    if (!nav) return;

    const layout = getLayout();
    const isRTL = layout === "rtl";
    const container = nav.parentElement;
    if (!container) return;

    const containerWidth = container.clientWidth;
    const navWidth = nav.scrollWidth;
    const maxOffset = navWidth - containerWidth;

    let newOffset;

    if (direction === "left") {
      newOffset = Math.max(
        scrollPositionRef.current - size,
        isRTL ? -maxOffset : 0
      );
    } else {
      newOffset = Math.min(
        scrollPositionRef.current + size,
        isRTL ? 0 : maxOffset
      );
    }

    const translateX = isRTL ? newOffset : -newOffset;
    onScroll(translateX);
    scrollPositionRef.current = newOffset;
  };

  if (!isClient) return null;

  return (
    <>
      {sidebarOption === "horizontal-sidebar" && (
        <div className="menu-navs">
          <span className="menu-previous" onClick={() => scrollNav("left")}>
            <IconChevronLeft />
          </span>
          <span className="menu-next" onClick={() => scrollNav("right")}>
            <IconChevronRight />
          </span>
        </div>
      )}
    </>
  );
};

export default HorizontalNav;
