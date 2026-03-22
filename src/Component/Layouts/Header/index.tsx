import { Col, Container, Row } from "reactstrap";
import HeaderMenu from "@/Component/Layouts/Header/HeaderMenu";
import React from "react";
import { SidebarProps } from "@/interface/common";
import { CirclesFour } from "phosphor-react";

const Header: React.FC<SidebarProps> = ({ sidebarOpen, setSidebarOpen }) => {
  return (
    <header className="header-main">
      <Container fluid>
        <Row>
          <Col
            xs="6"
            sm="4"
            className="d-flex align-items-center header-left p-0"
          >
            <span
              className="header-toggle me-3"
              onClick={() => setSidebarOpen?.(!sidebarOpen)}
            >
              <CirclesFour size={20} />
            </span>
          </Col>
          <Col
            xs="6"
            sm="8"
            className="d-flex align-items-center justify-content-end header-right p-0"
          >
            <HeaderMenu />
          </Col>
        </Row>
      </Container>
    </header>
  );
};

export default Header;
