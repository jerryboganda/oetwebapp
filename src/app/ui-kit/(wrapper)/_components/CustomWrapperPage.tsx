import React from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import { IconLink, IconSearch } from "@tabler/icons-react";

const CustomWrapperPage = () => {
  return (
    <div>
      <div className="overlay-page">
        <Col xl="12">
          <Card>
            <CardHeader>
              <h5>Custom Overlay</h5>
            </CardHeader>
            <CardBody>
              <Row>
                <Col xs="6" sm="6" lg="3">
                  <div className="custome-wrapper position-relative">
                    <img
                      alt="Custom Overlay"
                      src="/images/wrapper/custome-overlay.jpg"
                      className="img-fluid rounded"
                    />
                    <div className="custome-wrapper-content position-absolute top-0 start-0 w-100 h-100 d-flex flex-column justify-content-center align-items-center text-white text-center">
                      <h5>Custom Overlay</h5>
                      <span>overlay design</span>
                    </div>
                    <ul className="icon">
                      <li>
                        <a
                          className="text-outline-light h-35 w-35 d-flex align-items-center justify-content-center rounded-circle border"
                          href="#"
                        >
                          <IconSearch size={18} />
                        </a>
                      </li>
                      <li>
                        <a
                          className="text-outline-light h-35 w-35 d-flex align-items-center justify-content-center rounded-circle border"
                          href="#"
                        >
                          <IconLink size={18} />
                        </a>
                      </li>
                    </ul>
                  </div>
                </Col>

                <Col xs="6" sm="6" lg="3">
                  <div className="custome-wrapper-2 position-relative overflow-hidden rounded">
                    <img
                      alt="Custom Overlay"
                      src="/images/wrapper/custome-overlay-1.jpg"
                      className="img-fluid"
                    />
                    <div className="custome-wrapper-2-content">
                      <h5>Custom Overlay</h5>
                      <p className="mb-3">
                        CSS gradients allow us to display smooth transitions
                        between two or more colors.
                      </p>
                      <span className="rounded btn btn-success btn-sm">
                        Check Now
                      </span>
                    </div>
                  </div>
                </Col>

                <Col xs="6" sm="6" lg="3">
                  <div className="custome-wrapper-3 position-relative overflow-hidden rounded">
                    <img
                      alt="Custom Overlay"
                      src="/images/wrapper/custome-overlay-2.jpg"
                      className="img-fluid"
                    />
                    <div className="custome-wrapper-content-3">
                      <h5 className="mb-1">Custom Overlay</h5>
                      <span>Overlay Design</span>
                    </div>
                  </div>
                </Col>

                <Col xs="6" sm="6" lg="3">
                  <div className="custome-wrapper-4 position-relative overflow-hidden rounded">
                    <img
                      alt="Custom Overlay"
                      src="/images/wrapper/custome-overlay-3.jpg"
                      className="img-fluid"
                    />
                    <div className="custome-wrapper-content-4">
                      <h5 className="mb-1">Custom Overlay</h5>
                      <span>Overlay Design</span>
                    </div>
                  </div>
                </Col>
              </Row>
            </CardBody>
          </Card>
        </Col>
      </div>
    </div>
  );
};

export default CustomWrapperPage;
