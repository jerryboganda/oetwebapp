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

const avatarColors = [
  {
    colorClass: "primary",
    content: <FontAwesomeIcon icon={faUser} />,
    raw: "FontAwesomeIcon icon={faUser}",
  },
  {
    colorClass: "secondary",
    content: <FontAwesomeIcon icon={faUser} />,
    raw: "FontAwesomeIcon icon={faUser}",
  },
  {
    colorClass: "success",
    content: <FontAwesomeIcon icon={faUser} />,
    raw: "FontAwesomeIcon icon={faUser}",
  },
  {
    colorClass: "info",
    content: <FontAwesomeIcon icon={faUser} />,
    raw: "FontAwesomeIcon icon={faUser}",
  },
  { colorClass: "warning", content: "P", raw: "P" },
  { colorClass: "danger", content: "AD", raw: "AD" },
];

const AvatarColors = () => {
  return (
    <Col md={6}>
      <Card>
        <CardHeader className="code-header">
          <h5>Colors</h5>
          <a href="#" id="togglerBlockBtn">
            <IconCode data-source="blockbtn" className="source" size={32} />
          </a>
          <p className="text-muted">
            Use color <code>bg-*</code> to change the background theme color of
            avatar.
          </p>
        </CardHeader>
        <CardBody>
          <div className="d-flex gap-3 flex-wrap">
            {avatarColors.map((avatar, index) => (
              <span
                key={index}
                className={`bg-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50`}
              >
                {avatar.content}
              </span>
            ))}
          </div>
        </CardBody>
        <UncontrolledCollapse toggler="#togglerBlockBtn">
          <pre>
            <code className="language-html">
              {`<Card>
  <CardHeader>
    <h5>Colors</h5>
    <p className="text-muted">
      Use color <code>bg-*</code> to change the background theme color of avatar.
    </p>
  </CardHeader>
  <CardBody>
    <div className="d-flex gap-3 flex-wrap">
${avatarColors
  .map(
    (
      avatar
    ) => `<span className="bg-${avatar.colorClass} h-45 w-45 d-flex-center b-r-50">
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

export default AvatarColors;
