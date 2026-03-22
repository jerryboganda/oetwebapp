import React, { useState } from "react";
import { Code, Info, ShoppingCartSimple, Warning } from "phosphor-react";
import { Card, CardBody, CardHeader } from "react-bootstrap";
import "prismjs/themes/prism.css";

const alertDismissing = [
  {
    type: "basic",
    color: "primary",
    image: "/images/icons/cookie-.png",
    message:
      "We have Cookies! We use it to ensure you get the best experience on our website and service",
    buttons: [
      <button
        className="btn-close"
        data-bs-dismiss="alert"
        aria-label="Close"
        key="btn"
      />,
    ],
  },
  {
    type: "label",
    color: "success",
    icon: <ShoppingCartSimple size={16} />,
    message: "Your order is delayed",
    buttons: [
      <button
        className="btn btn-sm btn-success"
        data-bs-dismiss="alert"
        key="btn"
      >
        Accept
      </button>,
    ],
  },
  {
    type: "border",
    color: "warning",
    icon: <Info size={16} className="me-2 text-info" />,
    // title: (
    //   <h5 className="mb-0 d-inline" key="title">
    //     New Version is now available
    //   </h5>
    // ),
    title: "New Version is now available",
    message:
      "With this new Version you have accesss to more customization features and file export options.",
    buttons: [
      <a
        key="disallow"
        href="#"
        className="link-primary text-d-underline"
        data-bs-dismiss="alert"
      >
        Don&#39;t allow
      </a>,
      <a key="allow" href="#" className="link-primary text-d-underline ms-2">
        Allow
      </a>,
    ],
  },
  {
    type: "custom",
    color: "warning",
    icon: (
      <Warning size={25} weight="fill" className="align-middle text-warning" />
    ),
    title: "Under maintenance",
    message:
      "Our team is currently checking some errors in this area. We don't recommend changing any of your settings until the next update.",
    buttons: [
      <a href="#" key="btn" className="btn btn-warning">
        Get more info
      </a>,
    ],
  },
];

const CustomAlert: React.FC = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  const [visibleAlerts, setVisibleAlerts] = useState<boolean[]>(
    alertDismissing.map(() => true)
  );
  const dismissAlert = (index: number) => {
    setVisibleAlerts((prev) => {
      const newVisible = [...prev];
      newVisible[index] = false;
      return newVisible;
    });
  };
  return (
    <Card className="equal-card">
      <CardHeader className="code-header d-flex justify-content-between align-items-center">
        <h5 className="txt-ellipsis mb-0">Custom Alerts With Dismissing</h5>
        <a
          href="#customeralertExample"
          onClick={(e) => {
            e.preventDefault();
            setIsCodeVisible(!isCodeVisible);
          }}
        >
          <Code size={30} weight="bold" className="source" />
        </a>
      </CardHeader>

      <CardBody>
        {alertDismissing.map((alert, index) => {
          if (!visibleAlerts[index]) return null;
          if (alert.type === "custom") {
            return (
              <div className="alert custom-alert p-0" role="alert" key={index}>
                <div className="alert-header">
                  <button
                    type="button"
                    className="btn-close"
                    onClick={() => dismissAlert(3)}
                    aria-label="Close"
                  />
                </div>
                <div className="alert-body">
                  <h3 className="mb-3 text-center">
                    {alert.icon} {alert.title}
                  </h3>
                  <p className="mb-3">{alert.message}</p>
                  <div className="text-end">{alert.buttons}</div>
                </div>
              </div>
            );
          }

          return (
            <div
              key={index}
              className={`alert ${
                alert.type === "label"
                  ? `alert-label alert-label-${alert.color}`
                  : alert.type === "border"
                    ? `alert-border-${alert.color}`
                    : `alert-${alert.color} alert-dismissible`
              }`}
              role="alert"
            >
              {alert.type === "basic" && (
                <div className="d-flex justify-content-between">
                  {alert.image && (
                    <img
                      src={alert.image}
                      className="w-35 h-35 me-2"
                      alt="icon"
                    />
                  )}
                  <p className="mb-0">{alert.message}</p>
                  {alert.buttons}
                </div>
              )}

              {alert.type === "label" && (
                <div className="d-flex justify-content-between align-items-center w-100">
                  <p className="mb-0 d-flex align-items-center gap-2">
                    <span className={`label-icon label-icon-${alert.color}`}>
                      {alert.icon}
                    </span>
                    {alert.message}
                  </p>
                  {alert.buttons}
                </div>
              )}

              {alert.type === "border" && (
                <>
                  <h6 className="d-flex align-items-center">
                    {alert.icon} {alert.title}
                  </h6>
                  <p>{alert.message}</p>
                  <div className="text-end">{alert.buttons}</div>
                </>
              )}
            </div>
          );
        })}

        <pre
          className={`custome mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="customeralertExample"
        >
          <code className="language-html">
            $
            {alertDismissing
              .map((alert) => {
                switch (alert.type) {
                  case "basic":
                    return `    <Alert color="${alert.color}" dismissible>
      <div className="d-flex justify-content-between">
        <img src="${alert.image}" className="w-35 h-35 me-2" alt="icon" />
        <p className="mb-0">${alert.message}</p>
        <button type="button" className="btn-close" aria-label="Close"></button>
      </div>
    </Alert>`;

                  case "label":
                    return `    <Alert className="alert-label alert-label-${alert.color}">
      <div className="d-flex justify-content-between align-items-center w-100">
        <p className="mb-0 d-flex align-items-center gap-2">
          <span className="label-icon label-icon-${alert.color}">
            {/* Icon Placeholder */}
          </span>
          ${alert.message}
        </p>
        <button className="btn btn-sm btn-${alert.color}" >Accept</button>
      </div>
    </Alert>`;

                  case "border":
                    return `    <Alert className="alert-border-${alert.color}">
      <h6>
        {/* Icon Placeholder */} ${
          Array.isArray(alert.title)
            ? alert.title[0].props.children.join(" ")
            : alert.title
        }
      </h6>
      <p>${alert.message}</p>
      <div className="text-end">
        <a href="#" className="link-primary text-d-underline" >Don't allow</a>
        <a href="#" className="link-primary text-d-underline ms-2">Allow</a>
      </div>
    </Alert>`;

                  case "custom":
                    return `    <Alert className="custom-alert p-0">
      <div className="alert-header">
        <button type="button" className="btn-close" aria-label="Close"></button>
      </div>
      <div className="alert-body">
        <h3 className="mb-3 text-center">
          {/* Icon Placeholder */} ${alert.title}
        </h3>
        <p className="mb-3">${alert.message}</p>
        <div className="text-end">
          <a href="#" className="btn btn-${alert.color}">Get more info</a>
        </div>
      </div>
    </Alert>`;

                  default:
                    return "";
                }
              })
              .join("\n")}
          </code>
        </pre>
      </CardBody>
    </Card>
  );
};

export default CustomAlert;
