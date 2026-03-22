import React, { useState } from "react";
import {
  Offcanvas,
  OffcanvasHeader,
  OffcanvasBody,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  UncontrolledDropdown,
  Row,
} from "reactstrap";
import { FadersHorizontal } from "@phosphor-icons/react";
import { linkData } from "@/Data/HeaderMenuData";
import { MenuItem } from "@/interface/common";
import { KeyCommand } from "iconoir-react";

const HeaderApps: React.FC = () => {
  const [isOpen, setIsOpen] = useState(false);
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const toggleCanvas = () => setIsOpen(!isOpen);
  const toggleDropdown = () => setDropdownOpen(!dropdownOpen);

  return (
    <>
      <a
        type="button"
        className="d-block head-icon p-0 border-0 shadow-none bg-transparent"
        onClick={toggleCanvas}
      >
        <KeyCommand className="fs-6" />
      </a>

      <Offcanvas
        isOpen={isOpen}
        toggle={toggleCanvas}
        direction="end"
        className="header-apps-canvas"
      >
        <OffcanvasHeader toggle={toggleCanvas}>
          <div className="d-flex justify-content-between align-items-center w-100">
            <UncontrolledDropdown className="ms-auto">
              <DropdownToggle
                tag="button"
                className="btn p-1 bg-transparent border-0"
              >
                <FadersHorizontal size={20} weight="bold" />
              </DropdownToggle>

              <DropdownMenu className="mb-3 p-2 mt-4">
                <DropdownItem>Privacy Settings</DropdownItem>
                <DropdownItem>Account Settings</DropdownItem>
                <DropdownItem>Accessibility</DropdownItem>
                <DropdownItem divider />

                {/* Nested Dropdown separated from DropdownItem */}
                <Dropdown
                  isOpen={dropdownOpen}
                  toggle={toggleDropdown}
                  direction="end"
                  className="w-100"
                >
                  <DropdownToggle
                    tag="button"
                    className="dropdown-item d-flex justify-content-between align-items-center"
                  >
                    More Settings
                  </DropdownToggle>
                  <DropdownMenu className="sub-menu p-2">
                    <DropdownItem>Backup and Restore</DropdownItem>
                    <DropdownItem>Data Usage</DropdownItem>
                    <DropdownItem>Theme</DropdownItem>
                    <DropdownItem className="d-flex justify-content-between align-items-center">
                      <span className="mb-0">Notification</span>
                      <div className="form-check form-switch m-0">
                        <input
                          className="form-check-input form-check-primary"
                          type="checkbox"
                          id="notificationSwitch"
                        />
                      </div>
                    </DropdownItem>
                  </DropdownMenu>
                </Dropdown>
              </DropdownMenu>
            </UncontrolledDropdown>
            Shortcut
          </div>
        </OffcanvasHeader>

        <OffcanvasBody className="app-scroll">
          <Row className="row-cols-3">
            {linkData.map((item: MenuItem, index: number) => (
              <div className="d-flex-center text-center mb-3" key={index}>
                <a
                  href={item.href}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-decoration-none"
                >
                  <span
                    className={`text-${item.color} h-45 w-45 d-flex-center b-r-15 position-relative`}
                  >
                    <i className={`ph-duotone ${item.icon} f-s-30`}></i>
                    {item.badge && (
                      <span
                        className={`position-absolute ${item.badge.positionClass} translate-middle badge rounded-pill ${item.badge.bgColor} ${item.badge.animationClass || ""}`}
                      >
                        {item.badge.badgeText && (
                          <span>{item.badge.badgeText}</span>
                        )}
                      </span>
                    )}
                  </span>
                  <p className={`mb-0 f-w-500 text-${item.textColor}`}>
                    {item.text}
                  </p>
                </a>
              </div>
            ))}
          </Row>
        </OffcanvasBody>
      </Offcanvas>
    </>
  );
};

export default HeaderApps;
