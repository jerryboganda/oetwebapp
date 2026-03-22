import React, { ChangeEvent, useState } from "react";
import {
  Card,
  CardHeader,
  CardBody,
  Row,
  Col,
  FormGroup,
  Label,
  Form,
  Input,
} from "reactstrap";
import { Desktop, DeviceMobileSpeaker, Watch } from "phosphor-react";

const NotificationSettings = () => {
  const [emailSettings, setEmailSettings] = useState({
    comments: false,
    candidates: false,
    offers: false,
  });

  const [pushNotification, setPushNotification] = useState("everything");
  const [mobilePush, setMobilePush] = useState(true);
  const [desktopPush, setDesktopPush] = useState(true);
  const [smartwatchPush, setSmartwatchPush] = useState(true);

  const handleEmailChange = (e: ChangeEvent<HTMLInputElement>) => {
    const { id, checked } = e.target;
    setEmailSettings((prev) => ({
      ...prev,
      [id]: checked,
    }));
  };

  const handlePushNotificationChange = (e: ChangeEvent<HTMLInputElement>) => {
    setPushNotification(e.target.value);
  };

  const handleSwitchChange = (type: string) => {
    if (type === "mobile") setMobilePush(!mobilePush);
    if (type === "desktop") setDesktopPush(!desktopPush);
    if (type === "smartwatch") setSmartwatchPush(!smartwatchPush);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Notification</h5>
        </CardHeader>
        <CardBody>
          <div className="notification-content">
            <Row>
              {/* Email Notification */}
              <Col lg="6">
                <div className="notification-email">
                  <h6>By Email</h6>
                  <Form>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="checkbox"
                          id="comments"
                          checked={emailSettings.comments}
                          onChange={handleEmailChange}
                        />
                        Comments - notified posts on comment
                      </Label>
                    </FormGroup>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="checkbox"
                          id="candidates"
                          checked={emailSettings.candidates}
                          onChange={handleEmailChange}
                        />
                        Candidates - notified candidate applies
                      </Label>
                    </FormGroup>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="checkbox"
                          id="offers"
                          checked={emailSettings.offers}
                          onChange={handleEmailChange}
                        />
                        Offers - notified accepts or rejects
                      </Label>
                    </FormGroup>
                  </Form>
                </div>
              </Col>

              {/* Push Notification */}
              <Col lg="6">
                <div className="notification-push">
                  <h6 className="mb-0">Push Notification</h6>
                  <p className="mb-0">
                    These are delivered via SMS to your mobile phone.
                  </p>

                  {/* Radio buttons for push notification preferences */}
                  <Form>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="radio"
                          name="pushNotification"
                          value="everything"
                          checked={pushNotification === "everything"}
                          onChange={handlePushNotificationChange}
                        />
                        Everything
                      </Label>
                    </FormGroup>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="radio"
                          name="pushNotification"
                          value="sameAsEmail"
                          checked={pushNotification === "sameAsEmail"}
                          onChange={handlePushNotificationChange}
                        />
                        Same as email
                      </Label>
                    </FormGroup>
                    <FormGroup check>
                      <Label check>
                        <Input
                          type="radio"
                          name="pushNotification"
                          value="noPush"
                          checked={pushNotification === "noPush"}
                          onChange={handlePushNotificationChange}
                        />
                        No push notification
                      </Label>
                    </FormGroup>
                  </Form>
                </div>
              </Col>
            </Row>

            {/* Divider */}
            <hr />

            {/* Push Notification Devices List */}
            <Row>
              <Col md="12">
                <ul className="share-menu-list">
                  <li>
                    <div className="share-menu-item mb-4">
                      <span className="share-menu-img text-outline-primary h-45 w-45 d-flex-center b-r-10">
                        <DeviceMobileSpeaker
                          weight="bold"
                          size={30}
                          className="f-s-30"
                        />
                      </span>
                      <div className="share-menu-content">
                        <h6 className="mb-0">Mobile push notification</h6>
                        <p className="mb-0 text-secondary">
                          Receive all notifications via your mobile app
                        </p>
                      </div>
                      <div className="form-check form-switch d-flex mt-1">
                        <Input
                          type="switch"
                          id="mobilePush"
                          checked={mobilePush}
                          onChange={() => handleSwitchChange("mobile")}
                        />
                      </div>
                    </div>
                  </li>

                  <li>
                    <div className="share-menu-item mb-4">
                      <span className="share-menu-img text-outline-success h-45 w-45 d-flex-center b-r-10">
                        <Desktop weight="bold" size={30} className="f-s-30" />
                      </span>
                      <div className="share-menu-content">
                        <h6 className="mb-0">Desktop push notification</h6>
                        <p className="mb-0 text-secondary">
                          Receive all notifications via your desktop app
                        </p>
                      </div>
                      <div className="form-check form-switch d-flex mt-1">
                        <Input
                          type="switch"
                          id="desktopPush"
                          checked={desktopPush}
                          onChange={() => handleSwitchChange("desktop")}
                        />
                      </div>
                    </div>
                  </li>

                  <li>
                    <div className="share-menu-item mb-4">
                      <span className="share-menu-img text-outline-danger h-45 w-45 d-flex-center b-r-10">
                        <Watch weight="bold" size={30} className="f-s-30" />
                      </span>
                      <div className="share-menu-content">
                        <h6 className="mb-0">Smartwatch push notification</h6>
                        <p className="mb-0 text-secondary">
                          Receive all notifications via your smartwatch app
                        </p>
                      </div>
                      <div className="form-check form-switch d-flex mt-1">
                        <Input
                          type="switch"
                          id="smartwatchPush"
                          checked={smartwatchPush}
                          onChange={() => handleSwitchChange("smartwatch")}
                        />
                      </div>
                    </div>
                  </li>
                </ul>
              </Col>
            </Row>
          </div>
        </CardBody>
      </Card>
    </>
  );
};

export default NotificationSettings;
