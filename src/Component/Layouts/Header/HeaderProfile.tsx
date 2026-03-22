import React, { useState } from "react";
import Link from "next/link";
import { Offcanvas, OffcanvasBody, FormGroup, Input } from "reactstrap";
import {
  BellNotification,
  Dollar,
  EyeClosed,
  HelpCircle,
  Plus,
  UserLove,
  Settings,
} from "iconoir-react";
import { Detective, SignOut } from "phosphor-react";
import Image from "next/image";

type MenuItem = {
  label: string;
  href: string;
  icon: React.ReactNode;
  className?: string;
  external?: boolean;
};

const menuItems: MenuItem[] = [
  {
    label: "Profile Details",
    href: "/apps/profile",
    icon: <UserLove height={24} width={24} className="pe-1 f-s-20" />,
    external: true,
  },
  {
    label: "Settings",
    href: "/apps/setting",
    icon: <Settings height={24} width={24} className="pe-1 f-s-20" />,
    external: true,
  },
  {
    label: "Help",
    href: "/apps/faq",
    icon: <HelpCircle height={24} width={24} className="pe-1 f-s-20" />,
    external: true,
  },
  {
    label: "Pricing",
    href: "/apps/pricing",
    icon: <Dollar height={24} width={24} className="pe-1 f-s-20" />,
    external: true,
  },
  {
    label: "Add account",
    href: "/auth-pages/sign-up",
    icon: <Plus height={24} width={24} className="pe-1 f-s-20" />,
    external: true,
    className: "text-secondary",
  },
];

const HeaderProfile: React.FC = () => {
  const [isCanvasOpen, setCanvasOpen] = useState(false);

  const toggleCanvas = () => setCanvasOpen(!isCanvasOpen);

  return (
    <>
      <a role="button" className="d-block head-icon" onClick={toggleCanvas}>
        <Image
          src="/images/avatar/woman.jpg"
          alt="avatar"
          width={35}
          height={35}
          className="rounded-2"
        />
      </a>

      <Offcanvas
        direction="end"
        isOpen={isCanvasOpen}
        toggle={toggleCanvas}
        className="header-profile-canvas"
      >
        <OffcanvasBody className="app-scroll">
          <ul className="list-unstyled">
            {/* Profile Header */}
            <li className="d-flex gap-3 mb-3">
              <button
                className="btn btn-close icon-btn position-absolute top-2 end-0 me-2"
                onClick={toggleCanvas}
                aria-label="Close"
              />
              <div className="d-flex-center">
                <span className="h-45 w-45 d-flex-center rounded-2 position-relative">
                  <Image
                    alt=""
                    width={45}
                    height={45}
                    className="img-fluid rounded-2"
                    src="/images/avatar/woman.jpg"
                  />
                </span>
              </div>
              <div className="text-center mt-2">
                <h6 className="mb-0">
                  Laura Monaldo{" "}
                  <Image
                    alt="verified"
                    src="/images/profile/01.png"
                    width={20}
                    height={20}
                  />
                </h6>
                <p className="f-s-12 mb-0 text-secondary">
                  lauradesign@gmail.com
                </p>
              </div>
            </li>

            {/* Menu Items */}
            {menuItems.map((item) => (
              <li key={item.label}>
                <Link
                  href={item.href}
                  target={item.external ? "_blank" : undefined}
                  className={`f-w-500 d-block ${item.className || ""}`}
                >
                  {item.icon} {item.label}
                </Link>
              </li>
            ))}

            <li className="app-divider-v dotted py-1" />

            {/* Dropdown */}
            <li>
              <Link
                href="/apps/setting"
                target="_blank"
                className={`f-w-500 d-block`}
              >
                <EyeClosed height={24} width={24} className="pe-1 f-s-20" />
                Hide Settings
              </Link>
            </li>

            {/* Notification Toggle */}
            <li className="d-flex align-items-center justify-content-between">
              <a className="f-w-500" href="#">
                <BellNotification
                  height={24}
                  width={24}
                  className="pe-1 f-s-20"
                />
                Notification
              </a>
              <FormGroup switch className="mb-0">
                <Input type="switch" role="switch" id="basicSwitch" />
              </FormGroup>
            </li>

            {/* Incognito Toggle */}
            <li className="d-flex align-items-center justify-content-between">
              <a className="f-w-500" href="#">
                <Detective
                  height={24}
                  width={24}
                  weight="duotone"
                  className="pe-1 f-s-20"
                />
                Incognito
              </a>
              <FormGroup switch className="mb-0">
                <Input type="switch" role="switch" id="incognitoSwitch" />
              </FormGroup>
            </li>

            <li className="app-divider-v dotted py-1" />

            {/* Logout Button */}
            <li>
              <Link
                className="mb-0 btn btn-light-danger btn-sm d-flex align-items-center justify-content-center"
                href="/auth-pages/sign-in"
                role="button"
              >
                <SignOut
                  weight="duotone"
                  height={24}
                  width={24}
                  className="pe-1 f-s-20"
                />
                Log Out
              </Link>
            </li>
          </ul>
        </OffcanvasBody>
      </Offcanvas>
    </>
  );
};

export default HeaderProfile;
