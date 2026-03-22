import React from "react";
import { Card, CardHeader, CardBody, Row, Col, Button } from "reactstrap";
import {
  IconCode,
  IconBrandFacebook,
  IconBrandTwitter,
  IconBrandPinterest,
  IconBrandLinkedin,
  IconBrandReddit,
} from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";

const buttonData = [
  [
    {
      label: "Facebook",
      icon: IconBrandFacebook,
      iconName: "IconBrandFacebook",
      className: "btn btn-facebook b-r-22 text-white d-inline-flex-center",
    },
    {
      label: "Twitter",
      icon: IconBrandTwitter,
      iconName: "IconBrandTwitter",
      className: "btn btn-twitter b-r-22 text-white d-inline-flex-center",
    },
  ],
  [
    {
      label: "Pinterest",
      icon: IconBrandPinterest,
      iconName: "IconBrandPinterest",
      className: "btn btn-outline-pinterest b-r-22 d-inline-flex-center",
    },
    {
      label: "Linkedin",
      icon: IconBrandLinkedin,
      iconName: "IconBrandLinkedin",
      className: "btn btn-outline-linkedin b-r-22 d-inline-flex-center",
    },
  ],
  [
    {
      label: "Reddit",
      icon: IconBrandReddit,
      iconName: "IconBrandReddit",
      className: "btn btn-light-reddit b-r-22 d-inline-flex-center",
    },
    {
      label: "Twitter",
      icon: IconBrandTwitter,
      iconName: "IconBrandTwitter",
      className: "btn btn-light-twitter b-r-22 d-inline-flex-center",
    },
  ],
];

// Generate Prism preview code with JSX-style Tabler icons
const prismCode = `<div class="row">
${buttonData
  .map((column) => {
    const btns = column
      .map(
        (btn) => `    <button type="button" class="${btn.className}">
      <${btn.iconName} size={18} className="me-1" /> ${btn.label}
    </button>`
      )
      .join("\n");
    return `  <div class="col-md-6 col-lg-4 col-12">\n${btns}\n  </div>`;
  })
  .join("\n")}
</div>`;

const SocialButtons = () => {
  return (
    <Col xs={12}>
      <Card className="button-view">
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5 className="mb-0">Social Buttons</h5>
          <a href="#" id="toggleSocialBtn">
            <IconCode className="source" size={32} />
          </a>
        </CardHeader>

        <CardBody>
          <Row>
            {buttonData.map((column, colIndex) => (
              <Col
                md={6}
                lg={4}
                xs={12}
                key={colIndex}
                className="app-btn-list mb-3"
              >
                {column.map(({ icon: Icon, label, className }, btnIndex) => (
                  <Button key={btnIndex} type="button" className={className}>
                    <Icon size={18} className="me-1" />
                    {label}
                  </Button>
                ))}
              </Col>
            ))}
          </Row>
        </CardBody>

        <UncontrolledCollapseWrapper toggler="#toggleSocialBtn">
          <pre className="language-html mt-3 mb-0">
            <code className="language-html">{prismCode}</code>
          </pre>
        </UncontrolledCollapseWrapper>
      </Card>
    </Col>
  );
};

export default SocialButtons;
