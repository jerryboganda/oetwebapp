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

const radiusAvatarColors = [
  {
    radius: 0,
    colorClass: "light-primary",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
  {
    radius: 6,
    colorClass: "light-secondary",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
  {
    radius: 10,
    colorClass: "light-success",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
  {
    radius: 14,
    colorClass: "light-info",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
  {
    radius: 20,
    colorClass: "light-warning",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
  {
    radius: 30,
    colorClass: "light-danger",
    raw: "<FontAwesomeIcon icon={faUser} />",
  },
];

const AvatarRadius = () => {
  return (
    <Col md={6}>
      <Card>
        <CardHeader className="code-header">
          <h5>Radius</h5>
          <a href="#" id="togglerAvRadiusBtn">
            <IconCode data-source="av8" className="source" size={32} />
          </a>
          <p className="text-muted">Radius avatar text in soft color example</p>
        </CardHeader>
        <CardBody>
          <div className="d-flex gap-3 flex-wrap">
            {radiusAvatarColors.map((avatar, index) => (
              <span
                key={index}
                className={`text-${avatar.colorClass} h-45 w-45 d-flex-center b-r-${avatar.radius}`}
              >
                <FontAwesomeIcon icon={faUser} />
              </span>
            ))}
          </div>
        </CardBody>
        <UncontrolledCollapse toggler="#togglerAvRadiusBtn">
          <pre className="mt-3">
            <code className="language-html">
              {`<Card>
  <CardHeader>
    <h5>Radius</h5>
    <p className="text-muted">
      Radius avatar text in soft color example
    </p>
  </CardHeader>
  <CardBody>
    <div className="d-flex gap-3 flex-wrap">
${radiusAvatarColors
  .map(
    (
      avatar
    ) => `      <span className="text-${avatar.colorClass} h-45 w-45 d-flex-center b-r-${avatar.radius}">
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
export default AvatarRadius;
