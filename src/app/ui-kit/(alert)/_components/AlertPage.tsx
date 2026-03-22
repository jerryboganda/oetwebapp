"use client";
import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Col, Container, Row } from "reactstrap";
import "prismjs/themes/prism.css";
import AlertBorder from "@/app/ui-kit/(alert)/_components/AlertBorder";
import BasicAlert from "@/app/ui-kit/(alert)/_components/BasicAlert";
import LightAlert from "@/app/ui-kit/(alert)/_components/LightAlert";
import OutlineAlert from "@/app/ui-kit/(alert)/_components/OutlineAlert";
import WithIcon from "@/app/ui-kit/(alert)/_components/WithIcon";
import LabelIcon from "@/app/ui-kit/(alert)/_components/LabelIcon";
import CustomAlert from "@/app/ui-kit/(alert)/_components/CustomAlert";
import AlertContent from "@/app/ui-kit/(alert)/_components/AlertContent";
import LiveAlert from "@/app/ui-kit/(alert)/_components/LiveAlert";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const AlertPage: React.FC = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Alert"
          title="Ui Kits"
          path={["Alert"]}
          Icon={IconBriefcase}
        />

        <Row>
          <PrismCodeWrapper>
            <Col lg={6}>
              <BasicAlert />
            </Col>
            <Col lg={6}>
              <LightAlert />
            </Col>
            <Col lg={6}>
              <OutlineAlert />
            </Col>
            <Col lg={6}>
              <WithIcon />
            </Col>
            <Col lg={6}>
              <AlertBorder />
            </Col>
            <Col lg={6}>
              <LabelIcon />
            </Col>
            <Col lg={6}>
              <CustomAlert />
            </Col>
            <Col lg={6}>
              <AlertContent />
            </Col>
            <Col lg={12}>
              <LiveAlert />
            </Col>
          </PrismCodeWrapper>
        </Row>
      </Container>
    </div>
  );
};

export default AlertPage;
