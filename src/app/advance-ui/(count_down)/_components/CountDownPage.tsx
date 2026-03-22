import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";
import CountdownTimer from "./CountdownTimer";

export default function CountDownPage() {
  const targetDate = "December 31, 2025 23:59:59";

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="count-down"
        title="Advance Ui"
        path={["count-down"]}
        Icon={IconBriefcase}
      />

      <Row>
        <Col md={6}>
          <Card>
            <CardHeader>
              <h5>With Seconds</h5>
              <p>
                You can add only seconds countdown using
                <span className="text-danger"> countdown-seconds </span>
                class
              </p>
            </CardHeader>
            <CardBody>
              <div className="countdown-seconds">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={false}
                  showCountDownBg={false}
                  showDays={false}
                  showHours={false}
                  showMinutes={false}
                  showSeconds={true}
                />
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col md={6}>
          <Card className="equal-card">
            <CardHeader>
              <h5>With minutes</h5>
              <p>
                You can add minutes countdown style using{" "}
                <span className="text-danger"> app-countdown-min </span>
                class.
              </p>
            </CardHeader>
            <CardBody>
              <div className="app-countdown-min">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={false}
                  showCountDownBg={false}
                  showDays={false}
                  showHours={false}
                  showMinutes={true}
                  showSeconds={false}
                />
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col xl={6}>
          <Card className="equal-card">
            <CardHeader>
              <h5>With Hours</h5>
              <p>
                You can add hours countdown using{" "}
                <span className="text-danger">app-countdown-hours</span> class.
              </p>
            </CardHeader>
            <CardBody>
              <div className="app-countdown-hours">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={false}
                  showCountDownBg={false}
                  showDays={false}
                  showHours={true}
                  showMinutes={false}
                  showSeconds={false}
                />
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col lg={6}>
          <Card>
            <CardHeader>
              <h5>Countdown Styles</h5>
              <p>
                You can add countdown using{" "}
                <span className="text-danger">app-countdown-background</span>{" "}
                class.
              </p>
            </CardHeader>
            <CardBody>
              <div className="app-countdown-background">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={false}
                  showCountDownBg={false}
                  showDays={true}
                  showHours={false}
                  showMinutes={false}
                  showSeconds={false}
                />
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col xl={6}>
          <Card>
            <CardHeader>
              <h5>Countdown Styles</h5>
              <p>
                You can add countdown using{" "}
                <span className="text-danger">app-countdown-circle</span> class.
              </p>
            </CardHeader>
            <CardBody>
              <div className="app-countdown-circle">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={true}
                  showCountDownBg={false}
                  showDays={false}
                  showHours={false}
                  showMinutes={false}
                  showSeconds={false}
                />
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col lg={6}>
          <Card className="equal-card">
            <CardHeader>
              <h5>Background Countdown</h5>
              <p>
                You can add countdown with background using
                <span className="text-danger"> app-countdown-square</span>{" "}
                class.
              </p>
            </CardHeader>
            <div className="card-body d-flex-center">
              <div className="app-countdown-square">
                <CountdownTimer
                  targetDate={targetDate}
                  showCountDownCircle={false}
                  showCountDownBg={true}
                  showDays={false}
                  showHours={false}
                  showMinutes={false}
                  showSeconds={false}
                />
              </div>
            </div>
          </Card>
        </Col>
      </Row>
    </Container>
  );
}
