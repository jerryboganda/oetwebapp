import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Toast,
  ToastBody,
  ToastHeader,
} from "reactstrap";

interface Notification {
  id: number;
  type: string;
  message: string;
  ButtonColor: string;
}

const NotificationPositions = [
  { label: "Top", value: "top", background: "primary" },
  { label: "Left", value: "left", background: "success" },
  { label: "Right", value: "right", background: "info" },
  { label: "Bottom", value: "bottom", background: "warning" },
  { label: "Center", value: "center", background: "secondary" },
];

export const notificationsVariantsData: Notification[] = [
  {
    id: 1,
    type: "primary",
    message: "This is a Primary toast message.",
    ButtonColor: "light-primary",
  },
  {
    id: 2,
    type: "secondary",
    message: "This is a Secondary toast message.",
    ButtonColor: "light-secondary",
  },
  {
    id: 3,
    type: "success",
    message: "This is a Success toast message.",
    ButtonColor: "light-success",
  },
  {
    id: 4,
    type: "danger",
    message: "This is a Danger toast message.",
    ButtonColor: "light-danger",
  },
  {
    id: 5,
    type: "warning",
    message: "This is a Warning toast message.",
    ButtonColor: "light-warning",
  },
  {
    id: 6,
    type: "info",
    message: "This is an Info toast message.",
    ButtonColor: "light-info",
  },
  {
    id: 7,
    type: "light",
    message: "This is a Light toast message.",
    ButtonColor: "light-light",
  },
  {
    id: 8,
    type: "dark",
    message: "This is a Dark toast message.",
    ButtonColor: "light-dark",
  },
];

interface VisibleToast {
  key: string;
  id: number;
  position: "top" | "bottom" | "left" | "right" | "center";
  type?: string;
  message?: string;
}

const NotificationVariants = () => {
  const [visibleToasts, setVisibleToasts] = useState<VisibleToast[]>([]);

  const handleShowToast = (
    id: number,
    position: "top" | "bottom" | "left" | "right" | "center" = "top",
    type = "",
    message = ""
  ) => {
    const uniqueKey = `${id}-${new Date().getTime()}`;
    setVisibleToasts((prev) => [
      ...prev,
      { key: uniqueKey, id, position, type, message },
    ]);
    setTimeout(() => handleCloseToast(uniqueKey), 3000);
  };

  const handleCloseToast = (key: string) => {
    setVisibleToasts((prev) => prev.filter((t) => t.key !== key));
  };

  const getToastContainerClasses = (position: string) => {
    switch (position) {
      case "top":
        return "toast-container position-fixed top-0 start-50 translate-middle-x p-3";
      case "bottom":
        return "toast-container position-fixed bottom-0 start-50 translate-middle-x p-3";
      case "left":
        return "toast-container position-fixed top-0 start-20 p-3";
      case "right":
        return "toast-container position-fixed top-0 end-0 p-3";
      case "center":
        return "toast-container position-fixed top-0 start-50 translate-middle-x p-3";
      default:
        return "toast-container position-fixed top-0 end-0 p-3";
    }
  };

  return (
    <>
      <Col xs={12}>
        <Card>
          <CardHeader className="code-header">
            <h5>Position Notification</h5>
            <p>
              It is Very Easy to Customize, and it is used in website
              applications.
            </p>
          </CardHeader>
          <CardBody>
            <div className="d-flex flex-wrap gap-2">
              {NotificationPositions.map((pos, idx) => (
                <Button
                  key={pos.value}
                  color="light-primary"
                  onClick={() =>
                    handleShowToast(
                      idx + 100,
                      pos.value as
                        | "left"
                        | "center"
                        | "right"
                        | "top"
                        | "bottom",
                      pos.background,
                      `This is a ${pos.label} toast message`
                    )
                  }
                >
                  {pos.label}
                </Button>
              ))}
            </div>
          </CardBody>
        </Card>
      </Col>

      <Col xs={12}>
        <Card>
          <CardHeader className="code-header">
            <h5>Color Notification</h5>
            <p>
              It is Very Easy to Customize, and it uses in website application.
            </p>
          </CardHeader>
          <CardBody>
            <div className="d-flex flex-wrap gap-2">
              {notificationsVariantsData.map((toast) => (
                <Button
                  key={toast.id}
                  color={toast.ButtonColor}
                  onClick={() =>
                    handleShowToast(toast.id, "top", toast.type, toast.message)
                  }
                >
                  {toast.type.charAt(0).toUpperCase() + toast.type.slice(1)}
                </Button>
              ))}
            </div>

            {["top", "bottom", "left", "right", "center"].map((pos) => (
              <div key={pos} className={getToastContainerClasses(pos)}>
                {visibleToasts
                  .filter((t) => t.position === pos)
                  .map((t) => (
                    <Toast
                      key={t.key}
                      className={`bg-${t.type} text-white mb-2`}
                    >
                      {t.id < 100 && (
                        <ToastHeader toggle={() => handleCloseToast(t.key)}>
                          {(t.type || "primary").charAt(0).toUpperCase() +
                            (t.type || "primary").slice(1)}
                        </ToastHeader>
                      )}
                      <ToastBody>{t.message}</ToastBody>
                    </Toast>
                  ))}
              </div>
            ))}
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default NotificationVariants;
