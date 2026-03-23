"use client";

import React, { useEffect, useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Toastify from "toastify-js";
import "toastify-js/src/toastify.css";
import { IconIcons } from "@tabler/icons-react";
import iconsList from "@tabler/icons-react/dist/esm/icons-list.mjs";

const size = 50;
const color = "#000";

const toComponentName = (iconName: string) =>
  `Icon${iconName
    .split("-")
    .filter(Boolean)
    .map((segment) =>
      /^\d/.test(segment)
        ? segment.toUpperCase()
        : `${segment.charAt(0).toUpperCase()}${segment.slice(1)}`
    )
    .join("")}`;

const TablerIconsComponent: React.FC = () => {
  const [iconList, setIconList] = useState(iconsList);
  const [searchValue, setSearchValue] = useState("");

  useEffect(() => {
    if (searchValue.trim() === "") {
      setIconList(iconsList);
    } else {
      const filteredIcons = iconsList.filter((iconName) =>
        iconName.toLowerCase().includes(searchValue.toLowerCase())
      );
      setIconList(filteredIcons);
    }
  }, [searchValue]);

  const copyIcon = (iconName: string) => {
    const iconTag = `<${toComponentName(iconName)} size={${size}} color="${color}" />`;
    navigator.clipboard.writeText(iconTag);
    Toastify({
      text: "Copied to the clipboard successfully",
      duration: 3000,
      close: true,
      gravity: "top",
      position: "right",
      stopOnFocus: true,
      style: {
        background: "rgba(var(--success),1)",
      },
    }).showToast();
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Tabler"
        title="Icons"
        path={["Tabler"]}
        Icon={IconIcons}
      />
      <Row>
        <Col xs="12">
          <Card>
            <CardHeader>
              <Row>
                <Col md="4">
                  <div className="search-bar app-form app-icon-form position-relative">
                    <input
                      type="search"
                      className="form-control"
                      name="search"
                      placeholder="Type to search"
                      value={searchValue}
                      onChange={(e) => setSearchValue(e.target.value)}
                    />
                  </div>
                </Col>
                <div className="col-md-8 text-end pt-2" />
              </Row>
            </CardHeader>
            <CardBody>
              <ul className="icon-list space-top-icon">
                {iconList.map((iconName, index) => {
                  const componentName = toComponentName(iconName);

                  return (
                    <li
                      className="icon-box"
                      onClick={() => copyIcon(iconName)}
                      key={index}
                    >
                      <i
                        className={`ti ti-${iconName}`}
                        style={{ fontSize: `${size}px`, color }}
                        aria-hidden="true"
                      />
                      <div className="icon-box-codes d-flex flex-column">
                        <strong className="text-capitalize">
                          {componentName}
                        </strong>
                        <code>{iconName}</code>
                      </div>
                    </li>
                  );
                })}
              </ul>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default TablerIconsComponent;
