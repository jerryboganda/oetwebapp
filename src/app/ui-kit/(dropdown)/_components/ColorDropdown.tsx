import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import {
  IconCirclesRelation,
  IconDots,
  IconMessageCircle,
  IconSettings,
  IconUserPlus,
} from "@tabler/icons-react";

interface DropdownItem {
  label: string;
  icon: React.ReactNode;
}
interface DropdownState {
  [key: string]: boolean;
}

const dropdownItems: DropdownItem[] = [
  { label: "Action", icon: <IconUserPlus size={18} /> },
  { label: "Another action", icon: <IconCirclesRelation size={18} /> },
  { label: "Something else here", icon: <IconMessageCircle size={18} /> },
  { label: "Settings", icon: <IconSettings size={18} /> },
];

// All color variants
const menuColors: string[] = [
  "primary",
  "secondary",
  "success",
  "danger",
  "warning",
  "info",
  "dark-light",
  "dark",
];

const ColorDropdown: React.FC = () => {
  const [dropdownOpen, setDropdownOpen] = useState<DropdownState>({});

  const toggleDropdown = (color: string) => {
    setDropdownOpen((prev) => ({
      ...prev,
      [color]: !prev[color],
    }));
  };

  return (
    <Col xs={12}>
      <Card>
        <CardHeader>
          <h5>Color Dropdown Menu</h5>
        </CardHeader>
        <CardBody>
          <Row>
            {menuColors.map((color, index) => (
              <Col md={6} xl={4} key={index}>
                <div className="app-dropdown mb-3">
                  <button
                    className="btn border-0 icon-btn"
                    type="button"
                    onClick={() => toggleDropdown(color)}
                    aria-expanded={dropdownOpen[color]}
                  >
                    <IconDots size={18} />
                  </button>
                  <ul
                    className={`dropdown-menu menu-${color} ${dropdownOpen[color] ? "show" : ""}`}
                  >
                    {dropdownItems.slice(0, 3).map((item, idx) => (
                      <li
                        className="dropdown-item d-flex justify-content-between"
                        key={idx}
                      >
                        <span>{item.label}</span>
                        {item.icon}
                      </li>
                    ))}
                    <li className="dropdown-divider"></li>
                    <li className="dropdown-item d-flex justify-content-between">
                      <span>{dropdownItems[3]?.label}</span>
                      {dropdownItems[3]?.icon}
                    </li>
                  </ul>
                </div>
              </Col>
            ))}
          </Row>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ColorDropdown;
