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

const outlineAvatarColors = [
  { colorClass: "primary", icon: <FontAwesomeIcon icon={faUser} /> },
  { colorClass: "secondary", icon: <FontAwesomeIcon icon={faUser} /> },
  { colorClass: "success", icon: <FontAwesomeIcon icon={faUser} /> },
  { colorClass: "info", icon: <FontAwesomeIcon icon={faUser} /> },
  { colorClass: "warning", icon: "AR" },
  { colorClass: "danger", icon: "TE" },
];

const AvatarOutline = () => {
  return (
    <Col md={6}>
      <Card className="equal-card">
        <CardHeader className="code-header">
          <h5>Outline</h5>
          <a href="#" id="togglerAvOutlineBtn">
            <IconCode data-source="av2" className="source" size={32} />
          </a>
          <p className="text-muted">
            For outline style use <code>text-outline-*</code> class.
          </p>
        </CardHeader>
        <CardBody>
          <div className="d-flex gap-3 flex-wrap">
            {outlineAvatarColors.map((avatar, index) => (
              <span
                key={index}
                className={`text-outline-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50`}
              >
                {avatar.icon}
              </span>
            ))}
          </div>
        </CardBody>
        <UncontrolledCollapse toggler="#togglerAvOutlineBtn">
          <pre className="mt-3">
            <code className="language-html">
              {`<div className="card equal-card">
  <div className="card-header">
    <h5>Outline</h5>
    <p className="text-muted">For outline style use <code>text-outline-*</code> class.</p>
  </div>
  <div className="card-body">
    <div className="d-flex gap-3 flex-wrap">
${outlineAvatarColors
  .map(
    (
      avatar
    ) => `      <span className="text-outline-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50">
        ${avatar.icon}
      </span>`
  )
  .join("\n")}
    </div>
  </div>
</div>`}
            </code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default AvatarOutline;
