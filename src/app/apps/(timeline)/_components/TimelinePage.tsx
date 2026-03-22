import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Col, Container, Row } from "reactstrap";
import TimeLine1 from "@/app/apps/(timeline)/_components/TimeLine1";
import TimeLine2 from "@/app/apps/(timeline)/_components/TimeLine2";
import TimeLine3 from "@/app/apps/(timeline)/_components/TimeLine3";
import { IconStack2 } from "@tabler/icons-react";

const TimelinePage: React.FC = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Timeline"
          title="Apps"
          path={["Timeline"]}
          Icon={IconStack2}
        />
        <Row className="row">
          <Col lg={6}>
            <TimeLine1 />
          </Col>

          <Col lg={6}>
            <TimeLine2 />
          </Col>

          <Col xs={12}>
            <TimeLine3 />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default TimelinePage;
