import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Row,
  ToastBody,
  ToastHeader,
} from "reactstrap";
import { Toast } from "reactstrap";

const toastData = [
  {
    title: "Default Toast",
    message: "Hello, world! This is a toast message.",
    type: "default",
    imgSrc: "/images/logo/polytronx-dark.svg",
  },
  {
    title: "Custom Content Toast",
    message: "Hello, world! This is a toast message.",
    type: "custom",
  },
  {
    title: "Primary Toast",
    message: "Hello, world! This is a toast message.",
    type: "primary",
  },
];

const DefaultToast = () => {
  const [show, setShow] = useState(false);
  const [visibleToasts, setVisibleToasts] = useState({
    default: true,
    custom: true,
    primary: true,
  });

  const closeToast = (toastType: string) => {
    setVisibleToasts((prev) => ({
      ...prev,
      [toastType]: false,
    }));
  };

  return (
    <div>
      <Col xs={12}>
        <Card>
          <CardHeader>
            <h5>Reactstrap Toasts</h5>
          </CardHeader>
          <CardBody>
            <Row>
              {toastData.map((toast) => (
                <Col key={toast.title} lg={4} className="mb-4 mb-lg-0">
                  {toast.type === "default" && visibleToasts.default && (
                    <Toast className="d-block b-1-primary bg-light-primary">
                      <ToastHeader>
                        <img
                          src={toast.imgSrc}
                          className="rounded me-2"
                          alt="logo"
                          width="30"
                          height="30"
                        />
                        <strong className="me-auto">PolytronX</strong>
                        <Button close onClick={() => closeToast("default")} />
                      </ToastHeader>
                      <ToastBody>{toast.message}</ToastBody>
                    </Toast>
                  )}

                  {toast.type === "custom" && visibleToasts.custom && (
                    <Toast className="d-block b-1-secondary">
                      <ToastBody className="text-secondary f-w-600">
                        {toast.message}
                        <div className="mt-2 pt-2 border-top">
                          <Button color="light-primary">Take action</Button>{" "}
                          <Button
                            color="light-secondary"
                            onClick={() => closeToast("custom")}
                          >
                            Close
                          </Button>
                        </div>
                      </ToastBody>
                    </Toast>
                  )}

                  {toast.type === "primary" && visibleToasts.primary && (
                    <Toast
                      className={`d-block bg-${toast.type} text-white border-0`}
                    >
                      <ToastBody className="d-flex justify-content-between align-items-center">
                        {toast.message}
                        <Button
                          close
                          className="btn-close-white"
                          onClick={() => closeToast("primary")}
                        />
                      </ToastBody>
                    </Toast>
                  )}
                </Col>
              ))}
            </Row>
          </CardBody>
        </Card>
      </Col>
      <Col xs={12}>
        <Card>
          <CardHeader>
            <h5>Placement Toasts</h5>
          </CardHeader>
          <CardBody>
            <Button color="light-primary" onClick={() => setShow(true)}>
              Show Toast
            </Button>
            <div className="mt-3">
              <Toast isOpen={show} className="mt-3 ">
                <ToastHeader
                  className="bg-light-primary border-primary border-opacity-25"
                  toggle={() => setShow(false)}
                >
                  Toast Header
                </ToastHeader>
                <ToastBody>Some text inside the toast body</ToastBody>
              </Toast>
            </div>
          </CardBody>
        </Card>
      </Col>
    </div>
  );
};

export default DefaultToast;
