import React from "react";
import {
  Col,
  Card,
  CardHeader,
  CardBody,
  UncontrolledCollapse,
} from "reactstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faUser } from "@fortawesome/free-solid-svg-icons";
import { IconCode, IconMail } from "@tabler/icons-react";

const avatarImages = [
  { src: "/images/avatar/1.png", bg: "primary" },
  { src: "/images/avatar/2.png", bg: "secondary" },
  { src: "/images/avatar/3.png", bg: "success" },
  { src: "/images/avatar/4.png", bg: "info" },
  { src: "/images/avatar/5.png", bg: "success" },
  { src: "/images/avatar/6.png", bg: "danger" },
];

const avatarSizes = [
  { size: 30, bg: "secondary" },
  { size: 35, bg: "success" },
  { size: 40, bg: "info" },
  { size: 45, bg: "warning" },
  { size: 50, bg: "danger" },
];
const indicatorAvatars = [
  {
    type: "icon",
    icon: <FontAwesomeIcon icon={faUser} />,
    bg: "danger",
    indicatorPosition: "top-0 start-1",
    indicatorBg: "danger",
  },
  {
    type: "icon",
    icon: <FontAwesomeIcon icon={faUser} />,
    bg: "success",
    indicatorPosition: "bottom-0 start-1",
    indicatorBg: "success",
  },
  {
    type: "icon",
    icon: <FontAwesomeIcon icon={faUser} />,
    bg: "warning",
    indicatorPosition: "bottom-0 end-0",
    indicatorBg: "warning",
  },
  {
    type: "image",
    src: "/images/avatar/1.png",
    bg: "secondary",
    indicatorPosition: "top-0 end-0",
    indicatorBg: "success",
  },
  {
    type: "image",
    src: "/images/avatar/4.png",
    bg: "secondary",
    indicatorPosition:
      "top-0 end-0 d-flex-center bg-warning border-light text-center h-20 w-20 f-s-10 start-30",
    indicatorText: "3",
  },
  {
    type: "image",
    src: "/images/avatar/6.png",
    bg: "secondary",
    indicatorPosition:
      "top-0 d-flex-center bg-danger border border-light text-center h-20 w-20 f-s-10 start-30",
    indicatorIcon: <IconMail size={18} />,
  },
];

const AvatarShowcase = () => {
  return (
    <>
      {/* Images Section */}
      <Col md={6}>
        <Card>
          <CardHeader className="code-header">
            <h5>Images</h5>
            <a href="#" id="togglerAvImageBtn">
              <IconCode data-source="av2" className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="d-flex gap-3 flex-wrap">
              {avatarImages.map(({ src, bg }, i) => (
                <div
                  key={i}
                  className={`h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-${bg}`}
                >
                  <img
                    src={src}
                    alt={`Avatar ${i + 1}`}
                    className="img-fluid"
                  />
                </div>
              ))}
            </div>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerAvImageBtn">
            <pre>
              <code className="language-html">
                {`<div className="d-flex gap-3 flex-wrap">
${avatarImages
  .map(
    ({
      src,
      bg,
    }) => `  <div className="h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-${bg}">
    <img src="${src}" alt="" className="img-fluid">
  </div>`
  )
  .join("\n")}
</div>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>

      {/* Size Section */}
      <Col md={6}>
        <Card className="equal-card">
          <CardHeader className="code-header">
            <h5>Size</h5>
            <a href="#" id="togglerAvSizeBtn">
              <IconCode data-source="av2" className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="d-flex align-items-center">
              <div className="d-flex gap-3 flex-wrap">
                {avatarSizes.map(({ size, bg }, i) => (
                  <span
                    key={i}
                    className={`bg-${bg} h-${size} w-${size} d-flex-center b-r-50`}
                  >
                    <FontAwesomeIcon icon={faUser} />
                  </span>
                ))}
              </div>
            </div>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerAvSizeBtn">
            <pre>
              <code className="language-html">
                {`<div className="d-flex gap-3 flex-wrap">
${avatarSizes
  .map(
    ({
      size,
      bg,
    }) => `  <span className="bg-${bg} h-${size} w-${size} d-flex-center b-r-50">
    <i className="fa fa-user"></i>
  </span>`
  )
  .join("\n")}
</div>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>

      <Col md={5}>
        <Card>
          <CardHeader className="code-header">
            <h5>Indicator Position And Icon</h5>
            <a href="#" id="togglerAvIndiBtn">
              <IconCode data-source="av2" className="source" size={32} />
            </a>
            <p className="text-muted">
              Radious avatar text in soft color example
            </p>
          </CardHeader>
          <CardBody>
            <div className="d-flex gap-3 flex-wrap">
              {indicatorAvatars.map((avatar, i) => (
                <span
                  key={i}
                  className={`bg-${avatar.bg} h-45 w-45 d-flex-center b-r-50 position-relative`}
                >
                  {avatar.type === "icon" && avatar.icon}
                  {avatar.type === "image" && (
                    <img src={avatar.src} alt="" className="img-fluid b-r-50" />
                  )}
                  <span
                    className={`position-absolute ${avatar.indicatorPosition} p-1 rounded-circle ${
                      avatar.indicatorText || avatar.indicatorIcon
                        ? ""
                        : `bg-${avatar.indicatorBg} border border-light`
                    }`}
                  >
                    {avatar.indicatorText ?? avatar.indicatorIcon ?? ""}
                  </span>
                </span>
              ))}
            </div>
          </CardBody>
          <UncontrolledCollapse toggler="#togglerAvIndiBtn">
            <pre>
              <code className="language-html">
                {`<div className="d-flex gap-3 flex-wrap">
${indicatorAvatars
  .map(
    ({
      bg,
      type,
      src,
      indicatorPosition,
      indicatorText,
      indicatorIcon,
      indicatorBg,
    }) => {
      const indicatorContent = indicatorText
        ? indicatorText
        : indicatorIcon
          ? "&lt;IconMail /&gt;"
          : "";

      return `  <span className="bg-${bg} h-45 w-45 d-flex-center b-r-50 position-relative">
    ${
      type === "icon"
        ? '&lt;i className="fa fa-user"&gt;&lt;/i&gt;'
        : `&lt;img src="${src}" alt="" className="img-fluid b-r-50"&gt;`
    }
    <span className="position-absolute ${indicatorPosition} ${
      indicatorContent ? "" : `p-1 bg-${indicatorBg} border border-light`
    } rounded-circle">${indicatorContent}</span>
  </span>`;
    }
  )
  .join("\n")}
</div>`}
              </code>
            </pre>
          </UncontrolledCollapse>
        </Card>
      </Col>
    </>
  );
};

export default AvatarShowcase;
