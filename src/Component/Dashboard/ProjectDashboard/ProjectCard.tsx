import React, { useEffect, useState } from "react";
import { Badge, Card, CardBody, Col, Row, Tooltip } from "reactstrap";

const ProjectCard = () => {
  const [time, setTime] = useState({
    hour: 0,
    minutes: 0,
    seconds: 0,
  });
  const [tooltipOpen, setTooltipOpen] = useState<{ [key: string]: boolean }>(
    {}
  );
  const toggleTooltip = (id: string) => {
    setTooltipOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  useEffect(() => {
    const updateClock = () => {
      const now = new Date();
      const hour = now.getHours();
      const minutes = now.getMinutes();
      const seconds = now.getSeconds();

      setTime({ hour, minutes, seconds });
    };

    const intervalId = setInterval(updateClock, 1000);
    updateClock(); // Initialize immediately

    return () => clearInterval(intervalId); // Cleanup on component unmount
  }, []);

  const { hour, minutes, seconds } = time;

  // Calculate rotation for clock hands
  const hourDeg = (hour % 12) * 30 + minutes * 0.5;
  const minuteDeg = minutes * 6;
  const secondDeg = seconds * 6;
  return (
    <Col lg="6" xxl="4">
      <Row>
        <Col sm={6}>
          <div className="card project-total-card">
            <CardBody>
              <div className="d-flex position-relative">
                <h5 className="text-dark txt-ellipsis-1">Total Hours</h5>
                <div className="clock-box">
                  <div className="clock">
                    <div
                      className="hour"
                      style={{
                        transform: `translate(-50%, -100%) rotate(${hourDeg}deg)`,
                      }}
                    ></div>
                    <div
                      className="min"
                      style={{
                        transform: `translate(-50%, -100%) rotate(${minuteDeg}deg)`,
                      }}
                    ></div>
                    <div
                      className="sec"
                      style={{
                        transform: `translate(-50%, -100%) rotate(${secondDeg}deg)`,
                      }}
                    ></div>
                  </div>
                </div>
              </div>
              <div>
                <div className="d-flex">
                  <h2 className="text-info-dark hour-display">{`${hour}H`}</h2>
                </div>
                <div className="progress-labels mg-t-40">
                  <span className="text-info">Productive</span>
                  <span className="text-info">Middle</span>
                  <span className="text-info">Idle</span>
                </div>
                <div className="custom-progress-container info-progress">
                  <div className="progress-bar productive"></div>
                  <div className="progress-bar middle"></div>
                  <div className="progress-bar idle"></div>
                </div>
              </div>
            </CardBody>
          </div>
        </Col>

        <Col sm="6">
          <Card className="bg-info-300 project-details-card">
            <CardBody>
              <div className="d-flex gap-2">
                <span className="badge bg-white-300 text-info-dark p-1  b-r-10">
                  📱 Mobile app
                </span>
                <span className="badge dashed-1-info text-info-dark ms-2 p-1  b-r-10">
                  Marketing
                </span>
              </div>
              <div className="my-4">
                <h5 className="f-w-600 text-info-dark txt-ellipsis-1">
                  Project Alpha
                </h5>
                <p className="text-info f-s-13 txt-ellipsis-1 mb-0">
                  Revolutionizing ideas, empowering innovation, and driving
                  success.
                </p>
              </div>
              <div className="d-flex align-items-center justify-content-between pt-3">
                <ul className="avatar-group">
                  <li className="h-35 w-35 d-flex-center b-r-50 overflow-hidden bg-light-primary b-2-primary">
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/4.png"
                    />
                  </li>
                  <li className="h-35 w-35 d-flex-center b-r-50 overflow-hidden bg-light-success b-2-success">
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/5.png"
                    />
                  </li>
                  <li
                    className="h-35 w-35 d-flex-center b-r-50 overflow-hidden bg-light-info b-2-info"
                    id="tooltip-maya"
                  >
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/6.png"
                    />
                    <Tooltip
                      target="tooltip-maya"
                      isOpen={tooltipOpen["maya"] || false}
                      toggle={() => toggleTooltip("maya")}
                    >
                      Maya Horton
                    </Tooltip>
                  </li>
                </ul>
                <Badge className="bg-white-300 text-info-dark ms-2">
                  🔥 1H left
                </Badge>
              </div>
            </CardBody>
          </Card>
        </Col>

        <Col sm="6">
          <Card className="bg-success-300 project-details-card">
            <CardBody>
              <div className="d-flex gap-2">
                <Badge className="bg-white-300 text-success-dark p-1 b-r-10">
                  ⚡ API
                </Badge>
                <Badge
                  color="green"
                  className="bg-transparent dashed-1-dark-light text-success-dark ms-2 p-1 b-r-10"
                >
                  Web Development
                </Badge>
              </div>
              <div className="my-4">
                <h5 className="f-w-600 text-success-dark txt-ellipsis-1">
                  Project Beta
                </h5>
                <p className="text-success f-s-13 txt-ellipsis-1 mb-0">
                  Innovating solutions for seamless task management efficiency.
                </p>
              </div>
              <div className="d-flex align-items-center justify-content-between pt-3">
                <ul className="avatar-group">
                  <li className="h-35 w-35 d-flex-center b-r-50 overflow-hidden bg-light-primary b-2-primary">
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/4.png"
                    />
                  </li>
                  <li
                    className="h-35 w-35 d-flex-center b-r-50 overflow-hidden bg-light-danger b-2-danger"
                    id="tooltip-maya-horton"
                  >
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/6.png"
                    />
                  </li>
                  <Tooltip
                    target="tooltip-maya-horton"
                    isOpen={tooltipOpen["maya-horton"] || false}
                    toggle={() => toggleTooltip("maya-horton")}
                  >
                    Maya Horton
                  </Tooltip>
                </ul>
                <Badge className="bg-white-300 text-success-dark ms-2">
                  ✨ 2D left
                </Badge>
              </div>
            </CardBody>
          </Card>
        </Col>

        <Col sm="6">
          <Card className="core-teams-card">
            <CardBody>
              <div className="d-flex">
                <h5 className="text-dark f-w-600 txt-ellipsis-1">
                  💼 Core Teams
                </h5>
              </div>
              <div>
                <h2 className="text-warning-dark my-4 d-inline-flex align-items-baseline">
                  1k<span className="f-s-12 text-dark"> Team Members</span>
                </h2>
                <ul className="avatar-group justify-content-start">
                  <li
                    className="h-35 w-35 d-flex-center b-r-50 overflow-hidden text-bg-primary b-2-light"
                    id="tooltip-sabrina"
                  >
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/4.png"
                    />
                    <Tooltip
                      target="tooltip-sabrina"
                      isOpen={tooltipOpen["sabrina"] || false}
                      toggle={() => toggleTooltip("sabrina")}
                    >
                      Sabrina Torres
                    </Tooltip>
                  </li>
                  <li
                    className="h-35 w-35 d-flex-center b-r-50 overflow-hidden text-bg-success b-2-light"
                    id="tooltip-eva"
                  >
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/5.png"
                    />
                    <Tooltip
                      target="tooltip-eva"
                      isOpen={tooltipOpen["eva"] || false}
                      toggle={() => toggleTooltip("eva")}
                    >
                      Eva Bailey
                    </Tooltip>
                  </li>
                  <li
                    className="h-35 w-35 d-flex-center b-r-50 overflow-hidden text-bg-danger b-2-light"
                    id="tooltip-michael"
                  >
                    <img
                      alt="avatar"
                      className="img-fluid"
                      src="/images/avatar/6.png"
                    />
                    <Tooltip
                      target="tooltip-michael"
                      isOpen={tooltipOpen["michael"] || false}
                      toggle={() => toggleTooltip("michael")}
                    >
                      Michael Hughes
                    </Tooltip>
                  </li>
                  <li
                    id="tooltip-more"
                    className="text-bg-secondary h-35 w-35 d-flex-center b-r-50"
                  >
                    10+
                  </li>
                  <Tooltip
                    target="tooltip-more"
                    isOpen={tooltipOpen["more"] || false}
                    toggle={() => toggleTooltip("more")}
                  >
                    10+
                  </Tooltip>
                </ul>
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Col>
  );
};

export default ProjectCard;
