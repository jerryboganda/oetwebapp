import React from "react";
import { Card, CardHeader, CardBody, Button, Col } from "reactstrap";
import { IconCode, IconCapture, IconBellRinging } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const iconData = [
  { btnClass: "btn-primary", icon: "capture" },
  { btnClass: "btn-secondary", icon: "bell-ringing" },
  { btnClass: "btn-outline-primary", icon: "capture" },
  { btnClass: "btn-outline-secondary", icon: "bell-ringing" },
  { btnClass: "btn-light-primary", icon: "capture" },
  { btnClass: "btn-light-secondary", icon: "bell-ringing" },
];

const socialButtonData = [
  { icon: "facebook", className: "btn btn-facebook" },
  { icon: "twitter", className: "btn btn-twitter" },
  { icon: "pinterest", className: "btn btn-pinterest" },
  { icon: "linkedin", className: "btn btn-linkedin" },
  { icon: "reddit", className: "btn btn-reddit" },
  { icon: "whatsapp", className: "btn btn-whatsapp" },
  { icon: "gmail", className: "btn btn-gmail" },
  { icon: "telegram", className: "btn btn-telegram" },
  { icon: "youtube", className: "btn btn-youtube" },
  { icon: "vimeo", className: "btn btn-vimeo" },
  { icon: "behance", className: "btn btn-behance" },
  { icon: "github", className: "btn btn-github" },
  { icon: "skype", className: "btn btn-skype" },
  { icon: "snapchat", className: "btn btn-snapchat" },
];

const ButtonsSection = () => {
  const iconMap: Record<string, JSX.Element> = {
    capture: <IconCapture size={18} />,
    "bell-ringing": <IconBellRinging size={18} />,
  };

  const prismIconCode = `<div className="app-btn-list">
${iconData
  .map(
    (
      item
    ) => `  <Button type="button" className="btn ${item.btnClass} icon-btn b-r-4">
    <Icon${item.icon
      .split("-")
      .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
      .join("")} size={18} />
  </Button>`
  )
  .join("\n")}
</div>`;

  const prismSocialCode = `<div className="app-btn-list">
${socialButtonData
  .map(
    (
      btn
    ) => `  <Button type="button" className="${btn.className} icon-btn b-r-22">
    <IconBrand${btn.icon.charAt(0).toUpperCase() + btn.icon.slice(1)} size={18} className="text-white" />
  </Button>`
  )
  .join("\n")}
</div>`;

  return (
    <>
      {/* Icon Buttons Section */}
      <Col xl={4}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Icon Buttons</h5>
            <a href="#" id="toggleIconBtns">
              <IconCode className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="app-btn-list">
              {iconData.map((item, index) => (
                <Button
                  color={"primary"}
                  key={index}
                  type="button"
                  className={`btn ${item.btnClass} icon-btn b-r-4`}
                >
                  {iconMap[item.icon]}
                </Button>
              ))}
            </div>
            <UncontrolledCollapseWrapper toggler="#toggleIconBtns">
              <pre className="language-html mt-3 mb-0">
                <code className="language-html">{prismIconCode}</code>
              </pre>
            </UncontrolledCollapseWrapper>
          </CardBody>
        </Card>
      </Col>

      {/* Social Buttons Section */}
      <Col xl={8}>
        <Card>
          <CardHeader className="code-header d-flex justify-content-between align-items-center">
            <h5 className="mb-0">Social Buttons</h5>
            <a href="#" id="toggleSocialBtns">
              <IconCode className="source" size={32} />
            </a>
          </CardHeader>
          <CardBody>
            <div className="app-btn-list">
              {socialButtonData.map((btn, index) => {
                const iconName =
                  "IconBrand" +
                  btn.icon.charAt(0).toUpperCase() +
                  btn.icon.slice(1);
                const BrandIcon =
                  require("@tabler/icons-react")[iconName] || (() => null);
                return (
                  <Button
                    key={index}
                    type="button"
                    className={`${btn.className} icon-btn b-r-22`}
                  >
                    <BrandIcon size={18} className="text-white" />
                  </Button>
                );
              })}
            </div>
            <UncontrolledCollapseWrapper toggler="#toggleSocialBtns">
              <pre className="language-html mt-3 mb-0">
                <code className="language-html">{prismSocialCode}</code>
              </pre>
            </UncontrolledCollapseWrapper>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default ButtonsSection;
