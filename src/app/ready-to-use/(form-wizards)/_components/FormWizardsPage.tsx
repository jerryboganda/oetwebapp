"use client";
import React from "react";
import { Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import NumberWizard from "@/app/ready-to-use/(form-wizards)/_components/NumberWizard";
import IconWizards from "@/app/ready-to-use/(form-wizards)/_components/IconWizards";
import VerticalNumberWizard from "@/app/ready-to-use/(form-wizards)/_components/VerticalNumberWizard";
import VerticalIconWizards from "@/app/ready-to-use/(form-wizards)/_components/VerticalIconWizards";
import { IconHeartHandshake } from "@tabler/icons-react";

const FormWizardsPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Form Wizards"
          title="Ready to use"
          path={["Form wizards"]}
          Icon={IconHeartHandshake}
        />

        <Row>
          <Col xs={12} lg={6}>
            <NumberWizard />
          </Col>
          <Col xs={12} lg={6}>
            <IconWizards />
          </Col>
          <Col xs={12} lg={6}>
            <VerticalNumberWizard />
          </Col>
          <Col xs={12} lg={6}>
            <VerticalIconWizards />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default FormWizardsPage;
