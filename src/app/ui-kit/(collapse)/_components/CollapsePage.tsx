"use client";
import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Collapse,
  Container,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";

const CollapsePage = () => {
  type CollapseTypes = "basic" | "horizontal" | "multi1" | "multi2";

  const [isOpen, setIsOpen] = useState<Record<CollapseTypes, boolean>>({
    basic: false,
    horizontal: false,
    multi1: false,
    multi2: false,
  });

  const toggle = (key: CollapseTypes) => {
    setIsOpen((prev) => ({ ...prev, [key]: !prev[key] }));
  };
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Collapse"
          title="Ui Kits"
          path={["Collapse"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Basic Collapse</h5>
              </CardHeader>
              <CardBody>
                <Button color="light-primary" onClick={() => toggle("basic")}>
                  Toggle Collapse
                </Button>
                <Collapse isOpen={isOpen.basic}>
                  <CardBody className="dashed-1-secondary rounded mt-3">
                    Some placeholder content for the collapse component.
                  </CardBody>
                </Collapse>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Horizontal Collapse</h5>
              </CardHeader>
              <CardBody>
                <Button
                  color="light-primary"
                  onClick={() => toggle("horizontal")}
                >
                  Toggle width collapse
                </Button>
                <Collapse isOpen={isOpen.horizontal} horizontal>
                  <CardBody className="dashed-1-secondary rounded mt-3 w-280">
                    Horizontal collapse content.
                  </CardBody>
                </Collapse>
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Multiple Toggles and Targets</h5>
              </CardHeader>
              <CardBody>
                <p className="d-inline-flex flex-wrap gap-1">
                  <Button
                    color="light-primary"
                    onClick={() => toggle("multi1")}
                  >
                    Toggle first element
                  </Button>
                  <Button
                    color="light-primary"
                    onClick={() => toggle("multi2")}
                  >
                    Toggle second element
                  </Button>
                  <Button
                    color="light-primary"
                    onClick={() =>
                      setIsOpen((prev) => ({
                        ...prev,
                        multi1: !prev.multi1,
                        multi2: !prev.multi2,
                      }))
                    }
                  >
                    Toggle both elements
                  </Button>
                </p>
                <Row>
                  <Col xs={12} sm={6}>
                    <Collapse isOpen={isOpen.multi1}>
                      <CardBody className="dashed-1-secondary rounded mt-3">
                        First collapse content.
                      </CardBody>
                    </Collapse>
                  </Col>
                  <Col xs={12} sm={6}>
                    <Collapse isOpen={isOpen.multi2}>
                      <CardBody className="dashed-1-secondary rounded mt-3">
                        Second collapse content.
                      </CardBody>
                    </Collapse>
                  </Col>
                </Row>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default CollapsePage;
