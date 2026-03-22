import React from "react";
import { Card, CardBody, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";

const TOTAL_LOADERS = 40;

const SpinnersPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Spinners"
          title="Advance Ui"
          path={["Spinners"]}
          Icon={IconBriefcase}
        />

        <Row>
          <Col xs="12">
            <Card>
              <CardBody>
                <div className="loader-container">
                  {Array.from({ length: TOTAL_LOADERS }, (_, i) => (
                    <div className="loader-main" key={i}>
                      <div className="loader_box">
                        <div className={`loader_${i + 1}`}></div>
                      </div>
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default SpinnersPage;
