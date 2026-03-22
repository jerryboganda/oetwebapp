import React, { useState } from "react";
import {
  Row,
  Col,
  Card,
  CardBody,
  Button,
  Badge,
  Label,
  Input,
  InputGroup,
  InputGroupText,
} from "reactstrap";
import Image from "next/image";
import {
  leftSessionList,
  rightSessionList,
  securityItems,
} from "@/Data/Apps/Settingapp/SettingAppData";
import { IconCircleXFilled } from "@tabler/icons-react";

type PasswordField = {
  id: "current" | "new" | "confirm";
  label: string;
  placeholder: string;
};

const fields: PasswordField[] = [
  {
    id: "current",
    label: "Current Password",
    placeholder: "********",
  },
  {
    id: "new",
    label: "New Password",
    placeholder: "********",
  },
  {
    id: "confirm",
    label: "Confirm Password",
    placeholder: "********",
  },
];
const SecurityCard = () => {
  const [visibility, setVisibility] = useState<
    Record<PasswordField["id"], boolean>
  >({
    current: false,
    new: false,
    confirm: false,
  });

  const toggleVisibility = (key: PasswordField["id"]) => {
    setVisibility((prev) => ({ ...prev, [key]: !prev[key] }));
  };
  return (
    <div>
      {/* Static: Account Security */}
      <Card className="security-card-content mb-4">
        <CardBody>
          <Row className="align-items-center">
            <Col sm="8">
              <h5 className="text-primary fw-semibold">Account Security</h5>
              <p className="text-secondary fs-6 mt-2 mb-0">
                Your account is valuable to hackers. To make 2-step verification
                very secure, use your phone&#39;s built-in security key.
              </p>
            </Col>
            <Col sm="4" className="text-end">
              <img
                alt="Account"
                className="w-100"
                src="/images/setting/account.png"
              />
            </Col>
          </Row>
        </CardBody>
      </Card>

      {/* Mapped Security Items */}
      {securityItems.map((item, idx) => (
        <Card className="mb-4" key={idx}>
          <CardBody>
            <Row className="security-box-card align-items-center">
              <Col md="3" className="position-relative">
                <span className="anti-code">{item.icon}</span>
                <p className="security-box-title text-dark f-w-500 f-s-16 ms-5 security-code">
                  {item.title}
                </p>
              </Col>
              <Col md="6" className="security-discription">
                <p className="text-secondary fs-6 mb-2">{item.description}</p>
                {item.badge && (
                  <Badge
                    color={item.badge.color}
                    className="text-secondary p-2"
                  >
                    {item.badge.icon}
                    {item.badge.text}
                  </Badge>
                )}
              </Col>
              <Col md="3" className="text-end">
                {item.button ? (
                  <Button color={item.button.color}>{item.button.text}</Button>
                ) : (
                  item.rightText && <p>{item.rightText}</p>
                )}
              </Col>
            </Row>
          </CardBody>
        </Card>
      ))}

      {/* Static: Devices and Sessions */}
      <Card className="security-card-content mb-4">
        <CardBody>
          <Row className="align-items-center">
            <Col sm="9">
              <h5 className="text-primary fw-semibold">
                Devices and active sessions
              </h5>
              <p className="text-secondary fs-6 mt-3">
                Your account is valuable to hackers. To make 2-step verification
                very secure, use your phone&#39;s built-in security key.
              </p>
            </Col>
            <Col sm="3" className="text-end">
              <img
                alt="Device"
                className="w-100"
                src="/images/setting/device.png"
              />
            </Col>
          </Row>
        </CardBody>
      </Card>

      <Row>
        <Col lg="12" xxl="6">
          <ul
            className="active-device-session active-device-list"
            id="shareMenuLeft"
          >
            {leftSessionList.map((item, idx) => (
              <li key={idx}>
                <Card className={idx === 0 ? "share-menu-active" : ""}>
                  <CardBody>
                    <div className="device-menu-item" draggable={false}>
                      <span className="device-menu-img">
                        <i
                          className={`ph-duotone ${item.iconClass} f-s-40 text-${item.iconColor}`}
                        ></i>
                      </span>
                      <div className="device-menu-content">
                        <h6 className="mb-0 txt-ellipsis-1">{item.name}</h6>
                        <p className="mb-0 txt-ellipsis-1 text-secondary">
                          {item.location}
                        </p>
                      </div>
                      <div className="device-menu-icons">
                        <Badge
                          color="light-secondary"
                          className="p-2 f-s-16 text-secondary"
                        >
                          {item.status === "online" ? (
                            <IconCircleXFilled
                              size={16}
                              className="me-1 text-success"
                            />
                          ) : (
                            <IconCircleXFilled
                              size={16}
                              className="me-1 text-primary"
                            />
                          )}
                          {item.status === "online" ? "Online" : "Offline"}
                        </Badge>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </li>
            ))}
          </ul>
        </Col>
        <Col lg="12" xxl="6">
          <ul
            className="active-device-session active-device-list"
            id="shareMenuRight"
          >
            {rightSessionList.map((item, idx) => (
              <li key={idx}>
                <Card>
                  <CardBody>
                    <div className="device-menu-item" draggable={false}>
                      <span className="device-menu-img">
                        <i
                          className={`ph-duotone ${item.iconClass} f-s-40 text-${item.iconColor}`}
                        ></i>
                      </span>
                      <div className="device-menu-content">
                        <h6 className="mb-0 txt-ellipsis-1">{item.name}</h6>
                        <p className="mb-0 txt-ellipsis-1 text-secondary">
                          {item.location}
                        </p>
                      </div>
                      <div className="device-menu-icons">
                        <Badge
                          color="light-secondary"
                          className="p-2 f-s-16 text-secondary"
                        >
                          {item.status === "online" ? (
                            <IconCircleXFilled
                              size={16}
                              className="me-1 text-success"
                            />
                          ) : (
                            <IconCircleXFilled
                              size={16}
                              className="me-1 text-primary"
                            />
                          )}
                          {item.status === "online" ? "Online" : "Offline"}
                        </Badge>
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </li>
            ))}
          </ul>
        </Col>
      </Row>

      <Card className="security-card-content">
        <CardBody>
          <div className="account-security mb-2">
            <Row className="align-items-center">
              <Col sm="9">
                <h5 className="text-primary fw-semibold">Change Password</h5>
                <p className="account-discription text-secondary fs-6 mt-3">
                  To change your password, please fill in the fields below. Your
                  password must contain at least 8 characters and include at
                  least one uppercase letter, one lowercase letter, one number,
                  and one special character.
                </p>
              </Col>
              <Col sm="3" className="account-security-img">
                <Image
                  src="/images/setting/password.png"
                  alt="Password Illustration"
                  width={150}
                  height={150}
                  className="w-150"
                />
              </Col>
            </Row>
          </div>

          <div className="app-form">
            <Row>
              {fields.map(({ id, label, placeholder }) => (
                <Col sm="12" className="mb-3" key={id}>
                  <Label for={`${id}Password`} className="form-label">
                    {label}
                  </Label>
                  <InputGroup className="input-group-password">
                    <InputGroupText className="b-r-left">
                      <i className="ph-bold ph-lock f-s-20" />
                    </InputGroupText>
                    <Input
                      id={`${id}Password`}
                      type={visibility[id] ? "text" : "password"}
                      placeholder={placeholder}
                    />
                    <InputGroupText
                      className="b-r-right cursor-pointer"
                      onClick={() => toggleVisibility(id)}
                    >
                      <i
                        className={`ph f-s-20 eyes-icon${
                          id === "current" ? "" : id + "1"
                        } ${visibility[id] ? "ph-eye" : "ph-eye-slash"}`}
                      />
                    </InputGroupText>
                  </InputGroup>
                </Col>
              ))}
            </Row>
          </div>
        </CardBody>
      </Card>
    </div>
  );
};

export default SecurityCard;
