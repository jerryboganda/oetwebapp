import { Col, Card, CardHeader, CardBody } from "reactstrap";
import { IconCode, IconDownload } from "@tabler/icons-react";
import { useState } from "react";

const buttonSizes = [
  {
    color: "primary",
    size: "xxl",
    icon: <IconDownload size={18} />,
    label: "Primary",
  },
  {
    color: "primary",
    size: "xl",
    icon: <IconDownload size={18} />,
    label: "Primary",
  },
  {
    color: "secondary",
    size: "lg",
    icon: <i className="ti ti-butterfly"></i>,
    label: "Secondary",
  },
  {
    color: "success",
    size: "",
    icon: <i className="ti ti-award"></i>,
    label: "Success",
  },
  {
    color: "danger",
    size: "sm",
    icon: <i className="ti ti-power"></i>,
    label: "Danger",
  },
  {
    color: "warning",
    size: "xs",
    icon: <i className="ti ti-alert-triangle"></i>,
    label: "Warning",
  },
];

const ButtonSizes = () => {
  const [isCodeVisible, setIsCodeVisible] = useState(false);
  return (
    <Col xs={12}>
      <Card>
        <CardHeader className="code-header">
          <h5>Button with Sizes</h5>
          <a
            href="#buttonsizesexample"
            onClick={(e) => {
              e.preventDefault();
              setIsCodeVisible(!isCodeVisible);
            }}
          >
            <IconCode data-source="btnsize" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody>
          <div className="app-btn-list">
            {buttonSizes.map((button, index) => (
              <button
                key={index}
                type="button"
                className={`btn btn-${button.color} ${button.size ? `btn-${button.size}` : ""}`}
              >
                {button.icon} {button.label}
              </button>
            ))}
          </div>
        </CardBody>
        <pre
          className={`btnsize mt-3 ${isCodeVisible ? "show" : "collapse"}`}
          id="buttonsizesexample"
        >
          <code className="language-html">
            {`
<div class="app-btn-list">
  ${buttonSizes
    .map(
      (button) => `
    <button type="button" class="btn btn-${button.color} ${button.size ? "btn-" + button.size : ""}">
      ${button.icon} ${button.label}
    </button>
  `
    )
    .join("\n")}
</div>
            `}
          </code>
        </pre>
      </Card>
    </Col>
  );
};

export default ButtonSizes;
