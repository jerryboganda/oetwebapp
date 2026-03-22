import React, { useCallback, useEffect, useRef, useState } from "react";
import { getLocalStorageItem } from "@/_helper";
import Link from "next/link";
import { Settings } from "iconoir-react/regular";
import { Offcanvas } from "react-bootstrap";

const themeName = "PolytronX-Theme";

const setLocalStorageItem = (key: string, value: string) => {
  if (typeof window !== "undefined") {
    localStorage.setItem(`${themeName}-${key}`, value);
  }
};

function componentToHex(c: number) {
  const hex = c.toString(16);
  return hex.length === 1 ? "0" + hex : hex;
}

function rgbToHex(r: number, g: number, b: number) {
  return "#" + componentToHex(r) + componentToHex(g) + componentToHex(b);
}

const Customizer = () => {
  const [sidebarOption, setSidebarOption] = useState("vertical-sidebar");
  const [layoutOption, setLayoutOption] = useState("ltr");
  const [colorOption, setColorOption] = useState("default");
  const [textOption, setTextOption] = useState("medium-text");
  const [showOffcanvas, setShowOffcanvas] = useState(false);

  const navRefs = useRef<HTMLElement[]>([]);
  const mainNavRef = useRef<HTMLElement | null>(null);
  const appWrapperRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const observer = new MutationObserver((mutations) => {
      mutations.forEach((mutation) => {
        mutation.addedNodes.forEach((node) => {
          if (
            node.nodeType === Node.ELEMENT_NODE &&
            (node as Element).tagName === "NAV"
          ) {
            navRefs.current.push(node as HTMLElement);
          }
        });
      });
    });

    observer.observe(document.body, {
      childList: true,
      subtree: true,
    });

    mainNavRef.current = document.querySelector(".main-nav");
    appWrapperRef.current = document.querySelector(".app-wrapper");

    navRefs.current = Array.from(document.querySelectorAll("nav"));

    return () => {
      observer.disconnect();
      navRefs.current = [];
    };
  }, []);

  useEffect(() => {
    setSidebarOption(getLocalStorageItem("sidebar-option", "vertical-sidebar"));
    setLayoutOption(getLocalStorageItem("layout-option", "ltr"));
    setColorOption(getLocalStorageItem("color-option", "default"));
    setTextOption(getLocalStorageItem("text-option", "medium-text"));
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;

    navRefs.current.forEach((nav) => {
      nav.classList.remove(
        "dark-sidebar",
        "horizontal-sidebar",
        "vertical-sidebar"
      );
      nav.classList.add(sidebarOption);
      if (sidebarOption === "vertical-sidebar" && mainNavRef.current) {
        mainNavRef.current.style.marginLeft = "0px";
      }
    });

    document.body.setAttribute("text", textOption);

    document.body.className = layoutOption;
    document.documentElement.setAttribute("dir", layoutOption);
    if (layoutOption === "box-layout") {
      document.documentElement.removeAttribute("dir");
    }

    if (appWrapperRef.current) {
      ["default", "gold", "warm", "happy", "nature", "hot"].forEach((color) => {
        appWrapperRef.current?.classList.remove(color);
      });
      appWrapperRef.current?.classList.add(colorOption);
    }
  }, [sidebarOption, layoutOption, colorOption, textOption]);

  const handleSidebarOptionChange = useCallback((option: string) => {
    setSidebarOption(option);
    setLocalStorageItem("sidebar-option", option);
  }, []);

  const handleLayoutOptionChange = useCallback((option: string) => {
    setLayoutOption(option);
    setLocalStorageItem("layout-option", option);
  }, []);

  const handleColorOptionChange = useCallback((option: string) => {
    setColorOption(option);

    const tempElement = document.createElement("div");
    tempElement.className = option;
    tempElement.style.display = "none";
    document.body.appendChild(tempElement);

    const primaryColorValue = getComputedStyle(tempElement)
      .getPropertyValue("--primary")
      .trim();

    if (primaryColorValue) {
      const primaryColorValues = primaryColorValue.split(",");
      if (primaryColorValues.length === 3) {
        const primaryColorHex = rgbToHex(
          parseInt(primaryColorValues[0] || ""),
          parseInt(primaryColorValues[1] || ""),
          parseInt(primaryColorValues[2] || "")
        );
        setLocalStorageItem("color-primary", primaryColorHex);
      }
    }

    const secondaryColorValue = getComputedStyle(tempElement)
      .getPropertyValue("--secondary")
      .trim();

    if (secondaryColorValue) {
      const secondaryColorValues = secondaryColorValue.split(",");
      if (secondaryColorValues.length === 3) {
        const secondaryColorHex = rgbToHex(
          parseInt(secondaryColorValues[0] || ""),
          parseInt(secondaryColorValues[1] || ""),
          parseInt(secondaryColorValues[2] || "")
        );
        setLocalStorageItem("color-secondary", secondaryColorHex);
      }
    }

    document.body.removeChild(tempElement);
    setLocalStorageItem("color-option", option);
  }, []);

  const handleTextOptionChange = useCallback((option: string) => {
    setTextOption(option);
    setLocalStorageItem("text-option", option);
  }, []);

  const resetCustomizer = useCallback(() => {
    setLocalStorageItem("sidebar-option", "dark-sidebar");
    setLocalStorageItem("layout-option", "ltr");
    setLocalStorageItem("color-option", "default");
    setLocalStorageItem("text-option", "medium-text");
    if (typeof window !== "undefined") {
      localStorage.clear();
      window.location.reload();
    }
  }, []);

  return (
    <>
      <button
        className="customizer-btn"
        type="button"
        onClick={() => setShowOffcanvas(true)}
      >
        <Settings />
      </button>
      <Offcanvas
        show={showOffcanvas}
        onHide={() => setShowOffcanvas(false)}
        placement={layoutOption === "rtl" ? "start" : "end"}
        className="offcanvas offcanvas-end app-customizer"
        style={{
          ...(layoutOption === "rtl" && {
            left: 0,
            right: "auto",
            transform:
              layoutOption === "rtl" ? "translateX(0%)" : "translateX(100%)",
          }),
        }}
      >
        <Offcanvas.Header className="flex-wrap bg-primary">
          <Offcanvas.Title className="text-white">
            Admin Customizer
          </Offcanvas.Title>
          <p className="d-block text-white opacity-75 mb-0">
            {" "}
            {/* Add mb-0 */}
            It&#39;s time to style according to your choice!
          </p>
          <button
            type="button"
            className="btn-close btn-close-white"
            onClick={() => setShowOffcanvas(false)}
            aria-label="Close"
          ></button>
        </Offcanvas.Header>

        <Offcanvas.Body className="offcanvas-body">
          <div className="app-divider-v secondary my-3">
            <h6 className="mt-2">Sidebar option</h6>
          </div>
          <ul className="sidebar-option d-flex gap-1">
            <li
              className={sidebarOption === "vertical-sidebar" ? "selected" : ""}
              onClick={() => handleSidebarOptionChange("vertical-sidebar")}
            >
              <ul>
                <li className="header"></li>
                <li className="sidebar"></li>
                <li className="body">
                  <span className="badge text-bg-secondary b-r-6">
                    Vertical
                  </span>
                </li>
              </ul>
            </li>
            <li
              className={
                sidebarOption === "horizontal-sidebar" ? "selected" : ""
              }
              onClick={() => handleSidebarOptionChange("horizontal-sidebar")}
            >
              <ul>
                <li className="header h-20">
                  <span className="badge text-bg-secondary b-r-6">
                    Horizontal
                  </span>
                </li>
                <li className="body w-100"></li>
              </ul>
            </li>
            <li
              className={sidebarOption === "dark-sidebar" ? "selected" : ""}
              onClick={() => handleSidebarOptionChange("dark-sidebar")}
            >
              <ul>
                <li className="header"></li>
                <li className="sidebar bg-dark-600"></li>
                <li className="body">
                  <span className="badge text-bg-secondary b-r-6">Dark</span>
                </li>
              </ul>
            </li>
          </ul>

          <div className="app-divider-v secondary my-3">
            <h6 className="mt-2">Layout option</h6>
          </div>
          <ul className="layout-option d-flex gap-1">
            <li
              className={layoutOption === "ltr" ? "selected" : ""}
              onClick={() => handleLayoutOptionChange("ltr")}
            >
              <ul>
                <li className="header" />
                <li className="sidebar" />
                <li className="body">
                  <span className="badge text-bg-secondary b-r-6">LTR</span>
                </li>
              </ul>
            </li>
            <li
              className={layoutOption === "rtl" ? "selected" : ""}
              onClick={() => handleLayoutOptionChange("rtl")}
            >
              <ul>
                <li className="header" />
                <li className="body">
                  <span className="badge text-bg-secondary b-r-6">RTL</span>
                </li>
                <li className="sidebar" />
              </ul>
            </li>
            <li
              className={layoutOption === "box-layout" ? "selected" : ""}
              onClick={() => handleLayoutOptionChange("box-layout")}
            >
              <ul>
                <li className="header" />
                <li className="sidebar" />
                <li className="body">
                  <span className="badge text-bg-secondary b-r-6">Box</span>
                </li>
              </ul>
            </li>
          </ul>

          <h6 className="mt-3">Color Hint</h6>
          <ul className="color-hint p-0 d-flex gap-1">
            {["default", "gold", "warm", "happy", "nature", "hot"].map(
              (color) => (
                <li
                  key={color}
                  className={
                    (colorOption === color ? "selected" : "") + ` ${color}`
                  }
                  onClick={() => handleColorOptionChange(color)}
                >
                  <div />
                </li>
              )
            )}
          </ul>

          <div className="app-divider-v secondary my-3">
            <h6 className="mt-2 font-primary">Text size</h6>
          </div>
          <ul className="text-size d-flex gap-1">
            {["small-text", "medium-text", "large-text"].map((size) => (
              <li
                key={size}
                className={textOption === size ? "selected" : ""}
                onClick={() => handleTextOptionChange(size)}
              >
                {size.split("-")[0]}
              </li>
            ))}
          </ul>
        </Offcanvas.Body>

        <div className="offcanvas-footer">
          <div className="d-flex gap-2">
            <button
              type="button"
              className="btn btn-danger w-100"
              onClick={resetCustomizer}
            >
              Reset
            </button>
            <Link
              type="button"
              className="btn btn-success w-100"
              href="https://polytronx.com"
              target="_blank"
            >
              Explore
            </Link>
          </div>
          <div className="d-flex gap-2 mt-2">
            <Link
              type="button"
              className="btn btn-primary w-100"
              href="mailto:support@polytronx.com"
            >
              Support
            </Link>
            <Link
              type="button"
              className="btn btn-dark w-100"
              href="https://polytronx.com/support"
            >
              Docs
            </Link>
          </div>
        </div>
      </Offcanvas>
    </>
  );
};

export default Customizer;
