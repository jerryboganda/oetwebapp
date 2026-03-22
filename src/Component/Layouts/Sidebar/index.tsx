import Scrollbar from "simplebar-react";
import MenuItem from "./MenuItem";
import Link from "next/link";
import { MenuList } from "@/Data/Sidebar/sidebar";
import React, { Fragment, useState, useRef, useEffect } from "react";
import { SidebarProps } from "@/interface/common";
import { IconChevronsRight } from "@tabler/icons-react";
import HorizontalNav from "@/Component/Layouts/Sidebar/HorizontalNav";

const Sidebar: React.FC<SidebarProps> = ({ sidebarOpen, setSidebarOpen }) => {
  const [navTranslateX, setNavTranslateX] = useState(0);
  const [isHorizontal, setIsHorizontal] = useState(false);
  const navRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    const getSidebarOption = () => {
      if (typeof window === "undefined") return "vertical-sidebar";
      return localStorage.getItem("PolytronX-Theme-sidebar-option");
    };

    const checkSidebarOption = () => {
      const option = getSidebarOption();
      setIsHorizontal(option === "horizontal-sidebar");
    };

    checkSidebarOption();

    const interval = setInterval(checkSidebarOption, 100);
    return () => clearInterval(interval);
  }, []);

  return (
    <nav
      className={["vertical-sidebar", sidebarOpen && "semi-nav"]
        .filter(Boolean)
        .join(" ")}
    >
      <div className="app-logo">
        <Link className="logo d-inline-block" href="/dashboard/project">
          <img
            src="/images/logo/polytronx-dark.svg"
            alt="#"
            className="dark-logo"
          />
        </Link>
        <span
          className="bg-light-light toggle-semi-nav"
          onClick={() => setSidebarOpen?.(!sidebarOpen)}
        >
          <IconChevronsRight className="f-s-20" />
        </span>
      </div>
      <Scrollbar className="app-nav simplebar-scrollable-y">
        <div
          className="nav-container"
          style={{
            overflow: isHorizontal ? "hidden" : "visible",
          }}
        >
          <ul
            ref={navRef}
            className="main-nav p-0 mt-2"
            style={{
              marginLeft: isHorizontal ? `${navTranslateX}px` : "0px",
              transition: isHorizontal ? "margin-left 0.3s ease" : "none",
            }}
          >
            {MenuList.map((opt, index) => (
              <Fragment key={index}>
                <MenuItem
                  title={opt.title ?? ""}
                  type={opt.type ?? ""}
                  path={opt.path}
                  name={opt.name}
                  iconClass={opt.iconClass}
                  badgeCount={opt.badgeCount}
                  links={opt.children}
                  collapseId={opt.collapseId}
                />
              </Fragment>
            ))}
          </ul>
        </div>
      </Scrollbar>
      <HorizontalNav navRef={navRef} onScroll={setNavTranslateX} />
    </nav>
  );
};

export default Sidebar;
