"use client";

import React, { Suspense, useState, ReactNode, useEffect } from "react";
import TopGo from "../CommonElements/TopGo";
import Loading from "@/app/loading";
import Sidebar from "@/Component/Layouts/Sidebar";
import Header from "@/Component/Layouts/Header";
import Footer from "@/Component/Layouts/Footer";
import { usePathname } from "next/navigation";
import Customizer from "@/Component/Customizer";
import LearnerBottomNav from "@/Component/OET/Layout/LearnerBottomNav";
import { Modal, ModalBody, ModalFooter } from "reactstrap";
import PreviewBrandImage from "@/Component/CommonElements/PreviewBrandImage";
import { getRoleFromPath } from "@/lib/oet/routing";

interface DefaultLayoutProps {
  children: ReactNode;
}

const DefaultLayout: React.FC<DefaultLayoutProps> = ({ children }) => {
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(false);
  const [welcomeModal, setWelcomeModal] = useState<boolean>(false);
  const pathname = usePathname();
  const isOetSurface = Boolean(getRoleFromPath(pathname));

  useEffect(() => {
    const head = document.getElementsByTagName("head")[0];

    for (const weight of [
      "regular",
      "thin",
      "light",
      "bold",
      "fill",
      "duotone",
    ]) {
      const href = `https://unpkg.com/@phosphor-icons/web@2.0.3/src/${weight}/style.css`;
      if (head && !head.querySelector(`link[href="${href}"]`)) {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.type = "text/css";
        link.href = href;
        head.appendChild(link);
      }
    }
    if (
      !isOetSurface &&
      (pathname === "/dashboard/project" || pathname === "/")
    ) {
      setWelcomeModal(true);
      return;
    }

    setWelcomeModal(false);
  }, [isOetSurface, pathname]);

  if (
    pathname.includes("/auth-pages") ||
    pathname.includes("/error-pages") ||
    pathname.includes("/other-pages/maintenance") ||
    pathname.includes("/other-pages/landing") ||
    pathname.includes("/other-pages/coming-soon")
  ) {
    return <>{children}</>;
  }
  const toggleWelcomeModal = () => setWelcomeModal(!welcomeModal);

  return (
    <div className="app-wrapper default">
      <Suspense fallback={<Loading />}>
        <Sidebar sidebarOpen={sidebarOpen} setSidebarOpen={setSidebarOpen} />
        <div className="app-content">
          <Header sidebarOpen={sidebarOpen} setSidebarOpen={setSidebarOpen} />
          <main>{children}</main>
          <TopGo />
          <Footer />
          <LearnerBottomNav />
        </div>
        {!isOetSurface ? <Customizer /> : null}

        <Modal
          isOpen={welcomeModal}
          toggle={toggleWelcomeModal}
          centered={true}
          backdrop="static"
          className="welcome-modal"
        >
          <div className="modal-content welcome-card">
            <ModalBody className="p-0">
              <div className="text-center position-relative welcome-card-content z-1 p-3">
                <div className="text-end position-relative z-1 me-2">
                  <i
                    className="ti ti-x fs-5 text-primary f-w-600 cursor-pointer"
                    onClick={toggleWelcomeModal}
                  ></i>
                </div>
                <h2 className="f-w-700 text-primary-dark mb-0">
                  <span>Welcome!</span>{" "}
                  <img
                    alt="gif"
                    className="w-45 d-inline align-baseline"
                    src="/images/dashboard/ecommerce-dashboard/celebration.gif"
                  />
                </h2>

                <div className="modal-img-box">
                  <PreviewBrandImage
                    alt="PolytronX welcome preview"
                    className="img-fluid"
                    src="/images/modals/welcome-1.png"
                  />
                </div>
                <ModalFooter className="modal-btn mb-4 justify-content-center">
                  <button
                    className="btn btn-primary text-white btn-sm rounded"
                    onClick={toggleWelcomeModal}
                    type="button"
                  >
                    Get Started
                  </button>
                </ModalFooter>
              </div>
            </ModalBody>
          </div>
        </Modal>
      </Suspense>
    </div>
  );
};

export default DefaultLayout;
