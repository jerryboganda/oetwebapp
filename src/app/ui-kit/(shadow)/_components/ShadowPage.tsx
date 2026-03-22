"use client";
import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Toastify from "toastify-js";
import "toastify-js/src/toastify.css";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";

const ShadowPage = () => {
  const boxShadows = Array.from({ length: 24 }, (_, i) => i + 1);

  const themedShadows = [
    { name: "Primary", className: "box-shadow-primary" },
    { name: "Secondary", className: "box-shadow-secondary" },
    { name: "Success", className: "box-shadow-success" },
    { name: "Danger", className: "box-shadow-danger" },
    { name: "Warning", className: "box-shadow-warning text-dark" },
    { name: "Info", className: "box-shadow-info" },
    { name: "Light", className: "box-shadow-light text-dark" },
    { name: "Dark", className: "box-shadow-dark" },
  ];

  const handleClick = (classList: string) => {
    const classToCopy = classList
      .split(" ")
      .find(
        (cls) =>
          cls.startsWith("box-shadow-") && !cls.includes("box-shadow-box")
      );

    if (classToCopy) {
      navigator.clipboard.writeText(classToCopy);
      Toastify({
        text: "Class name copied to the clipboard successfully",
        duration: 3000,
        close: true,
        gravity: "top",
        position: "right",
        stopOnFocus: true,
        style: { background: "rgba(var(--success),1)" },
      }).showToast();
    }
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Shadow"
          title="Ui Kits"
          path={["Shadow"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Box Shadow</h5>
              </CardHeader>
              <CardBody>
                <Container fluid>
                  <Row>
                    {boxShadows.map((num) => (
                      <Col key={num} sm={4} lg={3} xxl={2} className="mb-4">
                        <div
                          onClick={(e) =>
                            handleClick(e.currentTarget.className)
                          }
                          className={`box-shadow-box box-shadow-${num} f-w-500`}
                        >
                          Box-shadow-{num}
                        </div>
                      </Col>
                    ))}
                  </Row>
                </Container>
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Box Colour Shadow</h5>
              </CardHeader>
              <CardBody>
                <div className="container-fluid shadow-box">
                  <Row>
                    {themedShadows.map(({ name, className }) => (
                      <Col key={name} sm={3} lg={2} className="mb-4">
                        <div
                          onClick={(e) =>
                            handleClick(e.currentTarget.className)
                          }
                          className={`box-shadow-box box-shadow-25 ${className} f-w-500`}
                        >
                          {name}
                        </div>
                      </Col>
                    ))}
                  </Row>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ShadowPage;
