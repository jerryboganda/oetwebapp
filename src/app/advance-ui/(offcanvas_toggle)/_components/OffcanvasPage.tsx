"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Button,
  Offcanvas,
  OffcanvasHeader,
  OffcanvasBody,
} from "reactstrap";
import { Container, Row } from "reactstrap";
import BackdropOffcanvas from "@/app/advance-ui/(offcanvas_toggle)/_components/BackdropOffcanvas";
import PlacementOffcanvas from "@/app/advance-ui/(offcanvas_toggle)/_components/PlacementOffcanvas";
import ScrollingOffcanvas from "@/app/advance-ui/(offcanvas_toggle)/_components/ScrollingOffcanvas";
import { IconBriefcase } from "@tabler/icons-react";

const OffcanvasPage: React.FC = () => {
  const [offcanvasOpen, setOffcanvasOpen] = useState(false);
  const toggleOffcanvas = () => setOffcanvasOpen(!offcanvasOpen);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="OffCanvas Toggle"
          title="Advance Ui"
          path={["OffCanvas Toggle"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Live Demo</h5>
                <p>
                  Use the buttons below to show and hide an offcanvas element
                  via JavaScript that toggles the <code>.show</code> class on an
                  element with the{" "}
                  <span className="text-danger">.offcanvas</span> class.
                </p>
              </CardHeader>

              <CardBody>
                <Button
                  color="light-primary"
                  className="m-2"
                  onClick={toggleOffcanvas}
                >
                  Link with href
                </Button>

                <Button
                  color="light-primary"
                  className="m-2"
                  onClick={toggleOffcanvas}
                >
                  Button with data-bs-target
                </Button>

                {/* Offcanvas */}
                <Offcanvas
                  direction="start"
                  isOpen={offcanvasOpen}
                  toggle={toggleOffcanvas}
                >
                  <OffcanvasHeader toggle={toggleOffcanvas}>
                    Offcanvas
                  </OffcanvasHeader>
                  <OffcanvasBody>
                    <p>
                      Some text as placeholder. In real life you can have the
                      elements you have chosen. Like, text, images, lists, etc.
                    </p>
                  </OffcanvasBody>
                </Offcanvas>
              </CardBody>
            </Card>
          </Col>
          <PlacementOffcanvas />
          <BackdropOffcanvas />
          <ScrollingOffcanvas />
        </Row>
      </Container>
    </div>
  );
};

export default OffcanvasPage;
