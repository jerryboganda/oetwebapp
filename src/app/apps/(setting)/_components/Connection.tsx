import React from "react";
import { Row, Col, Card, CardBody, CardFooter, Input, Label } from "reactstrap";
import { connections } from "@/Data/Apps/Settingapp/SettingAppData";

const ConnectionSettings = () => {
  return (
    <Row>
      {connections.map(({ id, title, description, imgSrc }) => (
        <Col key={id} md="6" xxl="4" className="mb-4">
          <Card className="conection-setting h-100">
            <CardBody>
              <div className="conection-item">
                <div className="position-relative">
                  <span className="position-absolute">
                    <img alt={title} className="w-35 h-35" src={imgSrc} />
                  </span>
                  <h5 className="ms-5 mt-1">{title}</h5>
                </div>
                <div className="form-check form-switch d-flex mt-1">
                  <Input
                    type="switch"
                    role="switch"
                    className="form-check-input form-check-primary fs-3"
                    id={id}
                    defaultChecked
                  />
                  <Label className="form-check-label pt-2" htmlFor={id} />
                </div>
              </div>
              <p className="text-secondary f-s-16 mt-4">{description}</p>
            </CardBody>
            <CardFooter className="text-end text-d-underline link-primary">
              <a href="#">View integration</a>
            </CardFooter>
          </Card>
        </Col>
      ))}
    </Row>
  );
};

export default ConnectionSettings;
