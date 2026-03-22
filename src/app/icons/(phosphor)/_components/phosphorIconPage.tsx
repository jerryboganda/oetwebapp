"use client";
import React, { useState } from "react";
import * as PhosphorIcons from "phosphor-react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Toastify from "toastify-js";
import "toastify-js/src/toastify.css";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconIcons } from "@tabler/icons-react";

const PhosphorIconPage: React.FC = () => {
  const [searchValue, setSearchValue] = useState<string>("");

  const iconEntries = Object.entries(PhosphorIcons).filter(
    ([name]) => !name.toLowerCase().includes("context")
  ) as [string, React.FC<PhosphorIcons.IconProps>][];

  const filteredIcons = iconEntries.filter(([name]) =>
    name.toLowerCase().includes(searchValue.toLowerCase())
  );

  const copyIconCode = (iconName: string) => {
    navigator.clipboard.writeText(`<${iconName} />`);
    Toastify({
      text: "Copied to the clipboard successfully",
      duration: 3000,
      close: true,
      gravity: "top",
      position: "right",
      stopOnFocus: true,
      style: { background: "rgba(var(--success),1)" },
    }).showToast();
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Phosphor"
          title="Icons"
          path={["Phosphor"]}
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
                        placeholder="Type to search"
                        onChange={(e) => setSearchValue(e.target.value)}
                      />
                    </div>
                  </Col>
                </Row>
              </CardHeader>
              <CardBody>
                <ul className="icon-list space-top-icon">
                  {filteredIcons.map(([name, IconComponent]) => (
                    <li
                      className="icon-box"
                      key={name}
                      onClick={() => copyIconCode(name)}
                    >
                      <IconComponent size={30} />
                      <strong>{name}</strong>
                    </li>
                  ))}
                </ul>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default PhosphorIconPage;
