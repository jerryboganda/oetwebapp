import React, { useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Row,
  Col,
  Label,
  Input,
  Button,
} from "reactstrap";
import {
  privacyOptions,
  userOptions,
} from "@/Data/Apps/Settingapp/SettingAppData";

const PrivacyCard = () => {
  const [privacyOption, setPrivacyOption] = useState("option1");
  const [userOption, setUserOption] = useState("useroption1");
  return (
    <>
      <Card className="equal-card privacy-card">
        <CardHeader>
          <h5>Privacy</h5>
        </CardHeader>
        <CardBody>
          <h6>ACCESS</h6>
          <Row>
            {privacyOptions.map((option) => (
              <Col key={option.id} xl="6">
                <div className="setting-privacy-card form-selectgroup">
                  <div className="select-item">
                    <Label className="form-check-label" for={option.id}>
                      <span className="d-flex align-items-center position-relative">
                        <span
                          className={`privacy-img ${option.color} h-40 w-40 d-flex-center b-r-50 position-absolute`}
                        >
                          <i className={`ph-bold ${option.icon} f-s-18`}></i>
                        </span>
                        <span className="privacy-content ms-5 me-2">
                          <span className="mb-0 text-dark txt-ellipsis-1 f-s-16 f-w-500">
                            {option.label}
                          </span>
                          <span className="mb-0 text-secondary f-s-16">
                            {option.description}
                          </span>
                        </span>
                      </span>
                    </Label>
                    <Input
                      className="form-check-input b-r-100 w-25 h-25"
                      type="radio"
                      id={option.id}
                      name="option"
                      value={option.value}
                      checked={privacyOption === option.value}
                      onChange={(e) => setPrivacyOption(e.target.value)}
                    />
                  </div>
                </div>
              </Col>
            ))}
          </Row>

          <h6>USERS</h6>
          <Row>
            {userOptions.map((option) => (
              <Col key={option.id} xl="6">
                <div className="setting-privacy-card form-selectgroup">
                  <div className="select-item">
                    <Label className="form-check-label" for={option.id}>
                      <span className="d-flex align-items-center position-relative">
                        <span
                          className={`privacy-img ${option.color} h-40 w-40 d-flex-center b-r-50 position-absolute`}
                        >
                          <i className={`ph-bold ${option.icon} f-s-18`}></i>
                        </span>
                        <span className="privacy-content ms-5 me-2">
                          <span className="mb-0 text-dark txt-ellipsis-1 f-s-16 f-w-500">
                            {option.label}
                          </span>
                          <span className="mb-0 text-secondary f-s-16">
                            {option.description}
                          </span>
                        </span>
                      </span>
                    </Label>
                    <Input
                      className="form-check-input b-r-100 w-25 h-25"
                      type="radio"
                      id={option.id}
                      name="useroption"
                      value={option.value}
                      checked={userOption === option.value}
                      onChange={(e) => setUserOption(e.target.value)}
                    />
                  </div>
                </div>
              </Col>
            ))}
          </Row>

          <div className="publishe-card mt-4">
            <h6 className="m-0">Publishing</h6>
            <p className="mb-0 f-s-16 text-secondary">
              Your project is published
            </p>
            <Button
              className="btn btn-light-primary"
              type="button"
              color={"primary"}
            >
              Unpublish
            </Button>
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default PrivacyCard;
