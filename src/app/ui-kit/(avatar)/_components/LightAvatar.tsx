import {
  Col,
  Card,
  CardHeader,
  CardBody,
  UncontrolledCollapse,
} from "reactstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faUser } from "@fortawesome/free-solid-svg-icons";
import { IconCode } from "@tabler/icons-react";

const lightAvatarColors = [
  { colorClass: "light-primary", raw: "<FontAwesomeIcon icon={faUser} />" },
  { colorClass: "light-secondary", raw: "<FontAwesomeIcon icon={faUser} />" },
  { colorClass: "light-success", raw: "<FontAwesomeIcon icon={faUser} />" },
  { colorClass: "light-info", raw: "<FontAwesomeIcon icon={faUser} />" },
  { colorClass: "light-warning", raw: "<FontAwesomeIcon icon={faUser} />" },
  { colorClass: "light-danger", raw: "<FontAwesomeIcon icon={faUser} />" },
];

const AvatarLightColors = () => {
  return (
    <Col md={6}>
      <Card>
        <CardHeader className="code-header">
          <h5>Light Background</h5>
          <a href="#" id="togglerAvLightBtn">
            <IconCode data-source="av5" className="source" size={32} />
          </a>
          <p className="text-muted">
            For light style use <code>text-light-*</code> class.
          </p>
        </CardHeader>
        <CardBody>
          <div className="d-flex gap-3 flex-wrap">
            {lightAvatarColors.map((avatar, index) => (
              <span
                key={index}
                className={`text-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50`}
              >
                <FontAwesomeIcon icon={faUser} />
              </span>
            ))}
          </div>
        </CardBody>
        <UncontrolledCollapse toggler="#togglerAvLightBtn">
          <pre className="mt-3">
            <code className="language-html">
              {`<Card>
  <CardHeader>
    <h5>Light Background</h5>
    <p className="text-muted">
      For light style use <code>text-light-*</code> class.
    </p>
  </CardHeader>
  <CardBody>
    <div className="d-flex gap-3 flex-wrap">
${lightAvatarColors
  .map(
    (
      avatar
    ) => `      <span className="text-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50">
        ${avatar.raw}
      </span>`
  )
  .join("\n")}
    </div>
  </CardBody>
</Card>`}
            </code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default AvatarLightColors;
